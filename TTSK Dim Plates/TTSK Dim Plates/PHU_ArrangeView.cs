using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;
using System.Collections;
using Tekla.Structures.Drawing.UI;

namespace TTSK_AutoDim_Plates
{
    public static class PHU_ArrangeView
    {
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

                // SECTION ONLY: không move Main View / Top / Bottom / Front / special section.
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
