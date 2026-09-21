using System;
using System.Collections.Generic;
using System.Linq;

namespace TTSK_AutoDim_Plates
{
    // All dimensions are millimetres on paper. This planner makes no Tekla calls.
    internal static class PHU_SectionLayoutPlanner
    {
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
        }
        internal sealed class Plan
        {
            public readonly List<Rect> Places = new List<Rect>();
            public readonly List<bool> RightGroup = new List<bool>();
            public readonly List<Band> Bands = new List<Band>();
            public bool Horizontal;
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
            Rect main, double gap, bool horizontal, bool bottomUp)
        {
            if (sheet == null || !sheet.Valid || !Finite(gap) || gap < 0 ||
                items.Any(i => !Finite(i.Width) || !Finite(i.Height) || i.Width <= 0 || i.Height <= 0) ||
                obstacles.Any(o => o == null || !o.Valid))
                return new Plan { Error = "Kích thước hoặc khoảng cách Arrange không hợp lệ." };
            if (items.Count == 0) return new Plan();
            // Preserve the preferred layout first. When its right cluster is full,
            // compress that cluster before changing the rest of the drawing.
            var plan=BuildFixed(sheet,items,obstacles,main,gap,gap,horizontal,bottomUp,true);
            if(plan.Success)return plan;
            if(main!=null && gap>0)
            {
                plan=FindLargestFittingGap(gap,g=>BuildFixed(sheet,items,obstacles,main,gap,g,horizontal,bottomUp,true));
                if(plan.Success)return plan;
            }
            // Reduce the preferred, readable layout before trying another topology.
            // A geometrically valid scatter into leftover pockets is not a layout.
            if(gap>0)
            {
                plan=FindLargestFittingGap(gap,g=>BuildFixed(sheet,items,obstacles,main,g,g,horizontal,bottomUp,true));
                plan.RequestedGap=gap;
                if(plan.Success)return plan;
            }
            plan=BuildFixed(sheet,items,obstacles,main,gap,gap,horizontal,bottomUp,false);
            if(plan.Success)return plan;
            if(gap>0)
            {
                plan=FindLargestFittingGap(gap,g=>BuildFixed(sheet,items,obstacles,main,g,g,horizontal,bottomUp,false));
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
            double gap,double rightGap,bool horizontal,bool bottomUp,bool preferredOnly)
        {
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
            return ValidateReadingOrder(plan,places);
        }

        internal static bool ValidateReadingOrder(Plan plan,IList<Rect> places)
        {
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
