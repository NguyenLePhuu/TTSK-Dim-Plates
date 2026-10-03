// Arrange các section thường; hình học tránh vật cản và ngữ cảnh bố cục Shape đứng.
// LUỒNG: Run → ResolveTargets → ArrangeRegularSectionViews/PHU_SectionLayoutPlanner.Build → cập nhật vị trí và kiểm tra kết quả.
// ĐIỂM SỬA: chọn view ở ResolveTargets; xếp hàng/cột ở planner; khung/vật cản ở PHU_ArrangePresentation.Read.
// THAM SỐ: gap, MIN_EDGE_SAFE tính bằng mm giấy; sectionHorizontal/verticalBottomUp/alignedGrid chọn cách xếp.
// LƯU Ý: mainHorizontal còn trong chữ ký tương thích; Run hiện chỉ di chuyển section thường, không xếp lại main view.
// TRA CỨU: PROJECT_MAP.md ở gốc repo; chỉ mục kiểu/hàm và điểm chỉnh thông số.
// Mục lục:
//   PHU_ArrangeView.cs
//   PHU_SectionLayoutPlanner.cs
//   PHU_ArrangePresentation.cs
//   PHU_VerticalShapeViewLayoutContext.cs

#region PHU_ArrangeView - Chọn và sắp section thường trong vùng giấy


