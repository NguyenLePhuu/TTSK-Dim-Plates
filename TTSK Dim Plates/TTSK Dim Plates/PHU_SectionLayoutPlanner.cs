using System;
using System.Collections.Generic;
using System.Linq;

namespace TTSK_AutoDim_Plates
{
    // All dimensions are millimetres on paper. This planner makes no Tekla calls.
    internal static class PHU_SectionLayoutPlanner
    {
        internal enum VerticalLayout { LegacyColumns, AdaptiveRows, AlignedGrid }
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