namespace TTSK_AutoDim_Plates
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Linq;
    using Tekla.Structures.Drawing;
    using Tekla.Structures.Geometry3d;
    using System.Collections;
    using Tekla.Structures.Drawing.UI;

    public static class PHU_ArrangeView
    {
        // CHỈNH BỐ CỤC (mm giấy): lề an toàn vào trong khổ giấy; tăng làm vùng xếp section nhỏ lại.
        private const double MIN_EDGE_SAFE = 15.0;

        public class Result
        {
            public bool Success;
            public int MainCount;
            public int SectionCount;
            public string Message;
            public bool GapAdjusted;
            public double RequestedGap, EffectiveGap, RightGap;
            public string GapNotice;
            public bool SelectionMode;
            public int ProtectedSelectionCount;

            public string ToDisplayText()
            {
                if (!string.IsNullOrEmpty(Message))
                    return Message;

                return Success ? "Section: " + SectionCount : "Arrange section view lỗi.";
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

        public static Result Run(
            bool mainHorizontal,
            bool sectionHorizontal,
            double gap,
            bool verticalBottomUp
        )
        {
            return Run(mainHorizontal, sectionHorizontal, gap, verticalBottomUp, false);
        }

        public static Result Run(bool mainHorizontal, bool sectionHorizontal, double gap,
            bool verticalBottomUp, bool alignedGrid)
        {
            Result result = new Result();

            try
            {
                DrawingHandler dh = new DrawingHandler();
                if (!dh.GetConnectionStatus()) throw new InvalidOperationException("Không kết nối Drawing API.");
                Drawing drawing = dh.GetActiveDrawing();
                if (drawing == null)
                {
                    result.Success = false;
                    result.Message = "Không có active drawing.";
                    return result;
                }

                if (!PHU_SectionLayoutPlanner.Finite(gap)) throw new InvalidOperationException("GAP không hợp lệ.");
                if (drawing.IsLocked) throw new InvalidOperationException("Bản vẽ đang khóa.");
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
                List<View> anchorViews;
                Tekla.Structures.Drawing.Rectangle targetRectangle;
                ResolveTargets(dh, allViews, out mainViews, out regularSectionViews,
                    out anchorViews, out result.SelectionMode, out result.ProtectedSelectionCount, out targetRectangle);

                // CHỈ SECTION THƯỜNG: giữ vị trí Main/Top/Bottom/Front và section đặc biệt.
                var plan=ArrangeRegularSectionViews(
                    drawing,
                    mainViews,
                    regularSectionViews,
                    sectionHorizontal,
                    gap,
                    verticalBottomUp,
                    anchorViews,
                    alignedGrid,
                    targetRectangle
                );

                SelectViews(dh, regularSectionViews);
                if(targetRectangle!=null)dh.GetDrawingObjectSelector().SelectObjects(new ArrayList { targetRectangle }, true);

                result.Success = true;
                result.MainCount = mainViews.Count;
                result.SectionCount = regularSectionViews.Count;
                result.RequestedGap=gap;
                result.EffectiveGap=plan.GlobalGap;
                result.RightGap=plan.RightGap;
                result.GapAdjusted=plan.GapAdjusted;
                result.GapNotice=plan.GapNotice;
                result.Message =
                    result.SectionCount > 0
                        ? "Đã arrange " + result.SectionCount + (result.SelectionMode ? " section đã chọn." : " section view (tự động).")
                        : (result.SelectionMode ? "Selection không có section được phép di chuyển. Hãy chọn khung view mặt cắt." : "Không tìm thấy section thường để arrange.");
                if(result.ProtectedSelectionCount>0)result.Message += " Giữ nguyên " + result.ProtectedSelectionCount + " view chính/Top/Bottom được chọn.";
                if(result.GapAdjusted)result.Message += " " + result.GapNotice;
                if(targetRectangle!=null)result.Message += " Trong vùng Rectangle đã chọn.";
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
            catch (Exception ex) { throw new InvalidOperationException("Không đọc đủ danh sách view.",ex); }

            return views;
        }

        private static void ResolveTargets(DrawingHandler handler, List<View> all,
            out List<View> fixedViews, out List<View> movable, out List<View> anchors,
            out bool selectionMode, out int protectedCount, out Tekla.Structures.Drawing.Rectangle targetRectangle)
        {
            List<View> automaticMain, automaticSections;
            ClassifyViews(all, out automaticMain, out automaticSections);
            var selected = new HashSet<int>();
            selectionMode = false;
            targetRectangle = null;
            var objects = handler.GetDrawingObjectSelector().GetSelected();
            while(objects.MoveNext())
            {
                selectionMode = true;
                var view = objects.Current as View;
                if(view != null)selected.Add(ViewId(view));
                var rectangle = objects.Current as Tekla.Structures.Drawing.Rectangle;
                if(rectangle!=null) {
                    if(targetRectangle!=null && !targetRectangle.IsSameDatabaseObject(rectangle))
                        throw new InvalidOperationException("Chỉ chọn một Rectangle làm vùng sắp xếp.");
                    targetRectangle=rectangle;
                }
            }
            if(targetRectangle!=null)selectionMode=selected.Count>0;
            var front = FindFrontView(all);
            if(front == null)front = FindWidestView(automaticMain);
            var protectedIds = new HashSet<int>(all.Where(v => IsProtectedMainView(v, front)).Select(ViewId));
            var movableIds = ResolveMovableIds(all.Select(ViewId), automaticSections.Select(ViewId),
                selected, protectedIds, selectionMode);
            movable = all.Where(v => movableIds.Contains(ViewId(v))).ToList();
            fixedViews = all.Where(v => !movableIds.Contains(ViewId(v))).ToList();
            // Unselected sections are obstacles, but must not expand the main
            // cluster that determines the preferred lower row / right column.
            anchors = automaticMain.Where(v => !movableIds.Contains(ViewId(v))).ToList();
            protectedCount = selected.Count(id => protectedIds.Contains(id));
            movable.Sort(CompareSectionViewsByNameThenPosition);
            var rectangleOwner=targetRectangle==null?null:targetRectangle.GetView();
            if(rectangleOwner!=null && movable.Any(v=>v.IsSameDatabaseObject(rectangleOwner)))
                throw new InvalidOperationException("Rectangle nằm trong view đang được sắp xếp. Hãy vẽ Rectangle trên sheet hoặc view cố định.");
        }

        internal static PHU_SectionLayoutPlanner.Rect RestrictToRectangle(PHU_SectionLayoutPlanner.Rect paper,
            Point start,Point end,double angle)
        {
            if(start==null||end==null||!PHU_SectionLayoutPlanner.Finite(angle)||Math.Abs(angle/90-Math.Round(angle/90))>0.000001)
                throw new InvalidOperationException("Vùng sắp xếp cần là Rectangle có cạnh ngang/dọc.");
            var region=new PHU_SectionLayoutPlanner.Rect(Math.Min(start.X,end.X)+2,
                Math.Min(start.Y,end.Y)+2,Math.Max(start.X,end.X)-2,Math.Max(start.Y,end.Y)-2);
            if(!region.Valid)throw new InvalidOperationException("Rectangle quá nhỏ hoặc tọa độ không hợp lệ.");
            return region;
        }

        internal static HashSet<int> ResolveMovableIds(IEnumerable<int> all, IEnumerable<int> automatic,
            IEnumerable<int> selected, IEnumerable<int> protectedIds, bool selectionMode)
        {
            var result = new HashSet<int>(selectionMode ? selected : automatic);
            result.IntersectWith(all);
            result.ExceptWith(protectedIds);
            return result;
        }

        private static bool IsProtectedMainView(View view, View front)
        {
            if(IsKnownMainView(view) || (front != null && view.IsSameDatabaseObject(front)))return true;
            if(front == null)return false;
            return IsLongitudinalMainSection(view.DisplayCoordinateSystem, view.RestrictionBox,
                front.DisplayCoordinateSystem, front.RestrictionBox);
        }

        // Model-coordinate orientation + model-space coverage, independent of
        // section letters, sheet position, view scale and annotation extents.
        internal static bool IsLongitudinalMainSection(CoordinateSystem section, AABB sectionBox,
            CoordinateSystem front, AABB frontBox)
        {
            if(section == null || front == null || sectionBox == null || frontBox == null)return false;
            Vector sx=Unit(section.AxisX), sy=Unit(section.AxisY), fx=Unit(front.AxisX), fy=Unit(front.AxisY);
            if(sx==null||sy==null||fx==null||fy==null)return false;
            Vector sn=Unit(Vector.Cross(sx,sy)), fn=Unit(Vector.Cross(fx,fy));
            if(sn==null||fn==null)return false;
            double width=Math.Abs(frontBox.MaxPoint.X-frontBox.MinPoint.X);
            double height=Math.Abs(frontBox.MaxPoint.Y-frontBox.MinPoint.Y);
            double length=Math.Max(width,height);
            if(!PHU_SectionLayoutPlanner.Finite(length)||length<=1)return false;
            Vector longitudinal=width>=height?fx:fy;
            // Top/Bottom-type planes contain the main longitudinal axis and
            // are perpendicular to the front plane. Transverse sections fail.
            if(Math.Abs(Vector.Dot(sn,longitudinal))>.1 || Math.Abs(Vector.Dot(sn,fn))>.1)return false;
            double span=Math.Abs(Vector.Dot(sx,longitudinal))*Math.Abs(sectionBox.MaxPoint.X-sectionBox.MinPoint.X)
                +Math.Abs(Vector.Dot(sy,longitudinal))*Math.Abs(sectionBox.MaxPoint.Y-sectionBox.MinPoint.Y);
            return PHU_SectionLayoutPlanner.Finite(span) && span>=length*.55;
        }

        private static Vector Unit(Vector vector)
        {
            if(vector==null)return null;
            double length=vector.GetLength();
            if(!PHU_SectionLayoutPlanner.Finite(length)||length<1e-9)return null;
            return new Vector(vector.X/length,vector.Y/length,vector.Z/length);
        }

        private static void ClassifyViews(
            List<View> allViews,
            out List<View> mainViews,
            out List<View> regularSectionViews
        )
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
                if (IsProtectedMainView(section, frontView))
                    AddUniqueView(mainViews, section);
                else if (IsNamedSectionView(section))
                    AddUniqueView(regularSectionViews, section);
                else if (IsSpecialTopBottomSection(section, frontView, frontWidth))
                    AddUniqueView(mainViews, section);
                else
                    AddUniqueView(regularSectionViews, section);
            }

            // Fallback: một số view section trong Tekla không trả ViewType/Name có chữ Section.
            // Khi đó chọn các view nhỏ/hẹp hơn Front làm section thường, nhưng vẫn không đụng main view lớn.
            if (regularSectionViews.Count == 0)
                AddRegularSectionsByGeometry(
                    allViews,
                    mainViews,
                    regularSectionViews,
                    frontView,
                    frontWidth
                );

            // Geometry fallback may have promoted a non-section API view. Every view
            // must belong to exactly one group or it would obstruct its own placement.
            var movable = regularSectionViews;
            mainViews.RemoveAll(v => movable.Any(s => s.IsSameDatabaseObject(v)));

            mainViews.Sort(
                delegate(View a, View b)
                {
                    ViewBox ba;
                    ViewBox bb;
                    if (!TryGetViewBox(a, out ba) || !TryGetViewBox(b, out bb))
                        return 0;

                    int cy = bb.CenterY.CompareTo(ba.CenterY);
                    if (cy != 0)
                        return cy;
                    return ba.CenterX.CompareTo(bb.CenterX);
                }
            );

            regularSectionViews.Sort(CompareSectionViewsByNameThenPosition);
        }

        private static void AddRegularSectionsByGeometry(
            List<View> allViews,
            List<View> mainViews,
            List<View> regularSectionViews,
            View frontView,
            double frontWidth
        )
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

                    if (IsProtectedMainView(v, frontView))
                        continue;

                    ViewBox b;
                    if (!TryGetViewBox(v, out b))
                        continue;

                    // Section thường hay là mặt cắt nhỏ ở hai bên: hẹp hơn rõ so với front.
                    // Không dùng rule này để move Top/Bottom vì Top/Bottom thường dài gần Front.
                    bool narrowSection = frontWidth > 1.0 && b.Width <= frontWidth * 0.45;
                    bool tallSmallSection =
                        b.Height > b.Width * 1.15 && b.Width <= frontWidth * 0.60;

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
            catch { }
        }

        private static bool IsKnownMainView(View view)
        {
            string type = GetViewTypeText(view);
            string name = SafeViewName(view);

            if (ContainsIgnoreCase(type, "Front") || ContainsIgnoreCase(name, "Front"))
                return true;
            if (ContainsIgnoreCase(type, "Top") || ContainsIgnoreCase(name, "Top"))
                return true;
            if (ContainsIgnoreCase(type, "Bottom") || ContainsIgnoreCase(name, "Bottom"))
                return true;

            return false;
        }

        private static bool IsSpecialTopBottomSection(
            View section,
            View frontView,
            double frontWidth
        )
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
            catch { }

            return false;
        }

        private sealed class LayoutSnapshot
        {
            public List<ViewBox> Sections;
            public List<ViewBox> Fixed;
            public PHU_SectionLayoutPlanner.Rect Paper;
            public List<PHU_SectionLayoutPlanner.Rect> Obstacles;
            public PHU_SectionLayoutPlanner.Rect Main;
            public List<PHU_SectionLayoutPlanner.Item> Items;
        }
        private static PHU_SectionLayoutPlanner.Rect Rectangle(ViewBox b)
        { return new PHU_SectionLayoutPlanner.Rect(b.MinX,b.MinY,b.MaxX,b.MaxY); }

        private static LayoutSnapshot CaptureLayout(Drawing drawing,List<View> mainViews,List<View> sectionViews,List<View> anchorViews=null,
            Tekla.Structures.Drawing.Rectangle targetRectangle=null)
        {
            var sheet=drawing.GetSheet();double width=sheet.Width,height=sheet.Height;
            if(!PHU_SectionLayoutPlanner.Finite(width)||!PHU_SectionLayoutPlanner.Finite(height)||width<=30||height<=30)
                throw new InvalidOperationException("Không đọc được khổ giấy thực tế.");
            var sections=BuildBoxes(sectionViews);var fixedViews=BuildBoxes(mainViews);
            if(sections.Count!=sectionViews.Count||fixedViews.Count!=mainViews.Count)
                throw new InvalidOperationException("Không đọc được đầy đủ khung view. Chưa di chuyển view nào.");
            sections.Sort(CompareSectionBoxesByNameThenPosition);
            var all=mainViews.Concat(sectionViews).ToList();
            var ids=all.Select(ViewId).ToArray();
            var paper=new PHU_SectionLayoutPlanner.Rect(MIN_EDGE_SAFE,MIN_EDGE_SAFE,width-MIN_EDGE_SAFE,height-MIN_EDGE_SAFE);
            int? ignoredRectangle=null;
            if(targetRectangle!=null) {
                if(!targetRectangle.Select())throw new InvalidOperationException("Không đọc lại được Rectangle đã chọn.");
                var owner=targetRectangle.GetView();
                var start=Tekla.Structures.Drawing.Tools.DrawingCoordinateConverter.Convert(owner,sheet,targetRectangle.StartPoint);
                var end=Tekla.Structures.Drawing.Tools.DrawingCoordinateConverter.Convert(owner,sheet,targetRectangle.EndPoint);
                paper=RestrictToRectangle(paper,start,end,targetRectangle.Angle);
                ignoredRectangle=ObjectId(targetRectangle);
            }
            var rendered=PHU_ArrangePresentation.Read(ids,width,height,ignoredRectangle);
            for(int i=0;i<all.Count;i++)
            {
                int index=rendered.ViewIds.IndexOf(ids[i]);var origin=all[i].Origin;
                if(index<0||origin==null||Math.Abs(origin.X-rendered.ViewX[index])>.1||Math.Abs(origin.Y-rendered.ViewY[index])>.1)
                    throw new InvalidOperationException("Dữ liệu hiển thị chưa khớp vị trí view. Hãy refresh bản vẽ rồi chạy lại.");
            }
            var obstacles=fixedViews.Select(Rectangle).ToList();
            var anchors=anchorViews==null?fixedViews:fixedViews.Where(b=>anchorViews.Any(v=>v.IsSameDatabaseObject(b.View))).ToList();
            // The fixed clearance is only a small drawing stroke/rounding allowance;
            // obstacle widths and heights always come from current presentation data.
            obstacles.AddRange(rendered.Obstacles.Select(r=>r.Inflate(2)));
            return new LayoutSnapshot {
                Sections=sections,Fixed=fixedViews,Paper=paper,
                Obstacles=obstacles,Main=targetRectangle!=null||anchors.Count==0?null:Rectangle(GetClusterBoxFromBoxes(anchors)),
                Items=sections.Select(b=>new PHU_SectionLayoutPlanner.Item{Key=b.SortKey,Width=b.Width,Height=b.Height,AnchorX=b.View.Origin.X-b.MinX}).ToList()
            };
        }
        private static int ViewId(View view)
        { return ObjectId(view); }
        private static int ObjectId(DatabaseObject view)
        {
            var property=view.GetType().GetProperty("Identifier",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            var id=property==null?null:property.GetValue(view,null) as Tekla.Structures.Identifier;
            if(id==null||id.ID<=0)throw new InvalidOperationException("Không đọc được định danh view.");return id.ID;
        }
        private static PHU_SectionLayoutPlanner.Plan ArrangeRegularSectionViews(Drawing drawing,List<View> mainViews,List<View> sectionViews,
            bool horizontal,double gap,bool verticalBottomUp,List<View> anchorViews=null,bool alignedGrid=false,
            Tekla.Structures.Drawing.Rectangle targetRectangle=null)
        {
            if(sectionViews.Count==0)return new PHU_SectionLayoutPlanner.Plan{RequestedGap=gap,GlobalGap=gap,RightGap=gap};
            var snapshot=CaptureLayout(drawing,mainViews,sectionViews,anchorViews,targetRectangle);
            var plan=PHU_SectionLayoutPlanner.Build(snapshot.Paper,snapshot.Items,snapshot.Obstacles,snapshot.Main,gap,horizontal,verticalBottomUp,
                alignedGrid?PHU_SectionLayoutPlanner.VerticalLayout.AlignedGrid:PHU_SectionLayoutPlanner.VerticalLayout.LegacyColumns);
            if(!plan.Success)throw new InvalidOperationException(plan.Error);
            var originals=snapshot.Sections.Select(b=>new Point(b.View.Origin)).ToList();
            var fixedOrigins=snapshot.Fixed.Select(b=>new Point(b.View.Origin)).ToList();
            var handler=new DrawingHandler();
            var active=handler.GetActiveDrawing();
            if(active==null||!active.IsSameDatabaseObject(drawing))throw new InvalidOperationException("Bản vẽ đang mở đã thay đổi.");
            int touched=0;
            try
            {
                for(int i=0;i<snapshot.Sections.Count;i++)
                {
                    var b=snapshot.Sections[i];var target=plan.Places[i];
                    touched=i+1;
                    b.View.Origin=new Point(originals[i].X+target.Left-b.MinX,originals[i].Y+target.Bottom-b.MinY,originals[i].Z);
                    if(!b.View.Modify())throw new InvalidOperationException("Tekla từ chối di chuyển mặt cắt " + b.SortKey);
                }
                VerifyApplied(snapshot,plan,gap,fixedOrigins);
                drawing.CommitChanges();
                VerifyApplied(snapshot,plan,gap,fixedOrigins);
                var refreshed=CaptureLayout(drawing,mainViews,sectionViews,anchorViews,targetRectangle);
                if(!PHU_SectionLayoutPlanner.ValidatePlan(refreshed.Paper,refreshed.Items,refreshed.Obstacles,
                    refreshed.Main,plan,refreshed.Sections.Select(Rectangle).ToList()))
                    throw new InvalidOperationException("Layout bảng/keyplan sau cập nhật không còn đủ khoảng trống.");
                return plan;
            }
            catch(Exception ex)
            {
                bool restored=true;
                // Restore moved sections and any linked main view Tekla relocated implicitly.
                for(int i=0;i<touched;i++)try { snapshot.Sections[i].View.Origin=originals[i];restored &= snapshot.Sections[i].View.Modify(); }catch{restored=false;}
                for(int i=0;i<snapshot.Fixed.Count;i++)try {
                    var v=snapshot.Fixed[i].View;v.Select();
                    if(Distance.PointToPoint(v.Origin,fixedOrigins[i])>.01){v.Origin=fixedOrigins[i];restored &= v.Modify();}
                }catch{restored=false;}
                try { drawing.CommitChanges(); }catch{restored=false;}
                for(int i=0;i<touched;i++)try {var v=snapshot.Sections[i].View;v.Select();restored &= Distance.PointToPoint(v.Origin,originals[i])<.1;}catch{restored=false;}
                for(int i=0;i<snapshot.Fixed.Count;i++)try {var v=snapshot.Fixed[i].View;restored &= v.Select()&&Distance.PointToPoint(v.Origin,fixedOrigins[i])<.1;}catch{restored=false;}
                throw new InvalidOperationException(ex.Message+(restored?" Đã khôi phục vị trí ban đầu.":" Không khôi phục được toàn bộ vị trí; cần kiểm tra bản vẽ."),ex);
            }
        }
        private static void VerifyApplied(LayoutSnapshot snapshot,PHU_SectionLayoutPlanner.Plan plan,double gap,List<Point> fixedOrigins)
        {
            var actual=new List<PHU_SectionLayoutPlanner.Rect>();
            for(int i=0;i<snapshot.Sections.Count;i++)
            {
                var view=snapshot.Sections[i].View;
                if(!view.Select())throw new InvalidOperationException("Không đọc lại được mặt cắt.");
                ViewBox b;if(!TryGetViewBox(view,out b))throw new InvalidOperationException("Không đọc lại được khung mặt cắt.");
                var target=plan.Places[i];
                if(Math.Abs(b.MinX-target.Left)>.1||Math.Abs(b.MinY-target.Bottom)>.1)
                    throw new InvalidOperationException("Tekla đã thay đổi vị trí ngoài phương án Arrange.");
                actual.Add(Rectangle(b));
            }
            for(int i=0;i<snapshot.Fixed.Count;i++)
            {var v=snapshot.Fixed[i].View;if(!v.Select()||Distance.PointToPoint(v.Origin,fixedOrigins[i])>.1)throw new InvalidOperationException("Tekla đã dịch view chính liên kết.");}
            if(!PHU_SectionLayoutPlanner.ValidatePlan(snapshot.Paper,snapshot.Items,snapshot.Obstacles,snapshot.Main,plan,actual))
                throw new InvalidOperationException("Khung view sau di chuyển không đạt kiểm tra mép giấy/vùng né/khoảng cách.");
        }

        // Explicitly read-only entry point for diagnostics and regression on the open drawing.
        public static string AuditCurrentLayout(bool horizontal,double gap,bool bottomUp)
        {
            return AuditCurrentLayoutWithMode(horizontal,gap,bottomUp,false);
        }

        public static string AuditCurrentLayoutWithMode(bool horizontal,double gap,bool bottomUp,bool alignedGrid)
        {
            var handler=new DrawingHandler();if(!handler.GetConnectionStatus())throw new InvalidOperationException("No Drawing API connection");
            var drawing=handler.GetActiveDrawing();if(drawing==null)throw new InvalidOperationException("No active drawing");
            List<View> main,sections,anchors;bool selectionMode;int protectedCount;
            Tekla.Structures.Drawing.Rectangle targetRectangle;
            ResolveTargets(handler,GetAllViews(drawing),out main,out sections,out anchors,out selectionMode,out protectedCount,out targetRectangle);
            var s=CaptureLayout(drawing,main,sections,anchors,targetRectangle);var p=PHU_SectionLayoutPlanner.Build(s.Paper,s.Items,s.Obstacles,s.Main,gap,horizontal,bottomUp,
                alignedGrid?PHU_SectionLayoutPlanner.VerticalLayout.AlignedGrid:PHU_SectionLayoutPlanner.VerticalLayout.LegacyColumns);
            var text=new System.Text.StringBuilder("ARRANGE READ ONLY "+drawing.Mark+"\r\nPaper="+s.Paper+" Gap="+gap+"\r\n");
            text.AppendLine("SelectionMode="+selectionMode+" ProtectedSelected="+protectedCount);
            text.AppendLine("Layout="+(alignedGrid?"AlignedGrid":"Legacy")+" Columns="+p.GridColumns);
            foreach(var b in s.Fixed)text.AppendLine("FIXED id="+ViewId(b.View)+" name="+SafeViewName(b.View)+" origin="+b.View.Origin);
            foreach(var obstacle in s.Obstacles)text.AppendLine("OBSTACLE "+obstacle);
            for(int i=0;i<s.Sections.Count;i++)text.AppendLine("VIEW "+s.Sections[i].SortKey+" "+Rectangle(s.Sections[i])+(p.Success?" => "+p.Places[i]:""));
            text.AppendLine(p.Success?"PLAN PASS":p.Error);
            text.AppendLine("GlobalGap="+p.GlobalGap+" RightGap="+p.RightGap+" "+p.GapNotice);return text.ToString();
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
            catch { }
        }

        private static int CompareSectionViewsByNameThenPosition(View a, View b)
        {
            string ka = GetSectionSortKey(a);
            string kb = GetSectionSortKey(b);

            int nameCompare = CompareSectionKeys(ka, kb);
            if (nameCompare != 0)
                return nameCompare;

            return ViewId(a).CompareTo(ViewId(b));
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

            return ViewId(a.View).CompareTo(ViewId(b.View));
        }

        private static int CompareSectionKeys(string a, string b)
        {
            bool hasA = !string.IsNullOrEmpty(a);
            bool hasB = !string.IsNullOrEmpty(b);

            if (hasA && hasB)
            {
                if(System.Text.RegularExpressions.Regex.IsMatch(a,@"^[A-Z]+$",System.Text.RegularExpressions.RegexOptions.IgnoreCase) &&
                    System.Text.RegularExpressions.Regex.IsMatch(b,@"^[A-Z]+$",System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {int length=a.Length.CompareTo(b.Length);if(length!=0)return length;}
                long na,nb;if(Int64.TryParse(a,out na)&&Int64.TryParse(b,out nb))return na.CompareTo(nb);
                return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            }

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

        private static string ExtractSectionLetterKey(string text, bool requirePair)
        {
            if(String.IsNullOrWhiteSpace(text))return "";
            string upper=text.Trim().ToUpperInvariant();
            var pair=System.Text.RegularExpressions.Regex.Match(upper,@"^([A-Z]+|[0-9]+)\s*[-－–_=]\s*\1$");
            if(pair.Success)return pair.Groups[1].Value;
            if(requirePair)return "";
            return System.Text.RegularExpressions.Regex.IsMatch(upper,@"^([A-Z]+|[0-9]+)$")?upper:"";
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

                if (b.MinX < c.MinX)
                    c.MinX = b.MinX;
                if (b.MaxX > c.MaxX)
                    c.MaxX = b.MaxX;
                if (b.MinY < c.MinY)
                    c.MinY = b.MinY;
                if (b.MaxY > c.MaxY)
                    c.MaxY = b.MaxY;
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

        private static bool TryGetViewBox(View view, out ViewBox box)
        {
            box = null;

            try
            {
                if (view == null)
                    return false;

                AABB bb = null;
                try
                {
                    bb = view.GetAxisAlignedBoundingBox();
                }
                catch
                {
                    bb = null;
                }

                if (bb == null || bb.MinPoint == null || bb.MaxPoint == null)
                    return false;

                double minX = Math.Min(bb.MinPoint.X, bb.MaxPoint.X);
                double maxX = Math.Max(bb.MinPoint.X, bb.MaxPoint.X);
                double minY = Math.Min(bb.MinPoint.Y, bb.MaxPoint.Y);
                double maxY = Math.Max(bb.MinPoint.Y, bb.MaxPoint.Y);

                if(!PHU_SectionLayoutPlanner.Finite(minX)||!PHU_SectionLayoutPlanner.Finite(maxX)||
                    !PHU_SectionLayoutPlanner.Finite(minY)||!PHU_SectionLayoutPlanner.Finite(maxY))return false;
                if (maxX <= minX + 0.5 || maxY <= minY + 0.5)
                    return false;

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

                if(IsKnownMainView(view))return false;
                if(ContainsIgnoreCase(GetViewTypeText(view),"Section") &&
                    System.Text.RegularExpressions.Regex.IsMatch(name,@"^([A-Z]+|[0-9]+)$"))return true;

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
                    if (!string.IsNullOrEmpty(text))
                        return text;

                    text = ReadAnyPropertyAsString(attr, "Type");
                    if (!string.IsNullOrEmpty(text))
                        return text;
                }

                string direct = ReadAnyPropertyAsString(view, "ViewType");
                if (!string.IsNullOrEmpty(direct))
                    return direct;
            }
            catch { }

            return "";
        }

        private static string SafeViewName(View view)
        {
            string name = ReadAnyPropertyAsString(view, "Name");
            if (!string.IsNullOrEmpty(name))
                return name;

            name = ReadAnyPropertyAsString(view, "ViewName");
            if (!string.IsNullOrEmpty(name))
                return name;

            return "";
        }

        private static string ReadAnyPropertyAsString(object obj, string propertyName)
        {
            try
            {
                if (obj == null)
                    return "";

                PropertyInfo prop = obj.GetType()
                    .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);

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

    }
}
#endregion

#region PHU_SectionLayoutPlanner - Lập kế hoạch vị trí các section


namespace TTSK_AutoDim_Plates
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    // All dimensions are millimetres on paper. This planner makes no Tekla calls.
    internal static class PHU_SectionLayoutPlanner
    {
        internal enum VerticalLayout { LegacyColumns, AdaptiveRows, AlignedGrid }
        // DUNG SAI SỐ (mm giấy): so sánh hình chữ nhật trong planner; không phải khoảng hở bố trí.
        internal const double Epsilon = 0.001;
        [Serializable]
        public sealed class Rect
        {
            public double Left, Bottom, Right, Top;
            public Rect(double left, double bottom, double right, double top)
            { Left = left; Bottom = bottom; Right = right; Top = top; }
            public double Width { get { return Right - Left; } }
            public double Height { get { return Top - Bottom; } }
            public double CenterX { get { return (Left + Right) / 2; } }
            public Rect Inflate(double d) { return new Rect(Left-d, Bottom-d, Right+d, Top+d); }
            public bool Valid { get { return Finite(Left) && Finite(Bottom) && Finite(Right) && Finite(Top) && Width > 0 && Height > 0; } }
            public bool Contains(Rect b) { return b.Left >= Left-Epsilon && b.Right <= Right+Epsilon && b.Bottom >= Bottom-Epsilon && b.Top <= Top+Epsilon; }
            public bool Overlaps(Rect b) { return Left < b.Right-Epsilon && Right > b.Left+Epsilon && Bottom < b.Top-Epsilon && Top > b.Bottom+Epsilon; }
            public override string ToString() { return String.Format(System.Globalization.CultureInfo.InvariantCulture,"[{0:F3},{1:F3}..{2:F3},{3:F3}]",Left,Bottom,Right,Top); }
        }
        internal sealed class Item
        {
            public string Key;
            public double Width, Height;
            public double? AnchorX;
        }
        internal sealed class Plan
        {
            public readonly List<Rect> Places = new List<Rect>();
            public readonly List<bool> RightGroup = new List<bool>();
            public readonly List<Band> Bands = new List<Band>();
            public bool Horizontal;
            public bool RowsBottomUp;
            public int GridColumns;
            public double RequestedGap, GlobalGap, RightGap;
            public bool TriedZeroGap;
            public string Error;
            public bool Success { get { return Error == null; } }
            public bool GapAdjusted { get { return GlobalGap < RequestedGap-Epsilon || RightGap < RequestedGap-Epsilon; } }
            public string GapNotice
            {
                get
                {
                    if(!GapAdjusted)return "";
                    string target=GlobalGap < RequestedGap-Epsilon ? "toàn bộ" : "cụm phải";
                    double used=GlobalGap < RequestedGap-Epsilon ? GlobalGap : RightGap;
                    return "Đã giảm GAP " + target + " " + RequestedGap.ToString("0.##") + " → " + used.ToString("0.##") + " mm để xếp vừa.";
                }
            }
        }
        internal sealed class Band
        {
            public int Start, Count;
            public bool Column, BottomUp;
        }
        internal static bool Finite(double n) { return !Double.IsNaN(n) && !Double.IsInfinity(n); }

        internal static Plan Build(Rect sheet, IList<Item> items, IList<Rect> obstacles,
            Rect main, double gap, bool horizontal, bool bottomUp, VerticalLayout verticalLayout=VerticalLayout.LegacyColumns)
        {
            if (sheet == null || !sheet.Valid || !Finite(gap) || gap < 0 ||
                items.Any(i => !Finite(i.Width) || !Finite(i.Height) || i.Width <= 0 || i.Height <= 0) ||
                obstacles.Any(o => o == null || !o.Valid))
                return new Plan { Error = "Kích thước hoặc khoảng cách Arrange không hợp lệ." };
            if (items.Count == 0) return new Plan();
            // Preserve the preferred layout first. When its right cluster is full,
            // compress that cluster before changing the rest of the drawing.
            var plan=BuildFixed(sheet,items,obstacles,main,gap,gap,horizontal,bottomUp,true,verticalLayout);
            if(plan.Success)return plan;
            if((horizontal || verticalLayout==VerticalLayout.LegacyColumns) && main!=null && gap>0)
            {
                plan=FindLargestFittingGap(gap,g=>BuildFixed(sheet,items,obstacles,main,gap,g,horizontal,bottomUp,true,verticalLayout));
                if(plan.Success)return plan;
            }
            // Reduce the preferred, readable layout before trying another topology.
            // A geometrically valid scatter into leftover pockets is not a layout.
            if(gap>0)
            {
                plan=FindLargestFittingGap(gap,g=>BuildFixed(sheet,items,obstacles,main,g,g,horizontal,bottomUp,true,verticalLayout));
                plan.RequestedGap=gap;
                if(plan.Success)return plan;
            }
            plan=BuildFixed(sheet,items,obstacles,main,gap,gap,horizontal,bottomUp,false,verticalLayout);
            if(plan.Success)return plan;
            if(gap>0)
            {
                plan=FindLargestFittingGap(gap,g=>BuildFixed(sheet,items,obstacles,main,g,g,horizontal,bottomUp,false,verticalLayout));
                plan.RequestedGap=gap;
                if(plan.Success)return plan;
            }
            return new Plan {RequestedGap=gap,GlobalGap=0,RightGap=0,TriedZeroGap=true,Error=
                "Đã thử giảm GAP toàn bộ đến 0 mm nhưng chưa tìm được bố cục đủ chỗ cho " + items.Count +
                " mặt cắt. Hãy giảm tỷ lệ hiển thị hoặc tăng khổ giấy; chưa di chuyển view nào."};
        }

        // Descending probes include zero explicitly. Refinement keeps the best feasible
        // plan; it does not assume every greedy packing decision is monotonic in GAP.
        static Plan FindLargestFittingGap(double requested,Func<double,Plan> build)
        {
            double high=requested;
            Plan last=null;
            for(int step=1;step<=12;step++)
            {
                double low=step==12?0:requested*(12-step)/12;
                last=build(low);
                if(last.Success)
                {
                    var best=last;
                    for(int refine=0;refine<12 && high-low>.01;refine++)
                    {
                        double middle=(low+high)/2;var candidate=build(middle);
                        if(candidate.Success){best=candidate;low=middle;}else high=middle;
                    }
                    return best;
                }
                high=low;
            }
            last.TriedZeroGap=true;return last;
        }

        static Plan BuildFixed(Rect sheet,IList<Item> items,IList<Rect> obstacles,Rect main,
            double gap,double rightGap,bool horizontal,bool bottomUp,bool preferredOnly,VerticalLayout verticalLayout)
        {
            if(!horizontal && verticalLayout==VerticalLayout.AlignedGrid)
                return BuildAlignedGrid(sheet,items,obstacles,main,gap,bottomUp);
            if(!horizontal && verticalLayout!=VerticalLayout.LegacyColumns)
                return BuildVerticalRows(sheet,items,obstacles,main,gap,bottomUp,preferredOnly);
            foreach(int mode in preferredOnly ? new[]{0} : new[]{0,1,2,3,4})
            {
                if(horizontal && mode==3)continue; // Horizontal order starts with a row.
                var plan = new Plan {RequestedGap=gap,GlobalGap=gap,RightGap=rightGap,Horizontal=horizontal};
                var blocked = Blocked(sheet,main,obstacles,plan,gap,gap);
                int next = 0;
                if (horizontal && mode == 0)
                {
                    PlaceRow(sheet, items, ref next, blocked, plan, main, gap, true);
                    var rightBlocked=Blocked(sheet,main,obstacles,plan,rightGap,gap);
                    PlaceColumns(sheet, items, ref next, rightBlocked, plan, main, rightGap, bottomUp, true);
                    blocked=Blocked(sheet,main,obstacles,plan,gap,gap);
                }
                else if ((!horizontal && mode < 2) || mode == 3)
                {
                    var rightBlocked=Blocked(sheet,main,obstacles,plan,rightGap,gap);
                    PlaceColumns(sheet, items, ref next, rightBlocked, plan, main, rightGap, bottomUp, true);
                    blocked=Blocked(sheet,main,obstacles,plan,gap,gap);
                }

                while (next < items.Count)
                {
                    int before = next;
                    if (mode == 2 && !horizontal)
                        PlaceColumns(sheet, items, ref next, blocked, plan, main, gap, bottomUp, false);
                    else
                        PlaceRow(sheet, items, ref next, blocked, plan, main, gap, false, mode == 1, mode == 4, mode == 2);
                    if (next == before) break;
                }
                if (next == items.Count && ValidatePlan(sheet,items,obstacles,main,plan)) return plan;
            }
            return new Plan {Error="Không đủ chỗ tại GAP đang thử.",RequestedGap=gap,GlobalGap=gap,RightGap=rightGap};
        }

        static Rect FlipY(Rect rect)
        { return rect==null?null:new Rect(rect.Left,-rect.Top,rect.Right,-rect.Bottom); }

        static double Anchor(Item item) { return item.AnchorX??item.Width/2; }

        // Columns share a view-origin X axis, with one pitch for the entire grid.
        // Rows share a lower-frame baseline. Measure envelopes around these
        // anchors, rather than centering each row independently.
        static Plan BuildAlignedGrid(Rect sheet,IList<Item> items,IList<Rect> obstacles,Rect main,double gap,bool bottomUp)
        {
            var blocked=Blocked(sheet,main,obstacles,new Plan(),gap,gap);
            foreach(bool rightOnly in new[]{true,false})
            {
                double left=rightOnly&&main!=null?Math.Max(sheet.Left,main.Right+gap):sheet.Left;
                if(left>=sheet.Right-Epsilon)continue;
                var region=new Rect(left,sheet.Bottom,sheet.Right,sheet.Top);
                for(int columns=items.Count;columns>=1;columns--)
                {
                    // Necessary first-row width bound avoids building oversized grids.
                    if(items.Take(columns).Sum(i=>i.Width)+gap*(columns-1)>region.Width+Epsilon)continue;
                    int rows=(items.Count+columns-1)/columns;
                    var l=new double[columns];var r=new double[columns];var heights=new double[rows];
                    for(int i=0;i<items.Count;i++)
                    {
                        double a=Anchor(items[i]);
                        if(!Finite(a))return new Plan{Error="Mốc căn cột không hợp lệ."};
                        l[i%columns]=Math.Max(l[i%columns],a);
                        r[i%columns]=Math.Max(r[i%columns],items[i].Width-a);
                        heights[i/columns]=Math.Max(heights[i/columns],items[i].Height);
                    }
                    double pitch=0;
                    for(int c=1;c<columns;c++)pitch=Math.Max(pitch,r[c-1]+l[c]+gap);
                    double width=l[0]+pitch*(columns-1)+r[columns-1];
                    double height=heights.Sum()+gap*(rows-1);
                    if(width>region.Width+Epsilon||height>region.Height+Epsilon)continue;
                    var relative=new List<Rect>();double rowY=bottomUp?0:height;
                    for(int row=0;row<rows;row++)
                    {
                        double baseline=bottomUp?rowY:rowY-heights[row];
                        for(int col=0;col<columns&&row*columns+col<items.Count;col++)
                        {
                            var item=items[row*columns+col];double x=l[0]+col*pitch-Anchor(item);
                            relative.Add(new Rect(x,baseline,x+item.Width,baseline+item.Height));
                        }
                        rowY+=bottomUp?heights[row]+gap:-heights[row]-gap;
                    }
                    // Critical translation levels come from real obstacle edges.
                    // Empty cells in the final row do not consume drawing space.
                    var ys=new List<double>{region.Bottom,region.Top-height};
                    foreach(var box in relative)foreach(var obstacle in blocked)
                    {ys.Add(obstacle.Top-box.Bottom);ys.Add(obstacle.Bottom-box.Top);}
                    var levels=ys.Where(y=>y>=region.Bottom-Epsilon&&y+height<=region.Top+Epsilon).Distinct();
                    foreach(double y in bottomUp?levels.OrderBy(v=>v):levels.OrderByDescending(v=>v))
                    {
                        var forbidden=new List<Tuple<double,double>>();
                        foreach(var box in relative)foreach(var obstacle in blocked)
                            if(box.Bottom+y<obstacle.Top-Epsilon&&box.Top+y>obstacle.Bottom+Epsilon)
                                forbidden.Add(Tuple.Create(obstacle.Left-box.Right,obstacle.Right-box.Left));
                        double x;
                        if(!FindGridTranslation(region.Left,region.Right-width,forbidden,out x))continue;
                        var plan=new Plan{RequestedGap=gap,GlobalGap=gap,RightGap=gap,GridColumns=columns,RowsBottomUp=bottomUp};
                        plan.Places.AddRange(relative.Select(b=>new Rect(b.Left+x,b.Bottom+y,b.Right+x,b.Top+y)));
                        plan.RightGroup.AddRange(items.Select(i=>false));
                        for(int row=0;row<rows;row++)plan.Bands.Add(new Band{Start=row*columns,Count=Math.Min(columns,items.Count-row*columns)});
                        if(ValidatePlan(sheet,items,obstacles,main,plan))return plan;
                    }
                }
            }
            return new Plan{Error="Chưa tìm được lưới căn hàng/cột tại GAP đang thử.",RequestedGap=gap,GlobalGap=gap,RightGap=gap};
        }

        static bool FindGridTranslation(double min,double max,List<Tuple<double,double>> forbidden,out double x)
        {
            x=0;double cursor=min,best=Double.MaxValue,target=(min+max)/2;
            foreach(var range in forbidden.OrderBy(v=>v.Item1))
            {
                if(range.Item2<cursor-Epsilon)continue;
                double end=Math.Min(max,range.Item1);
                if(end>=cursor-Epsilon)
                {double candidate=Math.Max(cursor,Math.Min(target,end));double score=Math.Abs(candidate-target);if(score<best){best=score;x=candidate;}}
                cursor=Math.Max(cursor,range.Item2);
                if(cursor>max+Epsilon)break;
            }
            if(cursor<=max+Epsilon)
            {double candidate=Math.Max(cursor,Math.Min(target,max));if(Math.Abs(candidate-target)<best){best=Math.Abs(candidate-target);x=candidate;}}
            return best<Double.MaxValue;
        }

        // Vertical mode grows by rows: A-B, then C-D, rather than exhausting
        // one column first. Reflect only the planning coordinates for upward
        // growth so both directions use the same collision/row solver.
        static Plan BuildVerticalRows(Rect sheet,IList<Item> items,IList<Rect> obstacles,
            Rect main,double gap,bool bottomUp,bool preferredOnly)
        {
            var space=bottomUp?FlipY(sheet):sheet;
            var fixedRects=bottomUp?obstacles.Select(FlipY).ToList():obstacles.ToList();
            var anchor=bottomUp?FlipY(main):main;
            // First try the right-hand region. If that cannot hold the complete
            // ordered sequence, use all available row intervals at the same GAP.
            // Additional capacity-first/compact trials are reserved for fallback.
            foreach(int strategy in preferredOnly?new[]{0,1}:new[]{0,1,2,3})
            {
                var plan=new Plan{RequestedGap=gap,GlobalGap=gap,RightGap=gap,Horizontal=false};
                var blocked=Blocked(space,anchor,fixedRects,plan,gap,gap);
                Rect region=space;
                if(strategy==0 && anchor!=null)
                {
                    double left=Math.Max(space.Left,anchor.Right+gap);
                    if(left>=space.Right-Epsilon)continue;
                    region=new Rect(left,space.Bottom,space.Right,space.Top);
                }
                int next=0;
                while(next<items.Count)
                {
                    int before=next;
                    // Each row derives its count from summed actual widths,
                    // maximum height and the free intervals at candidate levels.
                    PlaceRow(region,items,ref next,blocked,plan,null,gap,false,
                        strategy==2,strategy==3,true);
                    if(next==before)break;
                }
                if(next!=items.Count)continue;
                if(bottomUp)
                    for(int i=0;i<plan.Places.Count;i++)plan.Places[i]=FlipY(plan.Places[i]);
                plan.RowsBottomUp=bottomUp;
                if(ValidatePlan(sheet,items,obstacles,main,plan))return plan;
            }
            return new Plan{Error="Không đủ chỗ cho các hàng mặt cắt tại GAP đang thử.",RequestedGap=gap,GlobalGap=gap,RightGap=gap};
        }

        static List<Rect> Blocked(Rect sheet,Rect main,IList<Rect> obstacles,Plan plan,double obstacleGap,double otherGap)
        {
            var result=obstacles.Select(o=>o.Inflate(obstacleGap)).ToList();
            // Never scatter overflow into the left flank of the main views. The row
            // below them retains its full width and its existing centering behavior.
            if(main!=null && main.Left>sheet.Left+Epsilon)
                result.Add(new Rect(sheet.Left,main.Bottom,main.Left,main.Top));
            result.AddRange(plan.Places.Select(p=>p.Inflate(otherGap)));
            return result;
        }

        sealed class Candidate
        {
            public List<Rect> Places;
            public double Score;
        }
        static void PlaceRow(Rect sheet, IList<Item> items, ref int next, List<Rect> blocked,
            Plan plan, Rect main, double gap, bool belowOnly, bool prioritizeCount = false, bool compact = false, bool topFirst=false)
        {
            double desiredTop = main == null ? sheet.Top : Math.Min(sheet.Top, main.Bottom-gap);
            var previousRow=plan.Bands.LastOrDefault(b=>!b.Column);
            double ceiling=previousRow==null ? sheet.Top : plan.Places.Skip(previousRow.Start).Take(previousRow.Count).Min(p=>p.Bottom)-gap;
            if(topFirst)desiredTop=ceiling;
            Candidate best = null;
            double width = 0, height = 0;
            for (int count = 1; next+count <= items.Count; count++)
            {
                var item = items[next+count-1];
                width += item.Width + (count == 1 ? 0 : gap);
                height = Math.Max(height, item.Height);
                if (width > sheet.Width+Epsilon || height > sheet.Height+Epsilon) break;
                var levels = new List<double> { desiredTop, ceiling, sheet.Bottom+height };
                foreach (var b in blocked) { levels.Add(b.Bottom); levels.Add(b.Top+height); }
                foreach (double top in levels.Distinct().OrderByDescending(v=>v))
                {
                    if (top > ceiling+Epsilon || top-height < sheet.Bottom-Epsilon || belowOnly && top > desiredTop+Epsilon) continue;
                    var intervals = FreeIntervals(sheet.Left, sheet.Right,
                        blocked.Where(b=>b.Bottom < top-Epsilon && b.Top > top-height+Epsilon), true);
                    foreach (var interval in intervals)
                    {
                        if (interval.Item2-interval.Item1 < width-Epsilon) continue;
                        double left = Math.Max(interval.Item1, Math.Min(sheet.CenterX-width/2, interval.Item2-width));
                        if(compact)left=interval.Item1;
                        // Prefer the next readable row below the main cluster, then above it.
                        double zone = top <= desiredTop+Epsilon ? 0 : 2;
                        double score = zone*1e9 + Math.Abs(top-desiredTop)*1e5 - count*1000 + Math.Abs(left+width/2-sheet.CenterX);
                        if(prioritizeCount)score=-count*1e12+score;
                        if(compact)score=(ceiling-top)*1e6-count*1000+left;
                        if (best != null && score >= best.Score) continue;
                        var places = new List<Rect>(); double x = left;
                        for (int k=0;k<count;k++)
                        { var i=items[next+k]; places.Add(new Rect(x,top-height,x+i.Width,top-height+i.Height)); x+=i.Width+gap; }
                        best = new Candidate { Places=places, Score=score };
                    }
                }
            }
            Accept(best, ref next, blocked, plan, gap);
        }

        static void PlaceColumns(Rect sheet, IList<Item> items, ref int next, List<Rect> blocked,
            Plan plan, Rect main, double gap, bool bottomUp, bool rightOnly)
        {
            double nextColumnLeft=rightOnly && main!=null ? Math.Max(sheet.Left,main.Right+gap) : sheet.Left;
            while(next < items.Count)
            {
                Candidate best=null; double height=0,width=0;
                double startX = nextColumnLeft;
                double desiredTop=main == null ? sheet.Top : Math.Min(sheet.Top,main.Top);
                for(int count=1;next+count<=items.Count;count++)
                {
                    var item=items[next+count-1]; height+=item.Height+(count==1?0:gap); width=Math.Max(width,item.Width);
                    if(height>sheet.Height+Epsilon || width>sheet.Right-startX+Epsilon)break;
                    var xs=new List<double>{startX,sheet.Right-width};
                    foreach(var b in blocked){xs.Add(b.Right);xs.Add(b.Left-width);}
                    foreach(double x in xs.Distinct().OrderBy(v=>v))
                    {
                        if(x<startX-Epsilon || x+width>sheet.Right+Epsilon)continue;
                        var intervals=FreeIntervals(sheet.Bottom,sheet.Top,
                            blocked.Where(b=>b.Left<x+width-Epsilon&&b.Right>x+Epsilon),false);
                        foreach(var interval in intervals)
                        {
                            if(interval.Item2-interval.Item1<height-Epsilon)continue;
                            double bottom=bottomUp?interval.Item1:Math.Max(interval.Item1,Math.Min(desiredTop-height,interval.Item2-height));
                            double score=(x-startX)*1e6-count*1000 + (bottomUp?bottom-sheet.Bottom:Math.Abs(bottom+height-desiredTop));
                            if(best!=null&&score>=best.Score)continue;
                            var places=new List<Rect>();double y=bottomUp?bottom:bottom+height;
                            for(int k=0;k<count;k++)
                            {var i=items[next+k];double left=x+(width-i.Width)/2;double low=bottomUp?y:y-i.Height;
                                places.Add(new Rect(left,low,left+i.Width,low+i.Height));y+=bottomUp?i.Height+gap:-i.Height-gap;}
                            best=new Candidate{Places=places,Score=score};
                        }
                    }
                }
                if(best==null)break;
                nextColumnLeft=best.Places.Max(p=>p.Right)+gap;
                Accept(best,ref next,blocked,plan,gap,rightOnly,true,bottomUp);
            }
        }
        static void Accept(Candidate best,ref int next,List<Rect> blocked,Plan plan,double gap,bool rightGroup=false,bool column=false,bool bottomUp=false)
        {
            if(best==null)return;
            plan.Bands.Add(new Band{Start=next,Count=best.Places.Count,Column=column,BottomUp=bottomUp});
            foreach(var place in best.Places){plan.Places.Add(place);plan.RightGroup.Add(rightGroup);blocked.Add(place.Inflate(gap));}
            next+=best.Places.Count;
        }
        static List<Tuple<double,double>> FreeIntervals(double min,double max,IEnumerable<Rect> obstacles,bool xAxis)
        {
            var result=new List<Tuple<double,double>>();double cursor=min;
            foreach(var b in obstacles.OrderBy(b=>xAxis?b.Left:b.Bottom))
            {
                double lo=xAxis?b.Left:b.Bottom,hi=xAxis?b.Right:b.Top;
                if(hi<=cursor || lo>=max)continue;
                if(lo>cursor)result.Add(Tuple.Create(cursor,Math.Min(lo,max)));
                cursor=Math.Max(cursor,hi);if(cursor>=max)break;
            }
            if(cursor<max)result.Add(Tuple.Create(cursor,max));return result;
        }
        internal static bool Validate(Rect sheet,IList<Item> items,IList<Rect> obstacles,IList<Rect> places,double gap)
        {
            if(places.Count!=items.Count)return false;
            for(int i=0;i<places.Count;i++)
            {
                var p=places[i];
                if(p==null||!p.Valid||!sheet.Contains(p)||Math.Abs(p.Width-items[i].Width)>.05||Math.Abs(p.Height-items[i].Height)>.05)return false;
                if(obstacles.Any(b=>b.Inflate(gap).Overlaps(p)))return false;
                for(int j=0;j<i;j++)if(places[j].Inflate(gap).Overlaps(p))return false;
            }
            return true;
        }
        internal static bool ValidatePlan(Rect sheet,IList<Item> items,IList<Rect> obstacles,Rect main,Plan plan,IList<Rect> actual=null)
        {
            var places=actual??plan.Places;
            if(places.Count!=items.Count || plan.RightGroup.Count!=items.Count || !Finite(plan.GlobalGap) ||
                !Finite(plan.RightGap) || plan.RightGap<0 || plan.GlobalGap<0)return false;
            for(int i=0;i<places.Count;i++)
            {
                var p=places[i];
                if(p==null||!p.Valid||!sheet.Contains(p)||Math.Abs(p.Width-items[i].Width)>.05||Math.Abs(p.Height-items[i].Height)>.05)return false;
                if(main!=null && p.Left<main.Left-Epsilon && p.Bottom<main.Top-Epsilon && p.Top>main.Bottom+Epsilon)return false;
                double fixedGap=plan.RightGroup[i]?plan.RightGap:plan.GlobalGap;
                if(obstacles.Any(b=>b.Inflate(fixedGap).Overlaps(p)))return false;
                for(int j=0;j<i;j++)
                {
                    double between=plan.RightGroup[i]&&plan.RightGroup[j]?plan.RightGap:plan.GlobalGap;
                    if(places[j].Inflate(between).Overlaps(p))return false;
                }
            }
            if(plan.GridColumns>0)
            {
                for(int i=plan.GridColumns;i<places.Count;i++)
                    if(Math.Abs(places[i].Left+Anchor(items[i])-places[i%plan.GridColumns].Left-Anchor(items[i%plan.GridColumns]))>.1)return false;
                for(int i=2;i<Math.Min(plan.GridColumns,places.Count);i++)
                    if(Math.Abs((places[i].Left+Anchor(items[i])-places[i-1].Left-Anchor(items[i-1]))
                        -(places[1].Left+Anchor(items[1])-places[0].Left-Anchor(items[0])))>.1)return false;
            }
            return ValidateReadingOrder(plan,places);
        }

        internal static bool ValidateReadingOrder(Plan plan,IList<Rect> places)
        {
            if(plan.GridColumns>0)
            {
                int gridNext=0;double previous=plan.RowsBottomUp?Double.NegativeInfinity:Double.PositiveInfinity;
                foreach(var band in plan.Bands)
                {
                    if(band.Column||band.Start!=gridNext||band.Count!=Math.Min(plan.GridColumns,places.Count-gridNext)||band.Count<=0)return false;
                    var row=places.Skip(gridNext).Take(band.Count).ToList();
                    if(plan.RowsBottomUp?row.Min(p=>p.Bottom)<previous-Epsilon:row.Max(p=>p.Top)>previous+Epsilon)return false;
                    previous=plan.RowsBottomUp?row.Max(p=>p.Top):row.Min(p=>p.Bottom);
                    for(int i=1;i<row.Count;i++)if(row[i].Left<row[i-1].Right-Epsilon||Math.Abs(row[i].Bottom-row[0].Bottom)>.1)return false;
                    gridNext+=band.Count;
                }
                return gridNext==places.Count;
            }
            if(plan.RowsBottomUp)
            {
                // Vertical rows always read left-to-right. Reflection converts
                // bottom-to-top band progression back to the common validator.
                if(plan.Horizontal || plan.Bands.Any(b=>b.Column))return false;
                var descending=new Plan{Horizontal=false};
                descending.Bands.AddRange(plan.Bands);
                return ValidateReadingOrder(descending,places.Select(FlipY).ToList());
            }
            int next=0;double previousRowBottom=Double.PositiveInfinity,previousColumnRight=Double.NegativeInfinity;
            bool columnsEnded=false,seenColumn=false,seenRow=false;
            foreach(var band in plan.Bands)
            {
                if(band.Start!=next || band.Count<=0 || next+band.Count>places.Count)return false;
                var group=places.Skip(next).Take(band.Count).ToList();
                if(band.Column)
                {
                    if(columnsEnded || (!plan.Horizontal && seenRow))return false;
                    if(group.Min(p=>p.Left)<previousColumnRight-Epsilon)return false;
                    previousColumnRight=group.Max(p=>p.Right);seenColumn=true;
                    for(int i=1;i<group.Count;i++)
                    {
                        if(band.BottomUp ? group[i].Bottom<group[i-1].Top-Epsilon : group[i].Top>group[i-1].Bottom+Epsilon)return false;
                    }
                }
                else
                {
                    if(group.Max(p=>p.Top)>previousRowBottom+Epsilon)return false;
                    previousRowBottom=group.Min(p=>p.Bottom);seenRow=true;
                    if(seenColumn)columnsEnded=true;
                    for(int i=1;i<group.Count;i++)
                        if(group[i].Left<group[i-1].Right-Epsilon || Math.Abs(group[i].Bottom-group[0].Bottom)>.1)return false;
                }
                next+=band.Count;
            }
            return next==places.Count;
        }
    }
}
#endregion

#region PHU_ArrangePresentation - Đọc khung view, bảng layout và vật cản đang hiển thị


namespace TTSK_AutoDim_Plates
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Collections;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using System.Threading;
    using D = Tekla.Structures.Drawing;
    using Rect = TTSK_AutoDim_Plates.PHU_SectionLayoutPlanner.Rect;

    // Đọc sheet đang hiển thị, gồm bảng layout và keyplan liên kết API thường không liệt kê.
    // Dữ liệu vật cản lấy từ trạng thái hiện tại, không dùng snapshot đã lưu.
    internal static class PHU_ArrangePresentation
    {
        [Serializable]
        public sealed class Data
        {
            public List<Rect> Obstacles = new List<Rect>();
            public List<int> ViewIds = new List<int>();
            public List<double> ViewX = new List<double>(), ViewY = new List<double>();
        }
        internal static Data Read(int[] viewIds, double width, double height,int? ignoredRectangle=null)
        {
            var protectedTables=new List<int>();
            foreach(int id in Tekla.Structures.DrawingInternal.TableLayout.GetCurrentTables())
            {
                var table=new Tekla.Structures.DrawingInternal.LayoutTable{Id=id};
                if(!table.Select())throw new InvalidOperationException("Không đọc được bảng layout hiện tại.");
                if(table.IsTableKeyplan() || table.IsTableDwg())protectedTables.Add(id);
            }
            string bin = Path.GetDirectoryName(typeof(D.Drawing).Assembly.Location);
            var domain = AppDomain.CreateDomain("TTSK Arrange geometry " + Guid.NewGuid(), null,
                new AppDomainSetup { ApplicationBase=bin, ConfigurationFile=Path.Combine(bin,"TeklaStructures.exe.config") });
            try
            {
                var reader=(PHU_ArrangePresentationReader)domain.CreateInstanceFromAndUnwrap(
                    typeof(PHU_ArrangePresentationReader).Assembly.Location,typeof(PHU_ArrangePresentationReader).FullName);
                return (Data)reader.ReadCurrent(viewIds,width,height,protectedTables.ToArray(),ignoredRectangle);
            }
            finally { AppDomain.Unload(domain); }
        }
        internal static object Member(object value,string name)
        {
            var p=value.GetType().GetProperty(name);return p==null?null:p.GetValue(value,null);
        }
        static double Number(object o,string name)
        {
            if(o==null)throw new InvalidDataException("Thiếu tọa độ hiển thị: "+name);
            var value=Member(o,name);if(value==null)throw new InvalidDataException("Thiếu tọa độ hiển thị: "+name);
            double number=Convert.ToDouble(value,System.Globalization.CultureInfo.InvariantCulture);
            if(!PHU_SectionLayoutPlanner.Finite(number))throw new InvalidDataException("Tọa độ hiển thị không hữu hạn: "+name);
            return number;
        }
        static IEnumerable<object> Children(object o,string name="Primitives")
        { var e=Member(o,name) as IEnumerable;return e==null?Enumerable.Empty<object>():e.Cast<object>(); }

        internal static Data Parse(IEnumerable raw,int[] viewIds,double width,double height,int[] protectedTableIds=null,int? ignoredRectangle=null)
        {
            var result=new Data();var known=new HashSet<int>(viewIds);
            var protectedTables=new HashSet<int>(protectedTableIds??new int[0]);
            foreach(var view in raw)
            {
                int id=(int)Number(view,"Id");var position=Member(view,"Position");
                double x=Number(position,"X"),y=Number(position,"Y");
                if(known.Contains(id))
                {result.ViewIds.Add(id);result.ViewX.Add(x);result.ViewY.Add(y);continue;}
                if(id>0)
                {
                    // A rendered view absent from Drawing API views is linked layout
                    // content (e.g. keyplan). Protect its whole interior, not each part.
                    var linked=new List<Rect>();foreach(var group in Children(view))Collect(group,x,y,linked);
                    if(linked.Count>0)result.Obstacles.Add(Union(linked));continue;
                }
                foreach(var group in Children(view))
                {
                    if(ignoredRectangle.HasValue && Convert.ToInt32(Member(group,"Id")??0)==ignoredRectangle.Value)continue;
                    var pieces=new List<Rect>(); Collect(group,x,y,pieces);
                    if(pieces.Count==0)continue;
                    var bounds=Union(pieces);
                    bool nativeFrame=Convert.ToString(Member(group,"ObjectType"))=="266";
                    bool paperOutline=bounds.Contains(new Rect(.5,.5,width-.5,height-.5));
                    int groupId=Convert.ToInt32(Member(group,"Id")??0);
                    if(nativeFrame || paperOutline && !protectedTables.Contains(groupId))
                    {
                        // Some title templates include the drawing frame. Retain the frame's
                        // actual strokes, but do not reserve the whole interior of the sheet.
                        var interior=new List<Rect>();
                        foreach(var p in pieces)
                            if(p.Width>=width*.8 || p.Height>=height*.8)result.Obstacles.Add(p);
                            else interior.Add(p);
                        result.Obstacles.AddRange(Components(interior));
                        result.Obstacles.AddRange(ClosedCells(pieces,width,height));
                    }
                    else result.Obstacles.Add(bounds); // Entire table/keyplan, including empty cells.
                }
            }
            if(result.ViewIds.Count!=known.Count)
                throw new InvalidOperationException("Dữ liệu hiển thị chưa khớp các view hiện tại. Hãy refresh bản vẽ và chạy lại Arrange.");
            var compact=new List<Rect>();
            foreach(var obstacle in result.Obstacles.OrderByDescending(b=>b.Width*b.Height).ThenBy(b=>b.Left).ThenBy(b=>b.Bottom))
            {
                if(!obstacle.Valid)throw new InvalidOperationException("Biên hiển thị bảng/keyplan không hợp lệ.");
                if(!compact.Any(b=>b.Contains(obstacle)))compact.Add(obstacle);
            }
            result.Obstacles=compact;
            return result;
        }
        static IEnumerable<Rect> ClosedCells(List<Rect> pieces,double width,double height)
        {
            // Fill empty table cells, too. Merely avoiding grid strokes could put a
            // small section inside a large empty cell of a frame-bearing template.
            var horizontal=pieces.Where(p=>p.Height<=.21&&p.Width>2).ToList();
            var vertical=pieces.Where(p=>p.Width<=.21&&p.Height>2).ToList();
            for(int i=0;i<horizontal.Count;i++)for(int j=i+1;j<horizontal.Count;j++)
            {
                var a=horizontal[i];var b=horizontal[j];
                double bottom=Math.Min(a.Bottom,b.Bottom)+.1,top=Math.Max(a.Bottom,b.Bottom)+.1;
                double left=Math.Max(a.Left,b.Left),right=Math.Min(a.Right,b.Right);
                if(top-bottom<.5 || right-left<.5)continue;
                var xs=vertical.Where(v=>v.Bottom<=bottom+.2&&v.Top>=top-.2&&v.CenterX>=left-.2&&v.CenterX<=right+.2)
                    .Select(v=>v.CenterX).Distinct().OrderBy(x=>x).ToList();
                for(int k=1;k<xs.Count;k++)
                {
                    var cell=new Rect(xs[k-1],bottom,xs[k],top);
                    if(cell.Width<.5)continue;
                    // This branch is reached only for a proven paper-frame template.
                    // An enclosing drawing frame is a border, not a table cell.
                    if(cell.Width>=width*.8&&cell.Height>=height*.8)continue;
                    yield return cell;
                }
            }
        }
        static Rect Union(IEnumerable<Rect> pieces)
        {return new Rect(pieces.Min(p=>p.Left),pieces.Min(p=>p.Bottom),pieces.Max(p=>p.Right),pieces.Max(p=>p.Top));}
        static List<Rect> Components(List<Rect> pieces)
        {
            // Connected cells of a frame-bearing template become solid occupied regions.
            var merged=new List<Rect>();
            foreach(var piece in pieces)
            {
                var current=piece;bool changed;
                do
                {
                    changed=false;
                    for(int i=merged.Count-1;i>=0;i--)
                        if(current.Inflate(.2).Overlaps(merged[i]))
                        {current=Union(new[]{current,merged[i]});merged.RemoveAt(i);changed=true;}
                }while(changed);
                merged.Add(current);
            }
            return merged;
        }
        static void PointBox(object p,double x,double y,List<Rect> result,double radius=.1)
        { double px=Number(p,"X")+x,py=Number(p,"Y")+y;result.Add(new Rect(px-radius,py-radius,px+radius,py+radius)); }
        static void Collect(object p,double x,double y,List<Rect> result)
        {
            string type=p.GetType().Name;
            if(type=="Segment" || type=="PrimitiveGroup")
            {foreach(var c in Children(p))Collect(c,x,y,result);return;}
            if(type=="LinePrimitive")
            {
                var a=Member(p,"StartPoint");var b=Member(p,"EndPoint");
                result.Add(new Rect(Math.Min(Number(a,"X"),Number(b,"X"))+x-.1,Math.Min(Number(a,"Y"),Number(b,"Y"))+y-.1,
                    Math.Max(Number(a,"X"),Number(b,"X"))+x+.1,Math.Max(Number(a,"Y"),Number(b,"Y"))+y+.1));return;
            }
            if(type=="PointPrimitive")return; // Invisible construction/anchor point.
            if(type=="PathPrimitive" || type=="LoopPrimitive")
            {foreach(var s in Children(p,"Segments"))Collect(s,x,y,result);return;}
            if(type=="PolygonPrimitive")
            {Collect(Member(p,"OuterLoop"),x,y,result);return;}
            if(type=="CirclePrimitive" || type=="ArcPrimitive")
            {
                object center;double radius;
                if(type=="CirclePrimitive"){center=Member(p,"CenterPoint");radius=Number(p,"Radius");}
                else
                {
                    // Enclose the full circle: safe for any arc sweep and bulge sign.
                    var a=Member(p,"StartPoint");var b=Member(p,"EndPoint");double bulge=Number(p,"Bulge");
                    double ax=Number(a,"X"),ay=Number(a,"Y"),bx=Number(b,"X"),by=Number(b,"Y");
                    if(Math.Abs(bulge)<1e-9){PointBox(a,x,y,result);PointBox(b,x,y,result);return;}
                    double chord=Math.Sqrt((bx-ax)*(bx-ax)+(by-ay)*(by-ay));
                    radius=chord*(1+bulge*bulge)/(4*Math.Abs(bulge));
                    double cx=(ax+bx)/2-(by-ay)*(1-bulge*bulge)/(4*bulge),cy=(ay+by)/2+(bx-ax)*(1-bulge*bulge)/(4*bulge);
                    result.Add(new Rect(cx+x-radius,cy+y-radius,cx+x+radius,cy+y+radius));return;
                }
                PointBox(center,x,y,result,Math.Max(.1,radius));return;
            }
            if(type=="TextPrimitive" || type=="SymbolPrimitive" || type=="BitmapPrimitive")
            {
                var at=Member(p,"Position");double h=Number(p,"Height"),w;
                if(type=="TextPrimitive")
                    w=Convert.ToString(Member(p,"Text")).Length*Math.Max(h,Number(p,"Proportion"))*1.15;
                else w=Number(p,"Width");
                object angleValue=Member(p,"Angle");
                double angle=angleValue is double?(double)angleValue:Number(angleValue,"Radians");
                double px=Number(at,"X")+x,py=Number(at,"Y")+y;
                // Text descent and a conservative glyph width protect marks beyond table lines.
                var corners=new List<Rect>();
                foreach(double dx in new[]{-.2,w+.2})foreach(double dy in new[]{-h*.3,h*1.2})
                {double xx=px+dx*Math.Cos(angle)-dy*Math.Sin(angle),yy=py+dx*Math.Sin(angle)+dy*Math.Cos(angle);corners.Add(new Rect(xx-.1,yy-.1,xx+.1,yy+.1));}
                result.Add(Union(corners));return;
            }
            throw new NotSupportedException("Không đọc được vùng né của đối tượng hiển thị " + type + ". Chưa di chuyển view.");
        }
    }
    public sealed class PHU_ArrangePresentationReader : MarshalByRefObject
    {
        static object Member(object o,string name) {return PHU_ArrangePresentation.Member(o,name);}
            [DllImport("wtsapi32.dll",SetLastError=true,CharSet=CharSet.Unicode)]
            static extern bool WTSQuerySessionInformation(IntPtr server,int session,int info,out IntPtr buffer,out int bytes);
            [DllImport("wtsapi32.dll")] static extern void WTSFreeMemory(IntPtr buffer);
            public object ReadCurrent(int[] drawingViewIds, double paperWidth, double paperHeight,int[] protectedTableIds,int? ignoredRectangle)
            {
                string bin=Path.GetDirectoryName(typeof(D.Drawing).Assembly.Location);
                // Use Tekla's presentation RPC directly: DrawingHandler remoting is not initialized in this AppDomain.
                // Explicit RPC deadline avoids Tekla's default one-day timeout when its service is unavailable.
                string session=Environment.GetEnvironmentVariable("SESSIONNAME");
                if(String.IsNullOrEmpty(session))
                {
                    IntPtr ptr;int bytes;
                    if(!WTSQuerySessionInformation(IntPtr.Zero,System.Diagnostics.Process.GetCurrentProcess().SessionId,6,out ptr,out bytes))
                        throw new InvalidOperationException("Không đọc được phiên Tekla để lấy nội dung hiện hành.");
                    try { session=Marshal.PtrToStringUni(ptr); } finally { WTSFreeMemory(ptr); }
                }
                string version=typeof(D.Drawing).Assembly.GetName().Version.Major+".0";
                using(var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Trimble\Tekla Structures\"+version+@"\Drawings\PresentationPlugin\"+session))
                {
                    int port=key==null?0:Convert.ToInt32(key.GetValue("GrpcServerPort",0));
                    if(port<=0)throw new InvalidOperationException("Không tìm thấy dịch vụ nội dung bản vẽ trong phiên Tekla hiện tại.");
                    var grpc=Assembly.LoadFrom(Path.Combine(bin,"Grpc.Core.dll"));
                    var api=Assembly.LoadFrom(Path.Combine(bin,"Tekla.Structures.DrawingPresentationModelConvertor.dll"));
                    var channelType=grpc.GetType("Grpc.Core.Channel",true);
                    var credentialsType=grpc.GetType("Grpc.Core.ChannelCredentials",true);
                    var channel=Activator.CreateInstance(channelType,new object[]{"localhost",port,credentialsType.GetProperty("Insecure").GetValue(null,null)});
                    try
                    {
                        var clientType=api.GetType("Tekla.Structures.DrawingPresentationModelInterface.DrawingPresentationModelService+DrawingPresentationModelServiceClient",true);
                        var client=Activator.CreateInstance(clientType,new[]{channel});
                        var request=Activator.CreateInstance(Assembly.LoadFrom(Path.Combine(bin,"Tekla.Structures.GrpcContracts.dll")).GetType("Tekla.Structures.DrawingPresentationModelInterface.GetCurrentPresentationModelRequest",true));
                        var method=clientType.GetMethods().Single(m=>m.Name=="GetCurrentPresentationModel"&&m.GetParameters().Length==4);
                        var response=method.Invoke(client,new object[]{request,null,(DateTime?)DateTime.UtcNow.AddSeconds(15),CancellationToken.None});
                        var convertor=Assembly.LoadFrom(Path.Combine(bin,"Tekla.Structures.DrawingPresentationModelConvertor.dll"));
                        var convert=convertor.GetType("Tekla.Structures.DrawingPresentationModelConvertor.GrpcMessageConvertor",true).GetMethod("GetViews");
                        var raw=convert.Invoke(null,new[]{Member(response,"Views")}) as IEnumerable;
                        if(raw==null)throw new InvalidDataException("Không đọc được vùng né trên bản vẽ hiện hành. Chưa di chuyển view.");
                        return PHU_ArrangePresentation.Parse(raw, drawingViewIds, paperWidth, paperHeight,protectedTableIds,ignoredRectangle);
                    }
                    finally { channelType.GetMethod("ShutdownAsync").Invoke(channel,null); }
                }
            }


    }
}
#endregion

#region PHU_VerticalShapeViewLayoutContext - Giữ ngữ cảnh bố cục Shape đứng
#pragma warning disable 1633

namespace Tekla.Technology.Akit.UserScript
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Reflection;
    using TSD = Tekla.Structures.Drawing;
    using TSG = Tekla.Structures.Geometry3d;
    using TSM = Tekla.Structures.Model;

    /// <summary>
    /// Shared H/C/L vertical-member layout policy.  It is project-independent:
    /// remember the user's two sheet positions before Shape, suppress legacy
    /// ViewType-based arranging, then preserve both X positions/order and align
    /// the RIGHT view Y to the LEFT view Y exactly once after Shape.
    /// </summary>
    public static class PHU_VerticalShapeViewLayoutContext
    {
        // NGƯỠNG GIỮ BỐ CỤC: Tolerance là mm hình học; cosine gần 1 đòi hướng trục khớp chặt hơn.
        private const double Tolerance = 0.20;
        private const double DirectionCosineTolerance = 0.985;

        private sealed class Snapshot
        {
            public int Identifier;
            public TSD.View View;
            public TSG.Point Origin;
        }

        private sealed class Context
        {
            public TSD.Drawing Drawing;
            public Snapshot Left;
            public Snapshot Right;
        }

        private static Context _context;

        public static bool ShouldPreserveUserViewLayout
        {
            get { return _context != null; }
        }

        public static bool LastAlignmentApplied { get; private set; }
        public static string LastMessage { get; private set; }

        public static void Reset()
        {
            _context = null;
            LastAlignmentApplied = false;
            LastMessage = String.Empty;
        }

        /// <summary>
        /// Read-only.  A horizontal H/C/L main member does not match this
        /// topology and therefore keeps its existing company arrangement.
        /// </summary>
        public static bool Begin(TSM.Model model, TSD.Drawing drawing, TSM.Part mainPart)
        {
            Reset();
            try
            {
                if (
                    model == null
                    || drawing == null
                    || mainPart == null
                    || !model.GetConnectionStatus()
                )
                    return false;

                TSG.Matrix currentToGlobal = model
                    .GetWorkPlaneHandler()
                    .GetCurrentTransformationPlane()
                    .TransformationMatrixToGlobal;
                List<Snapshot> candidates = FindVerticalMainViews(
                    drawing,
                    mainPart,
                    currentToGlobal
                );
                if (candidates.Count != 2)
                {
                    LastMessage =
                        "Vertical H/C/L user-layout capture skipped: expected 2 full views, found "
                        + candidates.Count.ToString(CultureInfo.InvariantCulture)
                        + ".";
                    return false;
                }

                candidates.Sort(
                    delegate(Snapshot a, Snapshot b)
                    {
                        double dx = a.Origin.X - b.Origin.X;
                        if (Math.Abs(dx) > Tolerance)
                            return dx < 0.0 ? -1 : 1;
                        return a.Identifier.CompareTo(b.Identifier);
                    }
                );
                if (Math.Abs(candidates[0].Origin.X - candidates[1].Origin.X) <= Tolerance)
                {
                    LastMessage =
                        "Vertical H/C/L user-layout capture skipped: left/right X order is ambiguous.";
                    return false;
                }

                Context context = new Context();
                context.Drawing = drawing;
                context.Left = candidates[0];
                context.Right = candidates[1];
                _context = context;
                LastMessage =
                    "Vertical H/C/L user layout captured LEFT="
                    + context.Left.Identifier.ToString(CultureInfo.InvariantCulture)
                    + ", RIGHT="
                    + context.Right.Identifier.ToString(CultureInfo.InvariantCulture)
                    + ".";
                return true;
            }
            catch (Exception ex)
            {
                _context = null;
                LastMessage = "Vertical H/C/L user-layout capture skipped. " + ex.Message;
                return false;
            }
        }

        public static bool TryGetCapturedViewOrder(
            out int leftIdentifier,
            out int rightIdentifier,
            out TSG.Point leftOrigin,
            out TSG.Point rightOrigin
        )
        {
            leftIdentifier = 0;
            rightIdentifier = 0;
            leftOrigin = null;
            rightOrigin = null;
            if (_context == null || _context.Left == null || _context.Right == null)
                return false;
            leftIdentifier = _context.Left.Identifier;
            rightIdentifier = _context.Right.Identifier;
            leftOrigin = ClonePoint(_context.Left.Origin);
            rightOrigin = ClonePoint(_context.Right.Origin);
            return leftIdentifier != 0
                && rightIdentifier != 0
                && leftOrigin != null
                && rightOrigin != null;
        }

        public static bool AlignOnceAfterShape()
        {
            LastAlignmentApplied = false;
            if (_context == null)
                return false;

            try
            {
                RebindViews(_context);
                TSG.Point leftTarget = ClonePoint(_context.Left.Origin);
                TSG.Point rightTarget = new TSG.Point(
                    _context.Right.Origin.X,
                    _context.Left.Origin.Y,
                    _context.Right.Origin.Z
                );

                if (
                    !SetOrigin(_context.Left.View, leftTarget)
                    || !SetOrigin(_context.Right.View, rightTarget)
                )
                    throw new InvalidOperationException("Tekla rejected a captured view Origin.");
                _context.Drawing.CommitChanges();

                TSG.Point leftRead = _context.Left.View.Origin;
                TSG.Point rightRead = _context.Right.View.Origin;
                if (
                    Distance3(leftRead, leftTarget) > 0.05
                    || Distance3(rightRead, rightTarget) > 0.05
                    || rightRead.X <= leftRead.X + 0.05
                )
                    throw new InvalidOperationException("Origin read-back verification failed.");

                LastAlignmentApplied = true;
                LastMessage =
                    "Vertical H/C/L aligned once: original X/order preserved, RIGHT Y aligned to LEFT.";
                return true;
            }
            catch (Exception ex)
            {
                RestoreOriginalOrigins();
                TryCommit(_context.Drawing);
                LastMessage =
                    "Vertical H/C/L alignment failed; original origins restored. " + ex.Message;
                return false;
            }
        }

        private static List<Snapshot> FindVerticalMainViews(
            TSD.Drawing drawing,
            TSM.Part mainPart,
            TSG.Matrix currentToGlobal
        )
        {
            List<Snapshot> result = new List<Snapshot>();
            TSD.ContainerView sheet = drawing.GetSheet();
            TSD.DrawingObjectEnumerator views = sheet == null ? null : sheet.GetAllViews();
            while (views != null && views.MoveNext())
            {
                TSD.View view = views.Current as TSD.View;
                if (view == null || IsSectionView(view) || !ViewContainsPart(view, mainPart))
                    continue;
                TSG.Matrix globalToView = TSG.MatrixFactory.ToCoordinateSystem(
                    view.DisplayCoordinateSystem
                );
                if (!HasVerticalReferenceLine(mainPart, currentToGlobal, globalToView))
                    continue;
                int identifier = ReadIdentifier(view);
                TSG.Point origin = ClonePoint(view.Origin);
                if (identifier == 0 || origin == null)
                    continue;
                Snapshot snapshot = new Snapshot();
                snapshot.Identifier = identifier;
                snapshot.View = view;
                snapshot.Origin = origin;
                result.Add(snapshot);
            }
            return result;
        }

        /// <summary>
        /// Section views can contain the MainPart and expose a vertical REF,
        /// but they are not one of the two user-positioned full column views.
        /// Keep this classification shared by layout, Grid, and Neighbor
        /// preflight so an added B/C/D section cannot become a third candidate.
        /// </summary>
        internal static bool IsSectionView(TSD.View view)
        {
            if (view == null)
                return false;

            string runtimeName = view.GetType().Name ?? String.Empty;
            if (runtimeName.IndexOf("Section", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            try
            {
                string viewType = Convert.ToString(
                    view.ViewType,
                    CultureInfo.InvariantCulture
                ) ?? String.Empty;
                return viewType.IndexOf("Section", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool HasVerticalReferenceLine(
            TSM.Part part,
            TSG.Matrix currentToGlobal,
            TSG.Matrix globalToView
        )
        {
            List<TSG.Point> points = new List<TSG.Point>();
            try
            {
                ArrayList line = part.GetReferenceLine(false);
                if (line != null)
                {
                    foreach (object value in line)
                    {
                        TSG.Point point = value as TSG.Point;
                        if (point == null)
                            continue;
                        TSG.Point global = currentToGlobal.Transform(point);
                        points.Add(globalToView.Transform(global));
                    }
                }
            }
            catch
            {
                return false;
            }

            double bestLength = 0.0;
            double bestVerticalCosine = 0.0;
            for (int i = 0; i < points.Count; i++)
            {
                for (int j = i + 1; j < points.Count; j++)
                {
                    double dx = points[j].X - points[i].X;
                    double dy = points[j].Y - points[i].Y;
                    double length = Math.Sqrt((dx * dx) + (dy * dy));
                    if (length <= bestLength)
                        continue;
                    bestLength = length;
                    bestVerticalCosine = length <= Tolerance ? 0.0 : Math.Abs(dy / length);
                }
            }
            return bestLength > Tolerance && bestVerticalCosine >= DirectionCosineTolerance;
        }

        private static bool ViewContainsPart(TSD.View view, TSM.Part mainPart)
        {
            TSD.DrawingObjectEnumerator parts = view.GetAllObjects(typeof(TSD.Part));
            while (parts != null && parts.MoveNext())
            {
                TSD.Part part = parts.Current as TSD.Part;
                if (
                    part != null
                    && part.ModelIdentifier != null
                    && mainPart.Identifier != null
                    && part.ModelIdentifier.ID == mainPart.Identifier.ID
                )
                    return true;
            }
            return false;
        }

        private static void RebindViews(Context context)
        {
            TSD.ContainerView sheet = context.Drawing.GetSheet();
            TSD.DrawingObjectEnumerator views = sheet == null ? null : sheet.GetAllViews();
            TSD.View left = null;
            TSD.View right = null;
            while (views != null && views.MoveNext())
            {
                TSD.View view = views.Current as TSD.View;
                int identifier = ReadIdentifier(view);
                if (identifier == context.Left.Identifier)
                    left = view;
                if (identifier == context.Right.Identifier)
                    right = view;
            }
            if (left == null || right == null)
                throw new InvalidOperationException("A captured vertical Shape view disappeared.");
            context.Left.View = left;
            context.Right.View = right;
        }

        private static void RestoreOriginalOrigins()
        {
            if (_context == null)
                return;
            SetOrigin(_context.Left.View, ClonePoint(_context.Left.Origin));
            SetOrigin(_context.Right.View, ClonePoint(_context.Right.Origin));
        }

        private static bool SetOrigin(TSD.View view, TSG.Point origin)
        {
            if (view == null || origin == null)
                return false;
            if (Distance3(view.Origin, origin) <= 0.01)
                return true;
            view.Origin = ClonePoint(origin);
            return view.Modify();
        }

        private static int ReadIdentifier(object value)
        {
            try
            {
                PropertyInfo property =
                    value == null
                        ? null
                        : value
                            .GetType()
                            .GetProperty(
                                "Identifier",
                                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                            );
                Tekla.Structures.Identifier identifier =
                    property == null
                        ? null
                        : property.GetValue(value, null) as Tekla.Structures.Identifier;
                return identifier == null ? 0 : identifier.ID;
            }
            catch
            {
                return 0;
            }
        }

        private static TSG.Point ClonePoint(TSG.Point point)
        {
            return point == null ? null : new TSG.Point(point.X, point.Y, point.Z);
        }

        private static double Distance3(TSG.Point first, TSG.Point second)
        {
            if (first == null || second == null)
                return Double.PositiveInfinity;
            double dx = first.X - second.X;
            double dy = first.Y - second.Y;
            double dz = first.Z - second.Z;
            return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        }

        private static void TryCommit(TSD.Drawing drawing)
        {
            try
            {
                if (drawing != null)
                    drawing.CommitChanges();
            }
            catch { }
        }
    }
}
#pragma warning restore
#endregion
