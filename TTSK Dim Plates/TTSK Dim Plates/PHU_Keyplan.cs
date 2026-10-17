// Slot 09 Keyplan: hướng nhìn, tính góc, đọc thiết lập và giao diện áp dụng.
// LUỒNG: MainForm.RunKeyplanSlot09 → KeyplanForm → đọc frame/thiết lập → Geometry tìm góc hợp lệ → áp dụng thuộc tính.
// ĐIỂM SỬA: hướng và góc ở Geometry; đọc/ghi ở DrawingSettingsReader và dịch vụ apply; giao diện ở region PHU_KeyplanForm.
// THAM SỐ: ToleranceDegrees tính bằng độ; X=0/90/180/270, Y=0/180; Z liên tục, gợi ý theo độ chính xác nhập 0,001°.
// LƯU Ý: vector hình học dùng hệ global; tọa độ bố trí trên giấy không được đưa vào phép tính góc.
// TRA CỨU: PROJECT_MAP.md ở gốc repo; chỉ mục kiểu/hàm và điểm chỉnh thông số.
// Mục lục:
//   PHU_Keyplan.cs
//   PHU_KeyplanForm.cs

#region PHU_Keyplan - Hình học hướng nhìn, đọc/ghi thuộc tính keyplan
// Slot 09 Keyplan: hình học, đọc model, thuộc tính bản vẽ, áp dụng và thiết lập.
// Ghi thuộc tính bản vẽ và thiết lập người dùng trong LocalAppData.

#region Hình học vector và tìm góc
namespace TTSK_AutoDim_Plates.Keyplan
{


using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

    // Hình học thuần: vector ở hệ global; không đưa tọa độ sheet vào phép quay.
    public struct V
    {
        public double X, Y, Z;
        public V(double x, double y, double z) { X=x; Y=y; Z=z; }
        public static V operator +(V a,V b) { return new V(a.X+b.X,a.Y+b.Y,a.Z+b.Z); }
        public static V operator -(V a,V b) { return new V(a.X-b.X,a.Y-b.Y,a.Z-b.Z); }
        public static V operator *(V a,double s) { return new V(a.X*s,a.Y*s,a.Z*s); }
        public static double Dot(V a,V b) { return a.X*b.X+a.Y*b.Y+a.Z*b.Z; }
        public static V Cross(V a,V b) { return new V(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X); }
        public V Unit()
        {
            double scale=Math.Max(Math.Abs(X),Math.Max(Math.Abs(Y),Math.Abs(Z)));
            if(!Geometry.Finite(scale)||scale<1e-12)throw new InvalidOperationException("Vector không hợp lệ.");
            var v=this*(1/scale);return v*(1/Math.Sqrt(Dot(v,v)));
        }
        public override string ToString() { return string.Format(CultureInfo.InvariantCulture,"({0:0.######}, {1:0.######}, {2:0.######})",X,Y,Z); }
    }
    public sealed class Frame
    {
        public readonly V Right, Up;
        public V Normal { get { return V.Cross(Right,Up).Unit(); } }
        public Frame(V right,V up)
        {
            Right=right.Unit(); Up=up.Unit();
            if(Math.Abs(V.Dot(Right,Up))>1e-6) throw new InvalidOperationException("Hai trục không vuông góc.");
        }
        public V ToWorld(V v) { return Right*v.X+Up*v.Y+Normal*v.Z; }
        public V ToLocal(V v) { return new V(V.Dot(v,Right),V.Dot(v,Up),V.Dot(v,Normal)); }
        public Frame Spin(double degrees)
        {
            if(!Geometry.Finite(degrees))throw new InvalidOperationException("Góc không hợp lệ.");
            double a=degrees*Math.PI/180,c=Math.Cos(a),s=Math.Sin(a);
            return new Frame(Right*c+Up*s,Right*(-s)+Up*c);
        }
    }
    public sealed class Angles
    {
        public readonly int X, Y;
        public readonly double Z;
        public Angles(int x,int y,double z)
        {
            if(!Geometry.AllowedQuarter(x)||!(y==0||y==180)||!Geometry.Finite(z))
                throw new InvalidOperationException("X chỉ nhận 0/90/180/270; Y chỉ nhận 0/180; Z phải là góc hữu hạn.");
            X=x;Y=y;Z=Geometry.Wrap(z);
        }
        public override string ToString() { return string.Format(CultureInfo.InvariantCulture,"Local | Around X = {0}° | Around Y = {1}° | Around Z = {2:0.######}°",X,Y,Z); }
    }
    public sealed class Candidate
    {
        public Angles Angles;
        public double Error;
    }
    public sealed class RotationSearch
    {
        public readonly List<Candidate> Evaluated;
        public readonly List<Candidate> Matches;
        internal RotationSearch(List<Candidate> evaluated,List<Candidate> matches) { Evaluated=evaluated;Matches=matches; }
    }
    public sealed class GridTick
    {
        public string Label;
        public double Position;
    }
    public static class Geometry
    {
        // Numerical tolerance only: 0.0001 degree (~0.00175 mm over 1 metre).
        // Never round a requested angle to the nearest allowed value.
        // CHỈNH SAI SỐ GÓC (độ): chỉ dung sai số học; không làm tròn yêu cầu thành một góc được phép.
        public const double ToleranceDegrees=0.0001;
        // Số lẻ độ Z đọc lại từ ô native; chỉ giới hạn ứng viên gợi ý, không làm tròn góc gốc.
        const int ZInputDecimals=3;
        public static bool Finite(double n) { return !double.IsNaN(n)&&!double.IsInfinity(n); }
        public static bool AllowedQuarter(double a) { return a==0||a==90||a==180||a==270; }
        public static IEnumerable<Angles> AllowedAngles()
        {
            // Giữ API/tập 32 góc vuông cũ làm hạt giống; Search bổ sung Z liên tục theo target.
            foreach(int x in new[]{0,90,180,270})foreach(int y in new[]{0,180})foreach(int z in new[]{0,90,180,270})
                yield return new Angles(x,y,z);
        }
        public static double Wrap(double a) { a%=360;if(a<0)a+=360;return Math.Abs(a-360)<1e-9||Math.Abs(a)<1e-9?0:a; }
        public static double Angle(V a,V b)
        {
            a=a.Unit();b=b.Unit();var cross=V.Cross(a,b);
            return Math.Atan2(Math.Sqrt(V.Dot(cross,cross)),V.Dot(a,b))*180/Math.PI;
        }
        public static double Error(Frame a,Frame b) { return Math.Max(Angle(a.Normal,b.Normal),Math.Max(Angle(a.Right,b.Right),Angle(a.Up,b.Up))); }
        // Grid order is a property of its projection on paper, not equality of two
        // 3D bases. A rolled member may foreshorten the grid without reversing it.
        // Also require the expected viewing side to be the UNIQUE dominant side,
        // so a near edge-on Front cannot masquerade as a Top (45-degree ties fail).
        public static double KeyplanError(Frame view,Frame keyView)
        {
            double normal=V.Dot(view.Normal,keyView.Normal);
            if(normal<=Math.Max(Math.Abs(V.Dot(view.Normal,keyView.Right)),Math.Abs(V.Dot(view.Normal,keyView.Up)))+1e-8)return 180;
            var right=new V(V.Dot(keyView.Right,view.Right),V.Dot(keyView.Right,view.Up),0);
            var up=new V(V.Dot(keyView.Up,view.Right),V.Dot(keyView.Up,view.Up),0);
            if(V.Dot(right,right)<1e-16||V.Dot(up,up)<1e-16)return 180;
            return Math.Max(Angle(right,new V(1,0,0)),Angle(up,new V(0,1,0)));
        }
        // Giữ hoán vị chính xác tại góc vuông; Z khác góc vuông dùng sin/cos.
        static int Quarter(double a) { return ((int)a%360+360)%360; }
        static V Rx(V v,double a)
        { switch(Quarter(a)) { case 90:return new V(v.X,-v.Z,v.Y);case 180:return new V(v.X,-v.Y,-v.Z);case 270:return new V(v.X,v.Z,-v.Y);default:return v; } }
        static V Ry(V v,double a)
        { switch(Quarter(a)) { case 180:return new V(-v.X,v.Y,-v.Z);default:return v; } }
        static V Rz(V v,double a)
        {
            double wrapped=Wrap(a);
            if(AllowedQuarter(wrapped))switch((int)wrapped)
            {case 90:return new V(-v.Y,v.X,v.Z);case 180:return new V(-v.X,-v.Y,v.Z);case 270:return new V(v.Y,-v.X,v.Z);default:return v;}
            double angle=a*Math.PI/180,c=Math.Cos(angle),s=Math.Sin(angle);return new V(c*v.X-s*v.Y,s*v.X+c*v.Y,v.Z);
        }
        // Measured from AssemblyDrawing.Insert using saved LOCAL View creation attributes.
        // Intrinsic clockwise X, Y, Z; applied to the common 3D basis BEFORE view projection.
        // Column vertical placing is a separate paper rotation after projection.
        public static Frame LocalView(Frame mainPart,bool verticalColumn,string viewType,Angles angles)
        {
            if(mainPart==null||angles==null)throw new InvalidOperationException("Chưa đọc được hệ Local của cấu kiện.");
            Func<V,V> rotate=v=>mainPart.ToWorld(Rx(Ry(Rz(v,-angles.Z),-angles.Y),-angles.X));
            V x=rotate(new V(1,0,0)),y=rotate(new V(0,1,0)),z=rotate(new V(0,0,1));
            Frame f;
            switch(viewType)
            {
                case "FrontView":f=new Frame(x,y);break;
                case "TopView":f=new Frame(x,z*(-1));break;
                case "BackView":f=new Frame(x,y*(-1));break;
                case "BottomView":f=new Frame(x,z);break;
                default:throw new InvalidOperationException("Không có quy tắc quay đã kiểm chứng cho view "+viewType+".");
            }
            if(!verticalColumn)return f;
            return mainPart.Right.Z<0?new Frame(f.Up,f.Right*(-1)):new Frame(f.Up*(-1),f.Right);
        }
        public static bool IsStandardView(string type)
        {return type=="FrontView"||type=="TopView"||type=="BackView"||type=="BottomView";}
        public static RotationSearch SearchLocal(Frame mainPart,bool verticalColumn,string[] viewTypes,Frame[] target)
        { return Search(mainPart,verticalColumn,viewTypes,target,false); }
        public static RotationSearch SearchKeyplan(Frame mainPart,bool verticalColumn,string[] viewTypes,Frame[] target)
        { return Search(mainPart,verticalColumn,viewTypes,target,true); }
        static RotationSearch Search(Frame mainPart,bool verticalColumn,string[] viewTypes,Frame[] target,bool projection)
        {
            if(mainPart==null||viewTypes==null||target==null||viewTypes.Length!=2||target.Length!=2||target.Any(f=>f==null)
                ||viewTypes.Distinct().Count()!=2||viewTypes.Any(t=>!IsStandardView(t)))
                throw new InvalidOperationException("Cần hệ Local và hai loại view tiêu chuẩn xác định duy nhất.");
            var evaluated=new List<Candidate>();
            // Giữ 32 ứng viên cũ để kết quả góc vuông ổn định; bổ sung nghiệm Z giải tích, không quét theo bước.
            var candidates=AllowedAngles().Concat(SolveZCandidates(mainPart,verticalColumn,viewTypes,target))
                .GroupBy(a=>a.ToString()).Select(g=>g.First());
            foreach(var a in candidates)
            {
                double error=Enumerable.Range(0,2).Max(i=>projection?KeyplanError(LocalView(mainPart,verticalColumn,viewTypes[i],a),target[i]):Error(LocalView(mainPart,verticalColumn,viewTypes[i],a),target[i]));
                if(!Finite(error))throw new InvalidOperationException("Không xác định được sai lệch góc.");
                evaluated.Add(new Candidate { Angles=a,Error=error });
            }
            var matches=evaluated.Where(c=>c.Error<=ToleranceDegrees)
                .OrderBy(c=>(c.Angles.X==0?0:1)+(c.Angles.Y==0?0:1)+(c.Angles.Z==0?0:1))
                .ThenBy(c=>c.Angles.X).ThenBy(c=>c.Angles.Y).ThenBy(c=>c.Angles.Z).ToList();
            return new RotationSearch(evaluated,matches);
        }
        static IEnumerable<Angles> SolveZCandidates(Frame mainPart,bool verticalColumn,string[] viewTypes,Frame[] target)
        {
            foreach(int x in new[]{0,90,180,270})foreach(int y in new[]{0,180})
            {
                var roots=new List<double>();
                for(int i=0;i<2;i++)
                {
                    var zero=LocalView(mainPart,verticalColumn,viewTypes[i],new Angles(x,y,0));
                    var quarter=LocalView(mainPart,verticalColumn,viewTypes[i],new Angles(x,y,90));
                    var half=LocalView(mainPart,verticalColumn,viewTypes[i],new Angles(x,y,180));
                    // Trục keyplan phải chiếu ngang/dọc trên giấy: hai tích vô hướng chéo bằng 0.
                    AddSinusoidRoots(roots,V.Dot(target[i].Right,zero.Up),V.Dot(target[i].Right,quarter.Up),V.Dot(target[i].Right,half.Up));
                    AddSinusoidRoots(roots,V.Dot(target[i].Up,zero.Right),V.Dot(target[i].Up,quarter.Right),V.Dot(target[i].Up,half.Right));
                    // Cặp nhìn đối diện có thể cùng giữ chiều chiếu khi Z đổi; thêm ràng buộc pháp tuyến cho SearchLocal.
                    AddSinusoidRoots(roots,V.Dot(target[i].Normal,zero.Right),V.Dot(target[i].Normal,quarter.Right),V.Dot(target[i].Normal,half.Right));
                    AddSinusoidRoots(roots,V.Dot(target[i].Normal,zero.Up),V.Dot(target[i].Normal,quarter.Up),V.Dot(target[i].Normal,half.Up));
                }
                foreach(double z in roots.Select(value=>Wrap(Math.Round(value,ZInputDecimals,MidpointRounding.AwayFromZero))).Distinct())
                    yield return new Angles(x,y,z); // Sau làm tròn vẫn bắt buộc qua kiểm toàn bộ hai frame ở Search.
            }
        }
        static void AddSinusoidRoots(List<double> roots,double zero,double quarter,double half)
        {
            // f(Z)=a*cos(Z)+b*sin(Z)+c; không phụ thuộc tên/ID cấu kiện hoặc trục global cố định.
            double c=(zero+half)*0.5,a=(zero-half)*0.5,b=quarter-c,r=Math.Sqrt(a*a+b*b);
            if(r<1e-12||Math.Abs(c)>r+1e-12)return;
            double phase=Math.Atan2(b,a),delta=Math.Acos(Math.Max(-1,Math.Min(1,-c/r)));
            roots.Add(Wrap((phase+delta)*180/Math.PI));roots.Add(Wrap((phase-delta)*180/Math.PI));
        }
        public static Frame[] Targets(bool column,bool alongY,Frame grid)
        {
            V x=grid.Right,y=grid.Up,z=grid.Normal;
            if(Angle(z,new V(0,0,1))>ToleranceDegrees)throw new InvalidOperationException("Keyplan phải có pháp tuyến hướng lên +Z model.");
            if(column)return new[]{new Frame(x,z),new Frame(y,z)};
            return alongY?new[]{new Frame(y,z),new Frame(y,x*(-1))}:new[]{new Frame(x,z),new Frame(x,y)};
        }
        public static List<GridTick> ParseGrid(string coordinates,string labels,bool absolute)
        {
            var numbers=new List<double>(); double previous=0;
            foreach(string token in (coordinates??"").Split((char[])null,StringSplitOptions.RemoveEmptyEntries))
            {
                var bits=token.Split('*'); int count=1;double value;
                if(bits.Length>2 || (bits.Length==2&&(!int.TryParse(bits[0],out count)||count<1||count>10000))
                    || !double.TryParse(bits[bits.Length-1],NumberStyles.Float,CultureInfo.InvariantCulture,out value)
                    || double.IsNaN(value)||double.IsInfinity(value))throw new InvalidOperationException("Không đọc được tọa độ grid: "+token);
                for(int i=0;i<count;i++)
                {
                    previous=absolute||numbers.Count==0?value:previous+value;
                    if(!Finite(previous)||numbers.Count>=10000)throw new InvalidOperationException("Tọa độ grid vượt giới hạn đọc.");
                    numbers.Add(previous);
                }
            }
            var names=(labels??"").Split((char[])null,StringSplitOptions.RemoveEmptyEntries);
            if(numbers.Count!=names.Length||numbers.Count<2)throw new InvalidOperationException("Grid cần ít nhất hai tọa độ có nhãn khớp nhau.");
            var ticks=numbers.Select((p,i)=>new GridTick { Position=p,Label=names[i] }).OrderBy(t=>t.Position).ToList();
            if(ticks.Select(t=>t.Label).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=ticks.Count || ticks.Zip(ticks.Skip(1),(a,b)=>b.Position-a.Position).Any(d=>d<1e-6))
                throw new InvalidOperationException("Grid có nhãn hoặc vị trí trùng; chưa đủ bằng chứng xác định thứ tự.");
            return ticks;
        }
    }
}
#endregion

#region Nhận loại cấu kiện
namespace TTSK_AutoDim_Plates.Keyplan
{


using System;
using System.Collections.Generic;
using System.Linq;
using D = Tekla.Structures.Drawing;
using M = Tekla.Structures.Model;
using G = Tekla.Structures.Geometry3d;
using Tekla.Technology.Akit.UserScript;

    public sealed class ShapeReference
    {
        public V Center, Axis, Minimum, Maximum;
        public bool UniqueAxis;
        public int VertexCount;
        public List<V> Vertices;
    }
    // Geometry recognition is independent of the coordinate system used to
    // rotate drawing properties. No Model API writes or work-plane changes.
    public static class KeyplanShape
    {
        public static ShapeReference Measure(IEnumerable<V> source)
        {
            var points=source.Distinct().ToList();
            if(points.Count<4)throw new InvalidOperationException("Solid chưa đủ điểm 3D để nhận dạng hướng.");
            foreach(var p in points)if(!Geometry.Finite(p.X)||!Geometry.Finite(p.Y)||!Geometry.Finite(p.Z))throw new InvalidOperationException("Solid chứa tọa độ không hợp lệ.");
            var min=new V(points.Min(p=>p.X),points.Min(p=>p.Y),points.Min(p=>p.Z));
            var max=new V(points.Max(p=>p.X),points.Max(p=>p.Y),points.Max(p=>p.Z));
            var center=(min+max)*0.5;
            double scale=Math.Max(max.X-min.X,Math.Max(max.Y-min.Y,max.Z-min.Z));
            if(scale<1e-6)throw new InvalidOperationException("Solid không có kích thước.");
            var normalized=points.Select(p=>(p-center)*(1/scale)).ToList();
            V mean=new V();foreach(var p in normalized)mean+=p*(1.0/points.Count);
            var a=new double[3,3];var v=new double[3,3];for(int i=0;i<3;i++)v[i,i]=1;
            foreach(var p in normalized){var d=p-mean;double[] x={d.X,d.Y,d.Z};for(int i=0;i<3;i++)for(int j=0;j<3;j++)a[i,j]+=x[i]*x[j]/points.Count;}
            // Jacobi eigenvectors of the symmetric covariance matrix.
            for(int iteration=0;iteration<50;iteration++)
            {
                int p=0,q=1;double largest=Math.Abs(a[0,1]);
                for(int i=0;i<3;i++)for(int j=i+1;j<3;j++)if(Math.Abs(a[i,j])>largest){largest=Math.Abs(a[i,j]);p=i;q=j;}
                if(largest<1e-14)break;
                double angle=0.5*Math.Atan2(2*a[p,q],a[q,q]-a[p,p]),c=Math.Cos(angle),s=Math.Sin(angle);
                double pp=a[p,p],qq=a[q,q],pq=a[p,q];
                a[p,p]=c*c*pp-2*s*c*pq+s*s*qq;a[q,q]=s*s*pp+2*s*c*pq+c*c*qq;a[p,q]=a[q,p]=0;
                for(int k=0;k<3;k++)
                {
                    if(k!=p&&k!=q){double kp=a[k,p],kq=a[k,q];a[k,p]=a[p,k]=c*kp-s*kq;a[k,q]=a[q,k]=s*kp+c*kq;}
                    double vp=v[k,p],vq=v[k,q];v[k,p]=c*vp-s*vq;v[k,q]=s*vp+c*vq;
                }
            }
            var order=Enumerable.Range(0,3).OrderByDescending(i=>a[i,i]).ToArray();int first=order[0];
            return new ShapeReference {Center=center,Minimum=min,Maximum=max,Axis=new V(v[0,first],v[1,first],v[2,first]).Unit(),
                UniqueAxis=a[first,first]>a[order[1],order[1]]*1.01+1e-12,VertexCount=points.Count,Vertices=points};
        }
        internal static ShapeReference Read(M.Part part,G.Matrix global)
        {
            var points=new List<V>();var solid=part.GetSolid();
            if(solid!=null)
            {
                var faces=solid.GetFaceEnumerator();
                while(faces.MoveNext())
                {
                    var face=faces.Current as Tekla.Structures.Solid.Face;if(face==null)continue;
                    var loops=face.GetLoopEnumerator();while(loops.MoveNext())
                    {
                        var loop=loops.Current as Tekla.Structures.Solid.Loop;if(loop==null)continue;
                        var vertices=loop.GetVertexEnumerator();while(vertices.MoveNext())
                        {var p=vertices.Current as G.Point;if(p!=null){p=global.Transform(p);points.Add(new V(p.X,p.Y,p.Z));}}
                    }
                }
            }
            if(points.Count>=4)return Measure(points);
            // Some custom/formed parts expose a reference line while their
            // presentation solid is unavailable. This still gives a real 3D
            // direction, never a guessed global axis.
            var line=part.GetCenterLine(true);
            if(line!=null)
            {
                var centerline=new List<V>();foreach(var item in line)
                {var p=item as G.Point;if(p!=null){p=global.Transform(p);centerline.Add(new V(p.X,p.Y,p.Z));}}
                if(centerline.Count>=2)
                {
                    var first=centerline.First();var last=centerline.Last();var axis=(last-first).Unit();
                    if(centerline.Count>2)
                    {
                        var measured=Measure(centerline);if(measured.UniqueAxis)return measured;
                    }
                    return new ShapeReference {Center=(first+last)*0.5,Axis=axis,Minimum=new V(Math.Min(first.X,last.X),Math.Min(first.Y,last.Y),Math.Min(first.Z,last.Z)),
                        Maximum=new V(Math.Max(first.X,last.X),Math.Max(first.Y,last.Y),Math.Max(first.Z,last.Z)),UniqueAxis=true,VertexCount=centerline.Count,Vertices=centerline};
                }
            }
            throw new InvalidOperationException("Không đọc được solid hoặc centerline 3D của main part.");
        }
        internal static List<M.Part> Resolve(M.Model model,D.Drawing drawing,out bool authoritative)
        {
            authoritative=false;
            var direct=PHU_MainPartResolver.Resolve(model,drawing);
            if(direct!=null){authoritative=true;return new List<M.Part>{direct};}
            // If the authoritative main-part link is missing, inspect visible
            // references without pretending the first/largest is the main part.
            var unique=new Dictionary<int,M.Part>();var views=drawing.GetSheet().GetAllViews();
            while(views.MoveNext())
            {
                var view=views.Current as D.View;if(view==null)continue;
                var parts=view.GetAllObjects(typeof(D.Part));while(parts.MoveNext())
                {var dp=parts.Current as D.Part;if(dp==null||unique.ContainsKey(dp.ModelIdentifier.ID))continue;var p=model.SelectModelObject(dp.ModelIdentifier) as M.Part;if(p!=null)unique.Add(p.Identifier.ID,p);}
            }
            return unique.Values.OrderBy(p=>p.Identifier.ID).ToList();
        }
    }
}
#endregion

#region Đọc model và các view
namespace TTSK_AutoDim_Plates.Keyplan
{


using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using D = Tekla.Structures.Drawing;
using M = Tekla.Structures.Model;
using G = Tekla.Structures.Geometry3d;
using Tekla.Technology.Akit.UserScript;

    public sealed class AxisReference
    {
        public string Family, Label;
        public double Position;
        public V Direction;
        public override string ToString() { return Family+" · "+Label+" ["+Position.ToString("0.###",CultureInfo.InvariantCulture)+"]"; }
    }
    public sealed class GridReference
    {
        public int Id;
        public string Name, Error;
        public List<AxisReference> Axes=new List<AxisReference>();
        public List<GridTick> Floors=new List<GridTick>();
        public V Origin;
        public override string ToString() { return Name+" · ID "+Id+(Error==null?"":" · cần kiểm tra"); }
    }
    public sealed class ViewReference
    {
        public int Id;
        public string Name, Type;
        public Frame Frame;
        public double SheetX, SheetY;
        public bool Unsupported;
        public string ReadError;
        public override string ToString() { return Type+" "+Name+" · ID "+Id; }
    }
    public sealed class Snapshot
    {
        public string Identity, Mark, ModelPath, MainPart;
        public string DrawingKind, ShapeNote;
        public bool GenericMember, Overview;
        public ShapeReference Shape;
        public V MainOrigin;
        // Beam reference endpoints in GLOBAL coordinates, independent of drawing rotation.
        public V? MainStart, MainEnd;
        public Frame MainFrame;
        public bool VerticalColumn;
        public string RotationSupportError;
        public DrawingSettings Settings;
        public List<GridReference> Grids=new List<GridReference>();
        public List<ViewReference> Views=new List<ViewReference>();
        // Views giữ đúng phạm vi solver cũ; AllViews chỉ dùng báo cáo mọi view trên sheet.
        public List<ViewReference> AllViews=new List<ViewReference>();
        internal Snapshot WithViews(List<ViewReference> views) {var copy=(Snapshot)MemberwiseClone();copy.Views=views;return copy;}
    }
    public sealed class QuickResult
    {
        public Angles Angles;
        public string Message, Reference;
        public bool DirectionsMatch, LayoutMatches;
        public QuickState State;
        public int TestedCount, MathematicalMatchCount;
        public int GridId;
        public bool AlongY;
        public bool IsColumn;
        public string SelectionNote;
        public string ReferenceFirst, ReferenceSecond, ActionNote;
        public string BriefNote, ReferenceFirstCaption, ReferenceSecondCaption;
        // The exact view roles used to solve the tuple. UI and post-apply
        // verification consume these roles instead of inventing Front/Top.
        public string FirstViewType, SecondViewType;
        public Frame FirstTarget, SecondTarget;
        public bool CompleteViews=true, CanApply=true;
        public List<ViewAxisCheck> ViewChecks=new List<ViewAxisCheck>();
        internal Frame ViewCheckKey;
        internal QuickResult Copy() {return (QuickResult)MemberwiseClone();}
    }
    public enum ViewAxisState { Correct, Misaligned, Unverified }
    public sealed class ViewAxisCheck
    {
        public int Id;
        public string Name, Type, Message, Axes;
        public ViewAxisState State;
        public double? PaperRotation;
        public string Label {get {return Type.Replace("View","")+(string.IsNullOrWhiteSpace(Name)?"":" · "+Name)+" (#"+Id+")";}}
    }
    public enum QuickState { Correct, LayoutMismatch, MissingData, NoSolution, VerifiedSuggestion, IncompleteViews, Overview }
    public static class Reader
    {
        public static QuickResult VerifyApplied(Snapshot before,QuickResult plan,Snapshot after,Angles angles)
        {
            if(before==null||plan==null||after==null||before.Identity!=after.Identity||!KeyplanApply.Same(after.Settings,angles))
                throw new InvalidOperationException("Không xác nhận được bản vẽ và bộ góc đã lưu sau Modify.");
            if(before.MainFrame==null||after.MainFrame==null||Geometry.Error(before.MainFrame,after.MainFrame)>Geometry.ToleranceDegrees
                ||before.VerticalColumn!=after.VerticalColumn||after.RotationSupportError!=null)
                throw new InvalidOperationException("Hệ Local của cấu kiện đã thay đổi trong lúc áp dụng.");
            var oldGrid=before.Grids.SingleOrDefault(g=>g.Id==plan.GridId);var newGrid=after.Grids.SingleOrDefault(g=>g.Id==plan.GridId);
            if(oldGrid==null||newGrid==null||!ContainsGrid(oldGrid,newGrid)||!ContainsGrid(newGrid,oldGrid))
                throw new InvalidOperationException("Lưới keyplan đã thay đổi trong lúc áp dụng.");
            if(plan.FirstViewType==null||plan.SecondViewType==null||plan.FirstTarget==null||plan.SecondTarget==null)
                throw new InvalidOperationException("Thiếu ánh xạ view dùng để kiểm chứng bộ góc.");
            foreach(var view in after.Views)
            {
                Frame target=view.Type==plan.FirstViewType?plan.FirstTarget:view.Type==plan.SecondViewType?plan.SecondTarget:null;
                if(target==null||view.Unsupported)
                    throw new InvalidOperationException("Tekla tự sinh "+view.Type.Replace("View","")+" ngoài cặp dự kiến "+plan.FirstViewType.Replace("View","")+" / "+plan.SecondViewType.Replace("View","")+". Đã lưu góc nhưng chưa xác nhận view đúng grid.");
                if(Geometry.Error(view.Frame,Geometry.LocalView(before.MainFrame,before.VerticalColumn,view.Type,angles))>Geometry.ToleranceDegrees
                    ||Geometry.KeyplanError(view.Frame,target)>Geometry.ToleranceDegrees)
                    throw new InvalidOperationException("Sau Modify, view "+view.Type.Replace("View","")+" không khớp hướng grid mục tiêu. Không báo đạt hoặc tự thử lại góc khác.");
            }
            var first=after.Views.Where(v=>v.Type==plan.FirstViewType).ToList();var second=after.Views.Where(v=>v.Type==plan.SecondViewType).ToList();
            if(first.Count>1||second.Count>1)throw new InvalidOperationException("Tekla sinh view trùng loại; chưa xác nhận được cặp trái/phải.");
            if(plan.CompleteViews&&(first.Count!=1||second.Count!=1))throw new InvalidOperationException("Tekla không sinh đủ cặp view dự kiến sau Modify.");
            var result=plan.Copy();result.Angles=null;result.DirectionsMatch=after.Views.Count>0;result.CompleteViews=first.Count==1&&second.Count==1;
            result.LayoutMatches=result.CompleteViews&&(plan.IsColumn?first[0].SheetX+0.1<second[0].SheetX:second[0].SheetY>first[0].SheetY+0.1);
            result.State=result.CompleteViews?(result.LayoutMatches?QuickState.Correct:QuickState.LayoutMismatch):QuickState.IncompleteViews;
            result.Message=!result.CompleteViews?"Đã lưu góc; thiếu view để xác nhận đầy đủ keyplan.":result.LayoutMatches?"Đúng trục theo keyplan":
                "Hướng grid đúng; cần đặt "+plan.FirstViewType.Replace("View","")+(plan.IsColumn?" bên trái, ":" ở dưới, ")+plan.SecondViewType.Replace("View","")+(plan.IsColumn?" bên phải.":" ở trên.");
            result.ActionNote=result.State==QuickState.Correct?"":result.Message;
            if(!result.CompleteViews)result.Angles=angles;
            return AttachViewChecks(after,result,0,plan.ViewCheckKey);
        }
        // User overrides are independent. Never choose a mode just because it has
        // an easy angle solution or because the current (possibly wrong) views fit it.
        public static QuickResult Analyze(Snapshot s,bool? columnOverride=null,bool? alongYOverride=null,int gridId=0,int keyQuarterTurns=0)
        {
            return AttachViewChecks(s,AnalyzeMainViews(s,columnOverride,alongYOverride,gridId,keyQuarterTurns),keyQuarterTurns);
        }
        static QuickResult AnalyzeMainViews(Snapshot s,bool? columnOverride,bool? alongYOverride,int gridId,int keyQuarterTurns)
        {
            if(s==null)throw new InvalidOperationException("Chưa có dữ liệu bản vẽ.");
            if(s.Overview)return AnalyzeOverview(s,gridId,keyQuarterTurns);
            bool column=columnOverride??RecognizeColumn(s);
            Snapshot analysis=s;
            bool stacked=s.GenericMember&&column&&s.Views.Count==2&&s.Views.Any(v=>v.Type=="FrontView")&&s.Views.Any(v=>v.Type=="TopView")
                &&Math.Abs(s.Views[0].SheetX-s.Views[1].SheetX)<=0.1;
            if(stacked)analysis=s.WithViews(s.Views.OrderBy(v=>v.Type).Select((v,i)=>new ViewReference {Id=v.Id,Name=v.Name,Type=v.Type,Frame=v.Frame,
                Unsupported=v.Unsupported,SheetX=i*100,SheetY=v.SheetY}).ToList());
            var result=QuickAnalyze(analysis,column,alongYOverride??false,gridId,keyQuarterTurns,!alongYOverride.HasValue);
            if(column&&result.State==QuickState.NoSolution&&s.Views.Count==2&&s.Views.Select(v=>v.Type).Distinct().Count()==2)
            {
                // Thứ tự view trên giấy có thể ngược cặp có nghiệm; xét cả hai ánh xạ, báo cần sắp lại.
                var ordered=analysis.Views.OrderBy(v=>v.SheetX).ToList();
                var swapped=s.WithViews(ordered.Select((v,i)=>new ViewReference {Id=v.Id,Name=v.Name,Type=v.Type,Frame=v.Frame,
                    Unsupported=v.Unsupported,SheetX=i==0?100:0,SheetY=v.SheetY}).ToList());
                var alternative=QuickAnalyze(swapped,column,false,result.GridId,keyQuarterTurns);
                if(alternative.State!=QuickState.NoSolution&&alternative.State!=QuickState.MissingData)
                {
                    alternative.SelectionNote=result.SelectionNote;result=alternative;result.LayoutMatches=false;
                    if(result.State==QuickState.Correct)result.State=QuickState.LayoutMismatch;
                    string note="Đặt "+ordered[1].Type.Replace("View","")+" bên trái (X), "+ordered[0].Type.Replace("View","")+" bên phải (Y).";
                    result.ActionNote=(result.ActionNote??"")+" "+note;result.Message=result.State==QuickState.LayoutMismatch?"Góc Local đúng. "+note:result.Message+"\n"+note;
                }
            }
            if(stacked)
            {
                result.LayoutMatches=false;if(result.State==QuickState.Correct)result.State=QuickState.LayoutMismatch;
                result.ActionNote=(result.ActionNote??"")+" Hai view đang cùng vị trí ngang; cần đặt view X trái, Y phải.";
                result.Message=result.State==QuickState.LayoutMismatch?"Góc Local đúng. "+result.ActionNote:result.Message+"\n"+result.ActionNote;
            }
            if(s.GenericMember&&s.Settings!=null&&s.Settings.IsLocal&&s.Settings.AllowedAngles!=null&&s.Views.Any(v=>
                Geometry.IsStandardView(v.Type)&&!v.Unsupported&&Geometry.Error(v.Frame,Geometry.LocalView(s.MainFrame,s.VerticalColumn,v.Type,s.Settings.AllowedAngles))>Geometry.ToleranceDegrees))
            {
                result.State=QuickState.MissingData;result.CanApply=false;result.Angles=null;
                result.Message="View thực tế không khớp quy tắc Local/X/Y/Z của cấu kiện này. Cần kiểm tra phép đặt hướng hoặc view đã xoay riêng; không tự áp dụng lại.";
            }
            if(s.GenericMember&&(result.State==QuickState.NoSolution||!result.CanApply)&&s.Views.Count>0&&result.State!=QuickState.MissingData)
            {
                var perView=AnalyzeOverview(s,result.GridId,keyQuarterTurns);
                perView.IsColumn=column;perView.AlongY=result.AlongY;
                perView.Message="Không có bộ góc Local chung đã xác minh cho toàn bộ view hiện có.\n"+perView.Message;
                perView.ActionNote=perView.Message;return perView;
            }
            result.SelectionNote=(columnOverride.HasValue?"Phương án chọn tay: ":"Tự nhận theo trục 3D: ")+
                (column?"Cột":"Dầm")+" · "+result.SelectionNote+(s.ShapeNote==null?"":" · "+s.ShapeNote);
            return result;
        }
        static QuickResult AttachViewChecks(Snapshot s,QuickResult result,int turns=0,Frame checkedKey=null)
        {
            // Kiểm độc lập; tuyệt đối không đổi Angles/State/CanApply hoặc danh sách Views của solver.
            var grid=s.Grids.Single(g=>g.Id==result.GridId);
            var key=checkedKey??new Frame(grid.Axes.First(a=>a.Family=="Grid X").Direction,grid.Axes.First(a=>a.Family=="Grid Y").Direction).Spin(turns*90);
            result.ViewCheckKey=key;
            var views=s.AllViews.Count>0?s.AllViews:s.Views;
            result.ViewChecks=views.OrderByDescending(v=>v.SheetY).ThenBy(v=>v.SheetX).ThenBy(v=>v.Id)
                .Select(view=>CheckViewAxes(view,key,!result.IsColumn&&result.AlongY)).ToList();
            return result;
        }
        public static ViewAxisCheck CheckViewAxes(ViewReference view,Frame key,bool alongY)
        {return CheckViewAxesCore(view,key,alongY,true);}
        // Combo chỉ kiểm hệ trục hiện có; không đọc Drawing properties, không tìm/gợi ý góc.
        public static List<ViewAxisCheck> CheckCurrentViews(Snapshot snapshot)
        {
            if(snapshot==null)throw new InvalidOperationException("Chưa đọc được hình học bản vẽ.");
            string note;var grid=SelectGrid(snapshot,0,out note);
            var key=new Frame(grid.Axes.First(a=>a.Family=="Grid X").Direction,grid.Axes.First(a=>a.Family=="Grid Y").Direction);
            bool alongY=!snapshot.Overview&&!RecognizeColumn(snapshot)&&BeamAlongY(snapshot,key);
            var views=snapshot.AllViews.Count>0?snapshot.AllViews:snapshot.Views;
            if(views.Count==0)throw new InvalidOperationException("Không có view để kiểm tra.");
            return views.OrderByDescending(v=>v.SheetY).ThenBy(v=>v.SheetX).ThenBy(v=>v.Id)
                .Select(view=>CheckViewAxesCore(view,key,alongY,false)).ToList();
        }
        static ViewAxisCheck CheckViewAxesCore(ViewReference view,Frame key,bool alongY,bool suggestRotation)
        {
            var check=new ViewAxisCheck {Id=view.Id,Name=view.Name,Type=view.Type??"View",State=ViewAxisState.Unverified};
            if(view.ReadError!=null||view.Frame==null)
            {check.Message=view.ReadError??"Chưa đọc được hệ trục view.";return check;}
            check.Axes="Phải: "+AxisDirection(view.Frame.Right,key)+" · Lên: "+AxisDirection(view.Frame.Up,key);
            if(view.Unsupported)
            {check.Message="Đã đọc hệ trục; view phản chiếu/khai triển/undeformed chưa có quy tắc xác nhận.";return check;}
            // Dùng cùng chuẩn mặt bằng với solver: hướng X là (+X,+Y), hướng Y là (+Y,−X).
            // Trục ngang của section đi theo hướng chính đã chọn, không theo dấu normal đang kiểm.
            Frame plan=Geometry.Targets(false,alongY,key)[1];
            V normal=view.Frame.Normal;
            double nx=V.Dot(normal,plan.Right),ny=V.Dot(normal,plan.Up),nz=V.Dot(normal,plan.Normal);
            double[] facing={Math.Abs(nx),Math.Abs(ny),Math.Abs(nz)};
            var order=Enumerable.Range(0,3).OrderByDescending(i=>facing[i]).ToArray();
            if(facing[order[0]]<=facing[order[1]]+1e-8)
            {check.Message="Hướng chiếu xiên giữa hai phương; chưa có chuẩn keyplan duy nhất.";return check;}
            Frame target;
            // Pháp tuyến chỉ chọn họ mặt chiếu trong chuẩn trên; không đảo chuẩn để chấp nhận view ngược.
            if(order[0]==0)target=new Frame(plan.Up,plan.Normal);
            else if(order[0]==1)target=new Frame(plan.Right,plan.Normal);
            else target=plan;
            if(Geometry.KeyplanError(view.Frame,target)<=Geometry.ToleranceDegrees)
            {check.State=ViewAxisState.Correct;check.Message="Đúng chiều trục theo keyplan.";return check;}
            check.State=ViewAxisState.Misaligned;
            if(!suggestRotation)
            {
                check.Message=V.Dot(normal,target.Normal)<0?"Chiều nhìn ngược keyplan.":"Chiều trục view chưa đúng keyplan.";
                return check;
            }
            if(V.Dot(normal,target.Normal)<0)
            {
                check.Message="Chiều nhìn ngược keyplan; cần đổi phía nhìn. Chuẩn phải: "+AxisDirection(target.Right,key)+" · lên: "+AxisDirection(target.Up,key)+".";
                return check; // Quay trên giấy không đảo được pháp tuyến; không đưa góc sửa sai.
            }
            double angle=Math.Atan2(V.Dot(target.Right,view.Frame.Up),V.Dot(target.Right,view.Frame.Right))*180/Math.PI;
            angle=Math.Round(angle,3,MidpointRounding.AwayFromZero);
            if(Geometry.KeyplanError(view.Frame.Spin(angle),target)<=Geometry.ToleranceDegrees)
            {check.PaperRotation=angle;check.Message="Chưa đúng chiều trục · xoay riêng view trên giấy "+angle.ToString("0.###",CultureInfo.InvariantCulture)+"°.";}
            else check.Message="Hướng chiếu xiên không khớp đồng thời hai trục keyplan; cần kiểm tra riêng view.";
            return check;
        }
        static string AxisDirection(V direction,Frame key)
        {
            var axes=new[]{key.Right,key.Up,key.Normal};var names=new[]{"X lưới","Y lưới","Z model"};
            for(int i=0;i<axes.Length;i++)foreach(int sign in new[]{1,-1})
                if(Geometry.Angle(direction,axes[i]*sign)<=Geometry.ToleranceDegrees)return (sign>0?"+":"−")+names[i];
            return direction+" global";
        }
        static QuickResult AnalyzeOverview(Snapshot s,int gridId,int turns)
        {
            if(turns<0||turns>3)throw new InvalidOperationException("Chuẩn keyplan chỉ xoay 0/90/180/270°.");
            string note;var grid=SelectGrid(s,gridId,out note);
            var x=grid.Axes.First(a=>a.Family=="Grid X").Direction;var y=grid.Axes.First(a=>a.Family=="Grid Y").Direction;
            var key=new Frame(x,y).Spin(turns*90);
            var bx=Geometry.Targets(false,false,key);var by=Geometry.Targets(false,true,key);
            var names=new[]{"Mặt đứng X","Mặt bằng X","Mặt đứng Y","Mặt bằng Y","Mặt đứng X · phía đối diện","Mặt đứng Y · phía đối diện","Mặt dưới X","Mặt dưới Y"};
            var targets=new[]{bx[0],bx[1],by[0],by[1],new Frame(key.Right*(-1),key.Normal),new Frame(key.Up*(-1),key.Normal),
                new Frame(key.Right,key.Up*(-1)),new Frame(key.Up,key.Right)};
            int matched=0;var lines=new List<string>();var brief=new List<string>();Frame singleTarget=null;
            foreach(var view in s.Views)
            {
                var candidates=(from angle in new[]{0,90,-90,180} from i in Enumerable.Range(0,targets.Length)
                    where Geometry.KeyplanError(view.Frame.Spin(angle),targets[i])<=Geometry.ToleranceDegrees select new {Index=i,Angle=angle}).ToList();
                if(!view.Unsupported&&candidates.Count>0)
                {
                    var best=candidates[0];if(best.Angle==0)matched++;
                    if(s.Views.Count==1)singleTarget=targets[best.Index];
                    brief.Add(view.Type.Replace("View","")+(s.Views.Count(v=>v.Type==view.Type)>1?" #"+view.Id:"")+(best.Angle==0?": đúng trục.":": xoay trên giấy "+best.Angle+"°."));
                    lines.Add("View "+view.Id+" ("+view.Type.Replace("View","")+"): "+names[best.Index]+(best.Angle==0?" · đúng chiều trục":" · xoay trên giấy "+best.Angle+"°"));
                }
                else {lines.Add("View "+view.Id+": hướng chiếu xiên hoặc biến dạng; không có phép xoay trên giấy 0/±90/180° khớp keyplan");brief.Add(view.Type.Replace("View","")+": cần kiểm tra hướng chiếu.");}
            }
            string message="Đã kiểm "+s.Views.Count+" view; "+matched+" view khớp keyplan.\n"+string.Join("\n",lines)+"\nCác góc trên là xoay riêng trên giấy, không phải Around X/Y/Z của Drawing properties.";
            var result=new QuickResult {ViewCheckKey=key,State=QuickState.Overview,CanApply=false,GridId=grid.Id,CompleteViews=false,DirectionsMatch=s.Views.Count>0&&matched==s.Views.Count,
                BriefNote=(brief.Count<=3?string.Join("\n",brief):matched+"/"+s.Views.Count+" view đúng trục. Xem chi tiết khi rê chuột.")+"\nChưa có góc Local chung.",
                ReferenceFirstCaption="KEYPLAN · X",ReferenceSecondCaption="KEYPLAN · Y",
                Message=message,ActionNote=message,SelectionNote=note,Reference="Keyplan · "+grid.Name,ReferenceFirst=AxisLabels(s,grid,key.Right,false),ReferenceSecond=AxisLabels(s,grid,key.Up,false)};
            if(singleTarget!=null&&Math.Abs(singleTarget.Up.Z)<1e-6)
            {
                string view=s.Views[0].Type.Replace("View","").ToUpperInvariant();
                result.ReferenceFirstCaption=view+" · TRỤC ĐỨNG";result.ReferenceSecondCaption=view+" · TRỤC NGANG";
                result.ReferenceFirst=AxisLabels(s,grid,singleTarget.Right,false);result.ReferenceSecond=AxisLabels(s,grid,singleTarget.Up,false);
            }
            return result;
        }
        static V MemberAxis(Snapshot s)
        {
            if(s.GenericMember&&s.Shape!=null)
            {
                if(!s.Shape.UniqueAxis)throw new InvalidOperationException("Hình học có nhiều phương dài tương đương. Chọn phương án và hướng X/Y để đối chiếu; không tự đoán một trục.");
                return s.Shape.Axis;
            }
            if(s.MainStart.HasValue!=s.MainEnd.HasValue)throw new InvalidOperationException("Thiếu một đầu cấu kiện; chưa thể tự nhận phương án.");
            if(s.MainStart.HasValue)
            {
                V axis=(s.MainEnd.Value-s.MainStart.Value).Unit();
                if(s.MainFrame!=null&&Math.Min(Geometry.Angle(axis,s.MainFrame.Right),Geometry.Angle(axis,s.MainFrame.Right*(-1)))>Geometry.ToleranceDegrees)
                    throw new InvalidOperationException("Trục hai đầu không khớp hệ Local; cần kiểm tra lại hình học cấu kiện.");
                return axis;
            }
            if(s.MainFrame==null)throw new InvalidOperationException("Chưa đọc được trục dọc cấu kiện.");
            return s.MainFrame.Right;
        }
        public static bool RecognizeColumn(Snapshot s)
        {
            V axis=MemberAxis(s);
            double horizontal=Math.Sqrt(axis.X*axis.X+axis.Y*axis.Y),vertical=Math.Abs(axis.Z);
            if(Math.Abs(vertical-horizontal)<=1e-6)
                throw new InvalidOperationException("Trục cấu kiện nghiêng 45° giữa đứng và ngang. Hãy chọn phương án Dầm/Cột.");
            // This selects a keyplan convention, not a Tekla part type or the
            // vertical-placing option. The exact angle solver still has to pass.
            return vertical>horizontal;
        }
        // A tick requires the actual drawing's original Local settings AND its view geometry.
        // Absolute recommendations are calculated independently from the old settings.
        public static QuickResult QuickAnalyze(Snapshot s,bool column,bool alongY,int gridId=0,int keyQuarterTurns=0,bool autoDirection=false)
        {
            if(s==null)throw new InvalidOperationException("Chưa có dữ liệu bản vẽ.");
            if(keyQuarterTurns<0||keyQuarterTurns>3)throw new InvalidOperationException("Chuẩn keyplan chỉ xoay 0/90/180/270°.");
            string selectionNote;
            GridReference grid=SelectGrid(s,gridId,out selectionNote);
            var xs=grid.Axes.Where(a=>a.Family=="Grid X").OrderBy(a=>a.Position).ToList();
            var ys=grid.Axes.Where(a=>a.Family=="Grid Y").OrderBy(a=>a.Position).ToList();
            if(xs.Count<2||ys.Count<2)throw new InvalidOperationException("Lưới chưa đủ hai họ trục.");
            // Keep the user's X/Y keyplan convention independent of member direction.
            // These are the actual grid vectors, not fixed global model axes.
            int keyTurn=keyQuarterTurns%4;
            Frame key=new Frame(xs[0].Direction,ys[0].Direction).Spin(keyTurn*90);
            if(!column&&autoDirection)alongY=BeamAlongY(s,key);
            var primary=s.Views.Where(v=>Geometry.IsStandardView(v.Type)).ToList();
            bool completePair=s.Views.Count==2&&primary.Count==2&&primary.Select(v=>v.Type).Distinct().Count()==2
                &&(column||primary.Any(v=>v.Type=="FrontView")&&primary.Any(v=>v.Type=="TopView"));
            if(!completePair)
                return AnalyzeIncomplete(s,column,alongY,grid.Id,keyQuarterTurns,key,primary);
            var fronts=s.Views.Where(v=>v.Type=="FrontView").ToList();
            ViewReference first,second;
            if(!column)
            {
                var tops=s.Views.Where(v=>v.Type=="TopView").ToList();
                if(fronts.Count!=1||tops.Count!=1)throw new InvalidOperationException("Cần một Front và một Top chứa cấu kiện.");
                first=fronts[0];second=tops[0];
            }
            else
            {
                // Do not discard a tilted/wrong elevation and accidentally validate two others.
                var elevations=s.Views.Where(v=>v.Type=="FrontView"||v.Type=="TopView"||v.Type=="BackView"||v.Type=="BottomView").OrderBy(v=>v.SheetX).ToList();
                if(elevations.Count!=2||!Geometry.Finite(elevations[0].SheetX)||!Geometry.Finite(elevations[1].SheetX)||Math.Abs(elevations[0].SheetX-elevations[1].SheetX)<=0.1)
                    throw new InvalidOperationException("Chưa xác định được hai view cột trái và phải.");
                first=elevations[0];second=elevations[1];
            }
            if(first.Unsupported||second.Unsupported)throw new InvalidOperationException("Chưa hỗ trợ view phản chiếu hoặc khai triển.");
            var targets=Geometry.Targets(column,alongY,key);
            bool correct=Geometry.KeyplanError(first.Frame,targets[0])<=Geometry.ToleranceDegrees&&Geometry.KeyplanError(second.Frame,targets[1])<=Geometry.ToleranceDegrees;
            bool layout=Geometry.Finite(first.SheetX)&&Geometry.Finite(first.SheetY)&&Geometry.Finite(second.SheetX)&&Geometry.Finite(second.SheetY)
                &&(column?first.SheetX+0.1<second.SheetX:second.SheetY>first.SheetY+0.1);
            var result=new QuickResult { ViewCheckKey=key,DirectionsMatch=correct,LayoutMatches=layout,GridId=grid.Id,AlongY=alongY,IsColumn=column,
                FirstViewType=first.Type,SecondViewType=second.Type,FirstTarget=targets[0],SecondTarget=targets[1],
                ReferenceFirst=AxisLabels(s,grid,column?targets[0].Right:targets[1].Right,false),
                ReferenceSecond=AxisLabels(s,grid,column?targets[1].Right:targets[1].Up,!column),
                SelectionNote=selectionNote+(column?"":autoDirection?" · Hướng tự động theo trục dọc dầm":" · Hướng chọn tay"),
                Reference=ReferenceLabels(s,grid,column,targets) };
            if(s.Settings==null||s.Settings.Error!=null)
            {
                result.State=QuickState.MissingData;
                result.Message=s.Settings==null?"Chưa xác minh được Coordinate system và góc gốc.":s.Settings.Error;
                return result;
            }
            if(s.MainFrame==null||s.RotationSupportError!=null)
            {
                result.State=QuickState.MissingData;
                result.Message=s.RotationSupportError??"Chưa đọc được hệ Local của cấu kiện.";
                return result;
            }
            else
            {
                var original=s.Settings.AllowedAngles;
                bool originalCorrect=s.Settings.IsLocal&&original!=null
                    &&Geometry.KeyplanError(Geometry.LocalView(s.MainFrame,s.VerticalColumn,first.Type,original),targets[0])<=Geometry.ToleranceDegrees
                    &&Geometry.KeyplanError(Geometry.LocalView(s.MainFrame,s.VerticalColumn,second.Type,original),targets[1])<=Geometry.ToleranceDegrees;
                bool viewsFollowSettings=originalCorrect
                    &&Geometry.Error(Geometry.LocalView(s.MainFrame,s.VerticalColumn,first.Type,original),first.Frame)<=Geometry.ToleranceDegrees
                    &&Geometry.Error(Geometry.LocalView(s.MainFrame,s.VerticalColumn,second.Type,original),second.Frame)<=Geometry.ToleranceDegrees;
                if(correct&&originalCorrect&&viewsFollowSettings)
                {
                    result.State=layout?QuickState.Correct:QuickState.LayoutMismatch;
                    result.Message=layout?"Đúng trục theo keyplan":column?"Cần đặt view X bên trái, Y bên phải.":"Cần đặt Top ở trên Front.";
                    return result;
                }
                var search=Geometry.SearchKeyplan(s.MainFrame,s.VerticalColumn,new[]{first.Type,second.Type},targets);
                result.TestedCount=search.Evaluated.Count;result.MathematicalMatchCount=search.Matches.Count;
                result.State=search.Matches.Count==0?QuickState.NoSolution:QuickState.VerifiedSuggestion;
                result.Angles=search.Matches.Count==0?null:search.Matches[0].Angles;
                string mode=s.Settings.IsLocal?"":"Đổi Coordinate system: "+s.Settings.CoordinateSystem+" → Local.\n";
                result.ActionNote=(originalCorrect?"Góc gốc đúng nhưng view đã lệch; cần tái tạo view.\n":"")+
                    (layout?"":column?"Đặt view X bên trái, Y bên phải.":"Đặt Top ở trên Front.");
                result.Message=mode+(search.Matches.Count==0
                    ?"Không có bộ góc Local đúng keyplan trong miền X/Y cho phép và độ chính xác Z 0,001°."
                    :"View creation · Local · Nhập X / Y / Z như trên."+(originalCorrect?"\nGóc gốc đúng nhưng view đã lệch; cần tái tạo view.":"")+(layout?"":column?"\nĐặt view X bên trái, Y bên phải.":"\nĐặt Top ở trên Front."));
            }
            return result;
        }
        static QuickResult AnalyzeIncomplete(Snapshot s,bool column,bool alongY,int gridId,int turns,Frame key,List<ViewReference> present)
        {
            // Solve the full intended pair even when views are absent; a single
            // projection must never relax the 3D rotation constraints.
            var target=Geometry.Targets(column,alongY,key);
            // More than one real view cannot be substituted with an imaginary
            // pair. Keep all actual projections in the diagnostic and withhold
            // a drawing-wide angle tuple when their roles are not established.
            if(s.Views.Count>1)
            {
                var diagnostic=AnalyzeOverview(s,gridId,turns);diagnostic.IsColumn=column;diagnostic.AlongY=alongY;
                diagnostic.CompleteViews=false;diagnostic.CanApply=false;diagnostic.Angles=null;
                diagnostic.Message="Các view hiện có chưa tạo thành cặp keyplan xác định. Không xuất góc Local cho cặp view giả định.\n"+diagnostic.Message;
                diagnostic.ActionNote=diagnostic.Message;return diagnostic;
            }
            bool frontLeft=true,mappingKnown=!column;
            if(column&&present.Count>0)
            {
                var roles=new List<Tuple<string,bool>>();
                foreach(var v in present)
                {
                    double left=Math.Abs(V.Dot(v.Frame.Normal,target[0].Normal)),right=Math.Abs(V.Dot(v.Frame.Normal,target[1].Normal));
                    if(Math.Abs(left-right)>1e-6&&Math.Max(left,right)>Math.Abs(v.Frame.Normal.Z))roles.Add(Tuple.Create(v.Type,left>right));
                }
                var orders=roles.Select(role=>(role.Item1=="FrontView"||role.Item1=="BackView")?role.Item2:!role.Item2).Distinct().ToList();
                mappingKnown=roles.Count==present.Count&&orders.Count==1;
                if(mappingKnown)frontLeft=orders[0];
            }
            var surrogate=new Snapshot {Identity=s.Identity,Mark=s.Mark,ModelPath=s.ModelPath,MainPart=s.MainPart,
                MainOrigin=s.MainOrigin,MainStart=s.MainStart,MainEnd=s.MainEnd,MainFrame=s.MainFrame,Shape=s.Shape,
                VerticalColumn=s.VerticalColumn,RotationSupportError=s.RotationSupportError,Settings=s.Settings,Grids=s.Grids};
            string firstType=column&&!frontLeft?"TopView":"FrontView",secondType=firstType=="FrontView"?"TopView":"FrontView";
            if(present.Count==1&&(present[0].Type=="BackView"||present[0].Type=="BottomView"))
            {
                string basisType=present[0].Type=="BackView"?"FrontView":"TopView";
                if(firstType==basisType)firstType=present[0].Type;else secondType=present[0].Type;
            }
            surrogate.Views.Add(new ViewReference {Type=firstType,Frame=target[0],SheetX=0,SheetY=0});
            surrogate.Views.Add(new ViewReference {Type=secondType,Frame=target[1],SheetX=100,SheetY=100});
            var r=QuickAnalyze(surrogate,column,alongY,gridId,turns);
            if(column&&(!mappingKnown||r.State==QuickState.NoSolution))
            {
                // The Local basis can uniquely determine Front/Top assignment
                // even without any existing elevation. Check BOTH full pairs.
                surrogate.Views[0].Type=secondType;surrogate.Views[1].Type=firstType;
                var alternative=QuickAnalyze(surrogate,column,alongY,gridId,turns);
                bool firstValid=r.State!=QuickState.NoSolution&&r.State!=QuickState.MissingData;
                bool secondValid=alternative.State!=QuickState.NoSolution&&alternative.State!=QuickState.MissingData;
                if(firstValid!=secondValid)
                {
                    mappingKnown=true;
                    if(secondValid) {r=alternative;string swap=firstType;firstType=secondType;secondType=swap;}
                }
            }
            r.CompleteViews=false;r.CanApply=mappingKnown&&s.Views.All(v=>!v.Unsupported);
            r.LayoutMatches=false;
            r.DirectionsMatch=present.Count>0&&mappingKnown&&present.All(v=>!v.Unsupported&&Geometry.KeyplanError(v.Frame,target[v.Type==firstType?0:1])<=Geometry.ToleranceDegrees);
            var original=s.Settings==null?null:s.Settings.AllowedAngles;
            bool follow=s.MainFrame!=null&&original!=null&&present.All(v=>Geometry.Error(v.Frame,Geometry.LocalView(s.MainFrame,s.VerticalColumn,v.Type,original))<=Geometry.ToleranceDegrees);
            bool currentValid=s.Settings!=null&&s.Settings.IsLocal&&original!=null&&s.MainFrame!=null&&s.RotationSupportError==null
                &&Geometry.KeyplanError(Geometry.LocalView(s.MainFrame,s.VerticalColumn,firstType,original),target[0])<=Geometry.ToleranceDegrees
                &&Geometry.KeyplanError(Geometry.LocalView(s.MainFrame,s.VerticalColumn,secondType,original),target[1])<=Geometry.ToleranceDegrees;
            if(currentValid&&r.State!=QuickState.MissingData&&r.State!=QuickState.NoSolution)
            {
                // Synthetic target frames are unrolled. Preserve a valid original
                // tuple for rolled sections instead of rewriting equivalent angles.
                r.Angles=original;r.State=r.DirectionsMatch&&follow?QuickState.IncompleteViews:QuickState.VerifiedSuggestion;
            }
            if(r.State==QuickState.Correct||r.State==QuickState.LayoutMismatch)r.State=QuickState.IncompleteViews;
            if(r.State==QuickState.IncompleteViews&&(!r.DirectionsMatch||!follow)&&original!=null)
            {r.Angles=original;r.State=QuickState.VerifiedSuggestion;}
            if(r.State==QuickState.IncompleteViews)r.Angles=original;
            string warning="Đã kiểm "+present.Count+" view tiêu chuẩn. Chưa đủ cặp view để xác nhận keyplan.";
            if(present.Count>2||present.GroupBy(v=>v.Type).Any(g=>g.Count()>1))warning="Đã kiểm "+present.Count+" view Front/Top; có view trùng loại, cần kiểm tra bố trí.";
            int other=s.Views.Count-present.Count;
            if(other>0) {warning+=" "+other+" view loại khác chưa được kiểm chứng góc.";r.CanApply=false;}
            if(column)warning+=" Mục tiêu: "+firstType.Replace("View","")+" trái, "+secondType.Replace("View","")+" phải.";
            if(!mappingKnown) {warning+=" Chưa đủ căn cứ tự áp dụng cho cột.";r.CanApply=false;}
            if(s.Views.Any(v=>v.Unsupported)) {warning+=" Có view phản chiếu/khai triển; không tự áp dụng.";r.CanApply=false;}
            if(!r.CanApply)r.Angles=null;
            r.ActionNote=warning;r.Message=(r.State==QuickState.MissingData||r.State==QuickState.NoSolution?r.Message+"\n":r.Angles==null?"":"Góc Local theo hình học cấu kiện.\n")+warning;
            return r;
        }
        static V Center(Snapshot s) { return s.Shape!=null?s.Shape.Center:s.MainStart.HasValue&&s.MainEnd.HasValue?(s.MainStart.Value+s.MainEnd.Value)*0.5:s.MainOrigin; }
        public static bool BeamAlongY(Snapshot s,Frame key)
        {
            V axis=MemberAxis(s);
            double x=Math.Abs(V.Dot(axis,key.Right)),y=Math.Abs(V.Dot(axis,key.Up));
            if(Math.Max(x,y)<1e-6)
                throw new InvalidOperationException("Trục cấu kiện đứng. Chọn Tự nhận hoặc phương án Cột.");
            if(Math.Abs(x-y)<=1e-6)
                throw new InvalidOperationException("Trục dầm nghiêng 45° giữa X/Y. Hãy chọn hướng X/Y.");
            return y>x;
        }
        sealed class GridRank
        {
            public GridReference Grid;
            public double Outside, Alignment, Area;
        }
        public static GridReference SelectGrid(Snapshot s,int gridId,out string note)
        {
            var ranks=new List<GridRank>();
            foreach(var g in s.Grids.Where(g=>g.Error==null))
            {
                var xs=g.Axes.Where(a=>a.Family=="Grid X").OrderBy(a=>a.Position).ToList();
                var ys=g.Axes.Where(a=>a.Family=="Grid Y").OrderBy(a=>a.Position).ToList();
                if(xs.Count<2||ys.Count<2)continue;
                var key=new Frame(xs[0].Direction,ys[0].Direction);
                if(Geometry.Angle(key.Normal,new V(0,0,1))>Geometry.ToleranceDegrees)continue;
                double outside=0;
                var coverage=s.Shape!=null&&s.Shape.Vertices!=null?s.Shape.Vertices:(IEnumerable<V>)new[]{s.MainStart??s.MainOrigin,s.MainEnd??s.MainOrigin};
                foreach(var point in coverage)
                    foreach(var axes in new[]{xs,ys})
                    {
                        double p=V.Dot(point-g.Origin,axes[0].Direction);
                        outside+=Math.Max(0,Math.Max(axes[0].Position-p,p-axes.Last().Position));
                    }
                var axis=s.Overview||s.GenericMember&&s.Shape!=null&&!s.Shape.UniqueAxis?new V():s.MainFrame==null&&!s.MainStart.HasValue?new V():MemberAxis(s);
                double alignment=Math.Max(Math.Abs(V.Dot(axis,key.Right)),Math.Abs(V.Dot(axis,key.Up)));
                ranks.Add(new GridRank {Grid=g,Outside=outside,Alignment=alignment,
                    Area=(xs.Last().Position-xs[0].Position)*(ys.Last().Position-ys[0].Position)});
            }
            if(gridId!=0)
            {
                var manual=ranks.SingleOrDefault(r=>r.Grid.Id==gridId);
                if(manual==null)throw new InvalidOperationException("Lưới đã chọn không còn hợp lệ. Chọn Lưới: tự động trong Hiệu chỉnh.");
                note="Lưới chọn tay";return manual.Grid;
            }
            // Auxiliary unlabelled/elevation grids do not define a plan keyplan.
            // Area alone cannot distinguish a building grid from a conflicting
            // local grid. Prefer a larger grid only if it contains the SAME labelled
            // planes of every equally relevant candidate. Identical copies are safe.
            if(ranks.Count==0)throw new InvalidOperationException("Chưa đọc được lưới keyplan có đủ hai họ trục được đặt nhãn.");
            double outsideMin=ranks.Min(r=>r.Outside);
            var nearby=ranks.Where(r=>r.Outside<=outsideMin+0.001).ToList();
            double alignmentMax=nearby.Max(r=>r.Alignment);
            var tied=nearby.Where(r=>r.Alignment>=alignmentMax-1e-6).ToList();
            var best=tied.Where(r=>tied.All(other=>ContainsGrid(r.Grid,other.Grid)))
                .OrderByDescending(r=>r.Area).ThenBy(r=>r.Grid.Id).FirstOrDefault();
            if(best==null)throw new InvalidOperationException("Các lưới cùng phù hợp vị trí nhưng khác trục/nhãn: "+
                string.Join(" / ",tied.OrderBy(r=>r.Grid.Id).Select(r=>r.Grid.ToString()))+". Chọn lưới trong Hiệu chỉnh.");
            note="Lưới tự động: "+best.Grid.Name+" · "+best.Grid.Id+
                (ranks.Count>1?" (phạm vi cấu kiện → phương trục → trục/nhãn tương thích)":"")+
                (s.Grids.Count>ranks.Count?" · Bỏ qua lưới phụ thiếu hai họ nhãn hợp lệ":"");
            return best.Grid;
        }
        static bool ContainsGrid(GridReference outer,GridReference inner)
        {
            return inner.Axes.All(a=>outer.Axes.Any(b=>a.Family==b.Family&&a.Label==b.Label&&
                Geometry.Angle(a.Direction,b.Direction)<=Geometry.ToleranceDegrees&&
                Math.Abs(V.Dot(inner.Origin-outer.Origin,b.Direction)+a.Position-b.Position)<=0.001));
        }
        static string ReferenceLabels(Snapshot s,GridReference grid,bool column,Frame[] targets)
        {
            if(column)return "Keyplan · "+grid.Name+" · View trái: "+AxisLabels(s,grid,targets[0].Right,false)+
                " · View phải: "+AxisLabels(s,grid,targets[1].Right,false);
            return "Top · Trục đứng (trái → phải): "+AxisLabels(s,grid,targets[1].Right,false)+
                "\nTrục ngang gần dầm: "+AxisLabels(s,grid,targets[1].Up,true);
        }
        static string AxisLabels(Snapshot s,GridReference grid,V direction,bool nearest)
        {
            var axes=grid.Axes.Where(a=>Math.Abs(V.Dot(a.Direction,direction))>0.999999)
                .Select(a=>new {a.Label,Position=a.Position*V.Dot(a.Direction,direction)}).OrderBy(a=>a.Position).ToList();
            double p=V.Dot(Center(s)-grid.Origin,direction);
            if(nearest)
            {
                double distance=axes.Min(a=>Math.Abs(a.Position-p));
                return string.Join(" / ",axes.Where(a=>Math.Abs(Math.Abs(a.Position-p)-distance)<0.001)
                    .Select(a=>a.Label+(Math.Abs(a.Position-p)<0.001?" (trùng)":a.Position<p?" (dưới)":" (trên)")));
            }
            var exact=axes.FirstOrDefault(a=>Math.Abs(a.Position-p)<0.001);
            if(exact!=null)return exact.Label;
            int right=axes.FindIndex(a=>a.Position>p);
            if(right==0)return axes[0].Label+" (bên phải)";
            if(right<0)return axes.Last().Label+" (bên trái)";
            return axes[right-1].Label+" → "+axes[right].Label;
        }
        static V Vec(G.Point p) { return new V(p.X,p.Y,p.Z); }
        static V GlobalDirection(G.Vector v,G.Matrix matrix)
        { return (Vec(matrix.Transform(new G.Point(v.X,v.Y,v.Z)))-Vec(matrix.Transform(new G.Point()))).Unit(); }
        static int Id(object obj)
        {
            var p=obj.GetType().GetProperty("Identifier",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
            if(p==null)throw new InvalidOperationException("Không đọc được ID đối tượng.");
            return ((Tekla.Structures.Identifier)p.GetValue(obj,null)).ID;
        }
        public static Snapshot Capture()
        { return CaptureCore(true); }
        internal static Snapshot CaptureGeometry()
        { return CaptureCore(false); }
        static Snapshot CaptureCore(bool readProperties)
        {
            var model=new M.Model();var handler=new D.DrawingHandler();
            if(!model.GetConnectionStatus()||!handler.GetConnectionStatus())throw new InvalidOperationException("Chưa kết nối Tekla Model/Drawing API.");
            var drawing=handler.GetActiveDrawing();
            if(!(drawing is D.AssemblyDrawing))throw new InvalidOperationException("Slot 09 áp dụng cho Assembly drawing; Single-part không cần xoay theo keyplan.");
            if(drawing.UpToDateStatus==D.DrawingUpToDateStatus.PartsWereModified||drawing.UpToDateStatus==D.DrawingUpToDateStatus.OriginalPartDeleted||drawing.UpToDateStatus==D.DrawingUpToDateStatus.AllPartsDeleted)
                throw new InvalidOperationException("Model đã thay đổi. Cập nhật bản vẽ trước khi kiểm tra keyplan.");
            bool authoritative;
            var references=KeyplanShape.Resolve(model,drawing,out authoritative);
            if(references.Count==0)throw new InvalidOperationException("Bản vẽ không chứa liên kết tới Part 3D còn tồn tại. Không có hình học để đối chiếu.");
            bool overview=!authoritative||references.Count!=1;
            var part=references[0]; // Authoritative Assembly main part; overview uses every reference below.
            var toGlobal=model.GetWorkPlaneHandler().GetCurrentTransformationPlane().TransformationMatrixToGlobal;
            var s=new Snapshot { Mark=drawing.Mark,ModelPath=model.GetInfo().ModelPath,DrawingKind=drawing.GetType().Name,Overview=overview,
                MainPart=part.Identifier.ID+" · "+part.Name,MainOrigin=Vec(toGlobal.Transform(part.GetCoordinateSystem().Origin)) };
            var partCs=part.GetCoordinateSystem();
            s.MainFrame=new Frame(GlobalDirection(partCs.AxisX,toGlobal),GlobalDirection(partCs.AxisY,toGlobal));
            var beam=part as M.Beam;
            if(beam!=null) { s.MainStart=Vec(toGlobal.Transform(beam.StartPoint));s.MainEnd=Vec(toGlobal.Transform(beam.EndPoint)); }
            bool legacy=beam!=null&&(beam.Type==M.Beam.BeamTypeEnum.BEAM||beam.Type==M.Beam.BeamTypeEnum.COLUMN);
            if(legacy)
            {
                double radius=0;
                if(!beam.GetReportProperty("RADIUS",ref radius)||!Geometry.Finite(radius)||radius!=0)legacy=false;
            }
            s.GenericMember=!legacy;
            if(s.GenericMember&&!overview)
            {
                s.Shape=KeyplanShape.Read(part,toGlobal);
                s.MainStart=s.MainEnd=null;
                s.ShapeNote=part.GetType().Name+" · hướng theo solid 3D";
            }
            string version=Tekla.Structures.TeklaStructuresInfo.GetCurrentProgramVersion();
            if(!version.StartsWith("2025",StringComparison.Ordinal)||Tekla.Structures.TeklaStructuresInfo.GetBuildNumber().ToString()!="56843")
                s.RotationSupportError="Phiên bản Tekla này chưa được kiểm chứng quy tắc quay.";
            // Tekla can report a downward vertical member as BEAM. Its drawing still uses
            // vertical placing; determine this from the actual axis, never the part name.
            if(Geometry.Angle(s.MainFrame.Right,new V(0,0,s.MainFrame.Right.Z<0?-1:1))<=Geometry.ToleranceDegrees)
            {
                string placement="";
                if(!Tekla.Structures.TeklaStructuresSettings.GetAdvancedOption("XS_USE_VERTICAL_PLACING_FOR_COLUMNS_IN",ref placement))
                    s.RotationSupportError="Chưa đọc được tùy chọn đặt cột của Tekla.";
                else if(placement=="ASSEMBLY_AND_SINGLE_PART_DRAWINGS"||placement=="ASSEMBLY_DRAWINGS")s.VerticalColumn=true;
                else if(legacy&&drawing is D.AssemblyDrawing)s.RotationSupportError="Tùy chọn đặt cột hiện tại chưa được kiểm chứng.";
            }
            if(overview)
            {
                s.MainFrame=new Frame(new V(1,0,0),new V(0,1,0));s.MainStart=s.MainEnd=null;s.VerticalColumn=false;
                s.MainPart=references.Count+" cấu kiện liên kết 3D";s.MainOrigin=new V();
                foreach(var reference in references)s.MainOrigin+=Vec(toGlobal.Transform(reference.GetCoordinateSystem().Origin))*(1.0/references.Count);
                s.ShapeNote="Đối chiếu riêng từng view; bản vẽ không có hệ Local chung";
            }
            s.Identity=s.ModelPath+"|"+Id(drawing)+"|"+part.Identifier.ID;
            if(readProperties&&!overview)s.Settings=DrawingSettingsReader.Read();
            var referenceIds=new HashSet<int>(references.Select(p=>p.Identifier.ID));
            var views=drawing.GetSheet().GetAllViews();
            while(views.MoveNext())
            {
                var v=views.Current as D.View;if(v==null)continue;
                string type=v.ViewType.ToString();
                bool auxiliary=type.IndexOf("Section",StringComparison.OrdinalIgnoreCase)>=0 || type.IndexOf("Detail",StringComparison.OrdinalIgnoreCase)>=0;
                bool contains=false;
                if(!auxiliary)
                {
                    var parts=v.GetAllObjects(typeof(D.Part));
                    while(parts.MoveNext()) { var p=parts.Current as D.Part;if(p!=null&&referenceIds.Contains(p.ModelIdentifier.ID))contains=true; }
                }
                var item=new ViewReference {Id=Id(v),Name=v.Name,Type=type};
                try
                {
                    var cs=v.DisplayCoordinateSystem;item.Frame=new Frame(Vec(cs.AxisX),Vec(cs.AxisY));
                    var box=v.GetAxisAlignedBoundingBox();
                    if(box==null)throw new InvalidOperationException("Không đọc được khung view trên giấy.");
                    item.SheetX=(box.MinPoint.X+box.MaxPoint.X)/2;item.SheetY=(box.MinPoint.Y+box.MaxPoint.Y)/2;
                    item.Unsupported=v.Attributes.ReflectedView||v.Attributes.UnfoldedView||v.Attributes.UndeformedView;
                }
                catch(Exception ex)
                {
                    if(contains&&!auxiliary)throw; // Bảo toàn điều kiện dữ liệu bắt buộc của solver chính.
                    item.ReadError="Chưa đọc đủ dữ liệu view: "+ex.GetBaseException().Message;
                }
                s.AllViews.Add(item);
                if(contains&&!auxiliary)s.Views.Add(item);
            }
            var grids=model.GetModelObjectSelector().GetAllObjectsWithType(M.ModelObject.ModelObjectEnum.GRID);
            while(grids.MoveNext())
            {
                var grid=grids.Current as M.Grid;if(grid==null)continue;
                var item=new GridReference { Id=grid.Identifier.ID,Name=grid.Name };
                try
                {
                    var cs=grid.GetCoordinateSystem();item.Origin=Vec(toGlobal.Transform(cs.Origin));
                    V gx=GlobalDirection(cs.AxisX,toGlobal),gy=GlobalDirection(cs.AxisY,toGlobal);
                    if(Geometry.Angle(V.Cross(gx,gy),new V(0,0,1))>Geometry.ToleranceDegrees)throw new InvalidOperationException("Grid không nằm ngang hoặc chiều cao không theo +Z.");
                    AddAxes(item,"Grid X",Geometry.ParseGrid(grid.CoordinateX,grid.LabelX,false),gx);
                    AddAxes(item,"Grid Y",Geometry.ParseGrid(grid.CoordinateY,grid.LabelY,false),gy);
                    // Floor labels are optional for choosing a horizontal keyplan.
                    // Elevation direction comes from +Z, never from invented floor names.
                    try { item.Floors=Geometry.ParseGrid(grid.CoordinateZ,grid.LabelZ,true); }
                    catch(InvalidOperationException) { item.Floors=new List<GridTick>(); }
                }
                catch(Exception ex) { item.Error=ex.Message; }
                s.Grids.Add(item);
            }
            if(s.Grids.Count==0)throw new InvalidOperationException("Không có rectangular grid để xác định keyplan. Chưa hỗ trợ radial grid.");
            // Detect a user changing the active drawing while the read was running.
            var last=handler.GetActiveDrawing();
            if(last==null||!last.IsSameDatabaseObject(drawing)||!string.Equals(model.GetInfo().ModelPath,s.ModelPath,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Bản vẽ đã thay đổi trong lúc đọc; hãy đọc lại.");
            return s;
        }
        static void AddAxes(GridReference grid,string family,IEnumerable<GridTick> ticks,V direction)
        { foreach(var t in ticks)grid.Axes.Add(new AxisReference { Family=family,Label=t.Label,Position=t.Position,Direction=direction }); }
    }
}
#endregion

#region Đọc thuộc tính bản vẽ
namespace TTSK_AutoDim_Plates.Keyplan
{


using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

    public sealed class DrawingSettings
    {
        public string CoordinateSystem, Error;
        public double X, Y, Z;
        public bool IsLocal { get { return Error==null&&CoordinateSystem=="Local"; } }
        public Angles AllowedAngles
        {
            get { return Geometry.AllowedQuarter(X)&&(Y==0||Y==180)&&Geometry.Finite(Z)?new Angles((int)X,(int)Y,Z):null; }
        }
    }

    // The active-object-properties command reads the active drawing into a NEW dialog.
    // Empty drawing selection is required; previous selection is restored afterwards.
    // Never use Get (it can load ???), general properties, preset files, or a dialog
    // already open: those can differ from the drawing. Only navigation and Cancel.
    internal static partial class DrawingSettingsReader
    {
        static readonly object Sync=new object();
        const uint BM_CLICK=0x00F5, WM_GETTEXT=0x000D;
        delegate bool EnumProc(IntPtr h,IntPtr p);
        [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left,Top,Right,Bottom; }
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback,IntPtr p);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h,EnumProc callback,IntPtr p);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint id);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h,out Rect rect);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h,StringBuilder s,int size);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr SendMessageTimeout(IntPtr h,uint message,IntPtr w,StringBuilder text,uint flags,uint timeout,out IntPtr result);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr SendMessageTimeout(IntPtr h,uint message,IntPtr w,IntPtr l,uint flags,uint timeout,out IntPtr result);
        sealed class Field { internal IntPtr Handle;internal string Text,Class;internal Rect Rect; }
        static string Text(IntPtr h)
        {
            var text=new StringBuilder(1024);IntPtr result;
            if(SendMessageTimeout(h,WM_GETTEXT,new IntPtr(text.Capacity),text,2,1000,out result)==IntPtr.Zero)
                return ""; // Non-text controls can refuse WM_GETTEXT; required fields reject empty values.
            return text.ToString().Trim();
        }
        static List<IntPtr> Dialogs(int pid)
        {
            var list=new List<IntPtr>();
            EnumWindows((h,p)=> { uint id;GetWindowThreadProcessId(h,out id);if(id==pid&&IsWindowVisible(h)&&DialogName(Text(h))!=null)list.Add(h);return true; },IntPtr.Zero);
            return list;
        }
        static string DialogName(string title)
        {
            switch(title)
            {
                case "Assembly drawing properties":return "adraw_dial";
                default:return null;
            }
        }
        static void NavigateAttributes(Process process,IntPtr dialog)
        {
            string name=DialogName(Text(dialog));
            if(name==null)throw new InvalidOperationException("Không xác định được hộp thuộc tính của bản vẽ đang mở.");
            Akit(process,(t,s)=> {
                t.GetMethod("TreeSelect").Invoke(s,new object[]{name,"gratCastUnitDrawingAttributesMenuTree","View creation"});
                t.GetMethod("TabChange").Invoke(s,new object[]{name,"contMain","tabAttributes"});
            });
        }
        static List<Field> Fields(IntPtr dialog)
        {
            var fields=new List<Field>();
            EnumChildWindows(dialog,(h,p)=> { var name=new StringBuilder(256);GetClassName(h,name,name.Capacity);Rect rect;GetWindowRect(h,out rect);fields.Add(new Field {Handle=h,Class=name.ToString(),Text=Text(h),Rect=rect});return true; },IntPtr.Zero);
            return fields;
        }
        static string ComboText(IntPtr h)
        {
            // Owner-drawn combos lack CBS_HASSTRINGS: WM_GETTEXT is "???" and
            // CB_GETLBTEXT is an item pointer. Read only validated selection indexes.
            IntPtr index;
            if(SendMessageTimeout(h,0x147,IntPtr.Zero,IntPtr.Zero,2,1000,out index)==IntPtr.Zero||index.ToInt64()<0)return "";
            return "#"+index.ToInt64().ToString(CultureInfo.InvariantCulture);
        }
        static void Button(IntPtr dialog,string label)
        {
            if(label!="Cancel")throw new InvalidOperationException("Read-only button guard.");
            var buttons=Fields(dialog).Where(f=>f.Class.Equals("Button",StringComparison.OrdinalIgnoreCase)&&f.Text.Replace("&","")==label&&IsWindowVisible(f.Handle)).ToList();
            if(buttons.Count!=1)throw new InvalidOperationException("Không xác định được nút "+label+" của thuộc tính bản vẽ.");
            IntPtr result;
            if(SendMessageTimeout(buttons[0].Handle,BM_CLICK,IntPtr.Zero,IntPtr.Zero,2,3000,out result)==IntPtr.Zero)
                throw new InvalidOperationException("Tekla chưa phản hồi nút "+label+".");
        }
        static string Value(List<Field> fields,string label,string controlClass)
        {
            fields=fields.Where(f=>IsWindowVisible(f.Handle)).ToList();
            var labels=fields.Where(f=>f.Text.Replace("&","").TrimEnd(':')==label.TrimEnd(':')).ToList();
            if(labels.Count!=1)throw new InvalidOperationException("Không đọc được trường "+label);
            var anchor=labels[0].Rect;
            // Match the labelled row, not the collection order or fixed screen pixels.
            var controls=fields.Where(f=>f.Class.Equals(controlClass,StringComparison.OrdinalIgnoreCase)&&f.Rect.Left>=anchor.Right&&Math.Abs((f.Rect.Top+f.Rect.Bottom)-(anchor.Top+anchor.Bottom))<=Math.Max(8,anchor.Bottom-anchor.Top)).OrderBy(f=>f.Rect.Left).ToList();
            if(controls.Count!=1)throw new InvalidOperationException("Trường "+label+" không xác định duy nhất.");
            return controlClass=="ComboBox"?ComboText(controls[0].Handle):controls[0].Text;
        }
        static DrawingSettings ReadValues(IntPtr dialog)
        {
            var fields=Fields(dialog);
            string mode=Value(fields,"Coordinate system:","ComboBox");
            if(mode!="#0"&&mode!="#1")throw new InvalidOperationException("Chế độ hệ tọa độ chưa được hỗ trợ: "+mode);
            var result=new DrawingSettings {CoordinateSystem=mode=="#0"?"Local":"Model"};
            result.X=ComboAngle(Value(fields,"Around X:","ComboBox"),new[]{0,90,180,270});
            result.Y=ComboAngle(Value(fields,"Around Y:","ComboBox"),new[]{0,180});
            result.Z=Number(Value(fields,"Around Z:","Edit"));
            return result;
        }
        static int ComboAngle(string value,int[] values)
        {
            for(int i=0;i<values.Length;i++)if(value=="#"+i)return values[i];
            throw new InvalidOperationException("Chưa đọc được góc gốc trong View creation.");
        }
        static double Number(string text)
        {
            double value;
            if(!double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)||!Geometry.Finite(value))
                throw new InvalidOperationException("Không đọc được góc gốc: "+text);
            return value;
        }
        static void Akit(Process process,Action<Type,object> action)
        {
            string bin=Path.GetDirectoryName(process.MainModule.FileName);
            var proxyType=Assembly.LoadFrom(Path.Combine(bin,"Tekla.Macros.Akit.dll")).GetType("Tekla.Macros.Akit.DynamicScriptMessengerClientProxy",true);
            var scriptType=Assembly.LoadFrom(Path.Combine(bin,"Akit5.dll")).GetType("Tekla.Technology.Akit.IScript",true);
            object proxy=proxyType.GetMethod("Create").Invoke(null,new object[]{null});bool subscribed=false;
            try
            {
                subscribed=(bool)proxyType.GetMethod("Subscribe").Invoke(proxy,new object[]{process.Id});
                if(!subscribed)throw new InvalidOperationException("Không kết nối được thuộc tính gốc của Tekla.");
                action(scriptType,proxyType.GetMethod("GetRemoteScriptAdapter").Invoke(proxy,new object[]{process.Id}));
            }
            finally
            {
                if(subscribed)try { proxyType.GetMethod("Unsubscribe").Invoke(proxy,new object[]{process.Id}); }catch { }
                var client=proxyType.GetProperty("MessengerClient",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(proxy,null);
                var messenger=client?.GetType().GetProperty("Messenger",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(client,null) as IDisposable;
                if(messenger!=null)messenger.Dispose();
            }
        }
        internal static DrawingSettings Read()
        { return WithOwnedDialog(null); }
        internal static DrawingSettings WithOwnedDialog(Action<Process,IntPtr,DrawingSettings> action,Func<bool> canClose=null)
        {
            lock(Sync)
            {
                IntPtr owned=IntPtr.Zero;Thread open=null;Process process=null;
                var selected=new System.Collections.ArrayList();
                Tekla.Structures.Drawing.UI.DrawingObjectSelector selector=null;
                bool restoreSelection=false;
                var outcome=new DrawingSettings();
                Tekla.Structures.Drawing.Drawing active=null;
                try
                {
                    if(!Tekla.Structures.TeklaStructuresInfo.GetCurrentProgramVersion().StartsWith("2025",StringComparison.Ordinal)||Tekla.Structures.TeklaStructuresInfo.GetBuildNumber().ToString()!="56843")
                        throw new InvalidOperationException("Phiên bản Tekla chưa được kiểm chứng việc đọc thuộc tính gốc.");
                    var handler=new Tekla.Structures.Drawing.DrawingHandler();
                    active=handler.GetActiveDrawing();
                    if(!(active is Tekla.Structures.Drawing.AssemblyDrawing))throw new InvalidOperationException("Hãy mở Assembly drawing cần kiểm tra.");
                    process=TTSK_AutoDim_Plates.TeklaSessionCoordinator.GetBoundProcess();
                    if(Dialogs(process.Id).Count!=0)throw new InvalidOperationException("Đóng Drawing properties rồi kiểm tra lại để đọc giá trị gốc.");
                    selector=handler.GetDrawingObjectSelector();var selection=selector.GetSelected();
                    while(selection.MoveNext())selected.Add(selection.Current);
                    if(selected.Count>0) { restoreSelection=true;if(!selector.UnselectAllObjects())throw new InvalidOperationException("Chưa thể đọc thuộc tính gốc khi đang chọn đối tượng."); }
                    Exception openError=null;
                    open=new Thread(()=> { try {
                        Akit(process,(t,s)=>t.GetMethod("Callback").Invoke(s,new object[]{"acmd_display_selected_drawing_object_dialog","","main_frame"}));
                    } catch(Exception e) { openError=e; } });
                    open.IsBackground=true;open.SetApartmentState(ApartmentState.STA);open.Start();
                    for(int i=0;i<60;i++)
                    {
                        Thread.Sleep(100);var dialogs=Dialogs(process.Id);
                        if(dialogs.Count==1) { owned=dialogs[0];break; }
                        if(dialogs.Count>1)throw new InvalidOperationException("Có nhiều cửa sổ thuộc tính; chưa thể xác minh.");
                        if(openError!=null)throw openError;
                    }
                    if(owned==IntPtr.Zero)throw new InvalidOperationException("Không mở được thuộc tính gốc của bản vẽ.");
                    string expected="adraw_dial";
                    if(DialogName(Text(owned))!=expected)throw new InvalidOperationException("Hộp thuộc tính "+Text(owned)+" không khớp loại bản vẽ đang mở.");
                    if(!open.Join(3000))throw new InvalidOperationException("Tekla chưa tải xong thuộc tính gốc.");
                    if(openError!=null)throw openError;
                    NavigateAttributes(process,owned);
                    Thread.Sleep(150);
                    var first=ReadValues(owned);
                    Thread.Sleep(100);
                    var second=ReadValues(owned);
                    if(first.CoordinateSystem!=second.CoordinateSystem||first.X!=second.X||first.Y!=second.Y||first.Z!=second.Z)
                        throw new InvalidOperationException("Thuộc tính gốc thay đổi trong lúc đọc. Hãy kiểm tra lại.");
                    outcome=second;
                    if(action!=null)action(process,owned,second);
                }
                catch(Exception e) { outcome.Error=e.GetBaseException().Message; }
                finally
                {
                    if(owned!=IntPtr.Zero&&IsWindow(owned)&&IsWindowVisible(owned)&&(canClose==null||canClose()))try {
                        Button(owned,"Cancel");
                        for(int i=0;i<10&&IsWindowVisible(owned);i++)Thread.Sleep(50);
                        if(IsWindowVisible(owned))outcome.Error="Đóng hộp thuộc tính bằng Cancel rồi kiểm tra lại.";
                    } catch { outcome.Error="Chưa đóng được hộp thuộc tính. Bấm Cancel rồi kiểm tra lại."; }
                    if(open!=null&&open.IsAlive)open.Join(500);
                    if(restoreSelection&&selector!=null&&(canClose==null||canClose()))try {
                        var current=new Tekla.Structures.Drawing.DrawingHandler().GetActiveDrawing();
                        if(current!=null&&active!=null&&current.IsSameDatabaseObject(active)) {
                            if(!selector.SelectObjects(selected,false))outcome.Error="Chưa khôi phục được lựa chọn đối tượng. Hãy kiểm tra lại.";
                        } else outcome.Error="Bản vẽ đã thay đổi trong lúc đọc thuộc tính gốc.";
                    } catch { outcome.Error="Bản vẽ đã thay đổi trong lúc đọc thuộc tính gốc."; }
                    if(process!=null)process.Dispose();
                }
                return outcome;
            }
        }
    }
}
#endregion

#region Kiểm tra và áp dụng keyplan
namespace TTSK_AutoDim_Plates.Keyplan
{


using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using D = Tekla.Structures.Drawing;

    public sealed class KeyplanRunRequest
    {
        public string PreviousIdentity;
        public bool? Column, AlongY;
        public int GridId, QuarterTurns;
        public bool AutoApply;
        public Func<bool> ConfirmRecreate;
    }
    public sealed class KeyplanRunResult
    {
        public Snapshot Snapshot;
        public QuickResult Result;
        public bool Applied, ResetOverrides;
        public string Note;
    }
    public sealed class ApplyRequest
    {
        public string Identity;
        public bool Column, AlongY;
        public int GridId, QuarterTurns;
        public Angles Angles;
        // Needed only when Tekla locks Recreate = Yes after changing angles.
        public Func<bool> ConfirmRecreate;
    }
    public static class KeyplanApply
    {
        public static bool Same(DrawingSettings settings,Angles angles)
        { return settings!=null&&settings.IsLocal&&angles!=null&&settings.X==angles.X&&settings.Y==angles.Y&&Geometry.Finite(settings.Z)
            &&Math.Min(Geometry.Wrap(settings.Z-angles.Z),Geometry.Wrap(angles.Z-settings.Z))<=Geometry.ToleranceDegrees; }
        public static void Validate(ApplyRequest request,Snapshot fresh,QuickResult result)
        {
            if(request==null||request.Angles==null||fresh==null||fresh.Identity!=request.Identity)
                throw new InvalidOperationException("Bản vẽ đã thay đổi. Kiểm tra lại trước khi áp dụng.");
            if(fresh.RotationSupportError!=null||fresh.Settings==null||fresh.Settings.Error!=null||result==null||!result.CanApply)
                throw new InvalidOperationException("Chưa đủ dữ liệu tin cậy để tự áp dụng.");
            bool matchingCandidate=result.State==QuickState.VerifiedSuggestion&&result.Angles!=null&&result.Angles.ToString()==request.Angles.ToString();
            bool verifiedCurrent=(result.State==QuickState.Correct||result.State==QuickState.LayoutMismatch||result.State==QuickState.IncompleteViews)&&Same(fresh.Settings,request.Angles);
            if(!matchingCandidate&&!verifiedCurrent)
                throw new InvalidOperationException("Kết quả hình học đã thay đổi. Kiểm tra lại trước khi áp dụng.");
        }
        public static Snapshot Apply(ApplyRequest request)
        {
            if(request==null)throw new ArgumentNullException("request");
            return Run(new KeyplanRunRequest {PreviousIdentity=request.Identity,Column=request.Column,AlongY=request.AlongY,
                GridId=request.GridId,QuarterTurns=request.QuarterTurns,AutoApply=true,ConfirmRecreate=request.ConfirmRecreate},request).Snapshot;
        }
        public static QuickResult Analyze(Snapshot snapshot,KeyplanRunRequest request)
        {
            bool reset=request.PreviousIdentity!=null&&request.PreviousIdentity!=snapshot.Identity;
            return Reader.Analyze(snapshot,reset?null:request.Column,reset?null:request.AlongY,reset?0:request.GridId,reset?0:request.QuarterTurns);
        }
        public static KeyplanRunResult Run(KeyplanRunRequest request,ApplyRequest expected=null)
        {
            if(request==null)throw new ArgumentNullException("request");
            var handler=new D.DrawingHandler();var drawing=handler.GetActiveDrawing();
            Action identityGuard=()=> {
                var current=handler.GetActiveDrawing();
                if(current==null||drawing==null||!current.IsSameDatabaseObject(drawing))
                    throw new InvalidOperationException("Bản vẽ đã đổi; dừng ghi thuộc tính.");
            };
            identityGuard();
            var fresh=Reader.CaptureGeometry();identityGuard();
            var run=new KeyplanRunResult {Snapshot=fresh,ResetOverrides=request.PreviousIdentity!=null&&request.PreviousIdentity!=fresh.Identity};
            if(fresh.Overview){run.Result=Analyze(fresh,request);return run;}
            bool submitted=false,pending=false;
            QuickResult appliedPlan=null;Angles appliedAngles=null;
            var read=DrawingSettingsReader.WithOwnedDialog((process,dialog,before)=> {
                identityGuard();fresh.Settings=before;run.Result=Analyze(fresh,request);
                if(expected!=null)Validate(expected,fresh,run.Result);
                if(!request.AutoApply)return;
                var result=run.Result;
                if(!result.CanApply||result.State==QuickState.MissingData||result.State==QuickState.NoSolution)
                {run.Note="Chưa tự áp dụng: cần kiểm tra dữ liệu. ";return;}
                if(result.State!=QuickState.VerifiedSuggestion||result.Angles==null||Same(before,result.Angles))
                {run.Note="Thuộc tính Local · X / Y / Z đã đúng. ";return;}
                appliedPlan=result;appliedAngles=result.Angles;
                fresh.Settings=DrawingSettingsReader.EditInDialog(process,dialog,before,result.Angles,identityGuard,
                    request.ConfirmRecreate,()=>submitted=true,value=>pending=value);
                run.Applied=true;run.Note="Đã áp dụng Local · X / Y / Z. ";
            },()=>!pending);
            if(read.Error!=null)throw new InvalidOperationException((submitted?"Đã gửi Modify; hãy kiểm tra kết quả trước khi thử lại. ":"")+read.Error);
            identityGuard();
            if(run.Applied)
            {
                var after=Reader.CaptureGeometry();identityGuard();
                if(after.Identity!=fresh.Identity)throw new InvalidOperationException("Bản vẽ đã đổi trong lúc áp dụng.");
                after.Settings=fresh.Settings;run.Snapshot=after;
                run.Result=Reader.VerifyApplied(fresh,appliedPlan,after,appliedAngles);
            }
            return run;
        }
    }
    // Native DRAWING dialog only. No model object write, no preset save/load,
    // no direct drawing/view Delete/Insert, no work-plane change.
    // A native mandatory recreation requires explicit per-operation confirmation.
    internal static partial class DrawingSettingsReader
    {
        [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr h);
        [DllImport("user32.dll")] static extern int GetDlgCtrlID(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr h);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h,uint message,IntPtr w,IntPtr l);
        static IntPtr ControlFor(IntPtr dialog,string label,string controlClass)
        {
            var fields=Fields(dialog).Where(f=>IsWindowVisible(f.Handle)).ToList();
            var anchor=fields.Single(f=>f.Text.Replace("&","").TrimEnd(':')==label.TrimEnd(':')).Rect;
            return fields.Single(f=>f.Class.Equals(controlClass,StringComparison.OrdinalIgnoreCase)&&f.Rect.Left>=anchor.Right&&
                Math.Abs((f.Rect.Top+f.Rect.Bottom)-(anchor.Top+anchor.Bottom))<=Math.Max(8,anchor.Bottom-anchor.Top)).Handle;
        }
        static IntPtr Native(IntPtr h,uint message,IntPtr w,IntPtr l)
        {
            IntPtr result;
            if(!IsWindow(h)||SendMessageTimeout(h,message,w,l,2,5000,out result)==IntPtr.Zero)
                throw new InvalidOperationException("Tekla chưa phản hồi khi chỉnh thuộc tính. Không tự thử lại.");
            return result;
        }
        static void NotifyControl(IntPtr h,int code)
        { Native(GetParent(h),0x111,new IntPtr((code<<16)|(GetDlgCtrlID(h)&0xffff)),h); }
        static void SelectIndex(IntPtr dialog,string label,int index,int count)
        {
            var h=ControlFor(dialog,label,"ComboBox");
            if(!IsWindowEnabled(h)||Native(h,0x146,IntPtr.Zero,IntPtr.Zero).ToInt32()!=count)
                throw new InvalidOperationException("Danh sách "+label+" khác cấu hình đã kiểm chứng.");
            if(Native(h,0x14E,new IntPtr(index),IntPtr.Zero).ToInt32()!=index)throw new InvalidOperationException("Không chọn được "+label);
            NotifyControl(h,1); // CBN_SELCHANGE
            NotifyControl(h,9); // CBN_SELENDOK: Tekla commits the value and enables its apply/tree flags here.
        }
        static void EnableField(IntPtr dialog,string label)
        {
            var field=Fields(dialog).Single(f=>IsWindowVisible(f.Handle)&&f.Class=="Button"&&f.Text.Replace("&","").TrimEnd(':')==label.TrimEnd(':'));
            if(Native(field.Handle,0xF0,IntPtr.Zero,IntPtr.Zero).ToInt32()!=1)Native(field.Handle,BM_CLICK,IntPtr.Zero,IntPtr.Zero);
            if(Native(field.Handle,0xF0,IntPtr.Zero,IntPtr.Zero).ToInt32()!=1)throw new InvalidOperationException("Chưa bật ô áp dụng "+label);
        }
        internal static bool ApplyAngles(DrawingSettings expected,Angles angles,Action identityGuard,bool previewOnly=false,Func<bool> confirmRecreate=null)
        {
            bool submitted=false,pending=false,requiresRecreate=false;
            var outcome=WithOwnedDialog((process,dialog,before)=> {
                requiresRecreate=FillAngles(dialog,before,expected,angles,identityGuard);
                if(previewOnly)return;
                CommitInDialog(process,dialog,angles,identityGuard,confirmRecreate,requiresRecreate,()=>submitted=true,value=>pending=value);
            },()=>!pending);
            if(outcome.Error!=null)throw new InvalidOperationException((submitted?"Đã gửi Modify; hãy kiểm tra kết quả trước khi thử lại. ":"Chưa ghi thuộc tính. ")+outcome.Error);
            return requiresRecreate;
        }
        internal static DrawingSettings EditInDialog(Process process,IntPtr dialog,DrawingSettings before,Angles angles,Action identityGuard,
            Func<bool> confirmRecreate,Action submitted,Action<bool> pending)
        {
            bool recreate=FillAngles(dialog,before,before,angles,identityGuard);
            return CommitInDialog(process,dialog,angles,identityGuard,confirmRecreate,recreate,submitted,pending);
        }
        static bool FillAngles(IntPtr dialog,DrawingSettings before,DrawingSettings expected,Angles angles,Action identityGuard)
        {
                identityGuard();
                if(before.CoordinateSystem!=expected.CoordinateSystem||before.X!=expected.X||before.Y!=expected.Y||before.Z!=expected.Z)
                    throw new InvalidOperationException("Thuộc tính đã thay đổi trước khi ghi. Hãy kiểm tra lại.");
                // Prefer No whenever Tekla allows it. Some angle edits force Yes
                // and disable this field; never bypass Tekla's disabled control.
                var recreate=ControlFor(dialog,"Recreate the drawing","ComboBox");
                if(ComboText(recreate)!="#0"&&IsWindowEnabled(recreate))SelectIndex(dialog,"Recreate the drawing",0,2);
                string undeformed=Value(Fields(dialog),"Undeformed:","ComboBox");
                foreach(string label in new[]{"Coordinate system:","Around X:","Around Y:","Around Z:"})EnableField(dialog,label);
                SelectIndex(dialog,"Coordinate system:",0,6);
                SelectIndex(dialog,"Around X:",angles.X/90,4);
                SelectIndex(dialog,"Around Y:",angles.Y/180,2);
                var z=ControlFor(dialog,"Around Z:","Edit");IntPtr ignored;
                if(SendMessageTimeout(z,0xC,IntPtr.Zero,new StringBuilder(angles.Z.ToString(CultureInfo.InvariantCulture)),2,3000,out ignored)==IntPtr.Zero)
                    throw new InvalidOperationException("Không nhập được Around Z.");
                NotifyControl(z,0x300); // EN_CHANGE
                NotifyControl(z,0x200); // EN_KILLFOCUS: commit numeric edit to native dialog
                // Tekla itself changes Recreate to Yes after some angle edits.
                // Check AFTER editing; never rely on the initial combo value.
                if(ComboText(recreate)!="#0"&&IsWindowEnabled(recreate))SelectIndex(dialog,"Recreate the drawing",0,2);
                string recreation=ComboText(recreate);
                if(recreation!="#0"&&recreation!="#1")throw new InvalidOperationException("Chưa xác định được chế độ tái tạo bản vẽ.");
                bool requiresRecreate=recreation=="#1";
                var entered=ReadValues(dialog);
                if(!KeyplanApply.Same(entered,angles)||Value(Fields(dialog),"Undeformed:","ComboBox")!=undeformed)
                    throw new InvalidOperationException("Đọc lại nội dung hộp thuộc tính không khớp: "+entered.CoordinateSystem+" / "+entered.X+" / "+entered.Y+" / "+entered.Z+"; Recreate="+ComboText(recreate)+"; Undeformed="+Value(Fields(dialog),"Undeformed:","ComboBox")+" (gốc "+undeformed+"). Chưa ghi vào bản vẽ.");
                identityGuard();
                return requiresRecreate;
        }
        static DrawingSettings CommitInDialog(Process process,IntPtr dialog,Angles angles,Action identityGuard,Func<bool> confirmRecreate,
            bool requiresRecreate,Action submitted,Action<bool> pending)
        {
                bool authorized=requiresRecreate&&confirmRecreate!=null&&confirmRecreate();
                if(requiresRecreate&&!authorized)
                    throw new InvalidOperationException("Đã hủy tự áp dụng: Tekla yêu cầu tái tạo bản vẽ. Các góc gợi ý vẫn có thể kiểm tra lại.");
                identityGuard();
                if(!KeyplanApply.Same(ReadValues(dialog),angles))
                    throw new InvalidOperationException("Thuộc tính thay đổi trong lúc xác nhận. Chưa ghi vào bản vẽ.");
                submitted();
                ClickAndWait(process,dialog,"Modify",identityGuard,confirmRecreate,ref authorized,pending);
                ClickAndWait(process,dialog,"Apply",identityGuard,confirmRecreate,ref authorized,pending);
                // Reload the active drawing INTO THE SAME dialog. This proves
                // saved values, not just text still displayed in edited controls.
                var saved=ReloadFromDrawing(process,dialog,identityGuard);
                if(!KeyplanApply.Same(saved,angles))throw new InvalidOperationException("Tekla chưa lưu đúng Local/X/Y/Z sau Modify. Giá trị lưu: "+saved.CoordinateSystem+" / "+saved.X+" / "+saved.Y+" / "+saved.Z);
                ClickAndWait(process,dialog,"OK",identityGuard,confirmRecreate,ref authorized,pending);
                if(IsWindow(dialog)&&IsWindowVisible(dialog))throw new InvalidOperationException("Đã lưu thuộc tính nhưng chưa đóng được hộp thoại bằng OK.");
                return saved;
        }
        static void ClickAndWait(Process process,IntPtr dialog,string label,Action guard,Func<bool> confirm,ref bool authorized,Action<bool> pending)
        {
            guard();
            var button=Fields(dialog).Single(f=>f.Class=="Button"&&f.Text.Replace("&","")==label&&IsWindowVisible(f.Handle));
            if(!IsWindowEnabled(button.Handle))throw new InvalidOperationException("Nút "+label+" chưa sẵn sàng.");
            IntPtr parent=GetParent(button.Handle),id=new IntPtr(GetDlgCtrlID(button.Handle)&0xffff);
            Exception error=null;bool denied=false;IntPtr answered=IntPtr.Zero;
            var worker=new Thread(()=> {
                try {IntPtr result;if(SendMessageTimeout(parent,0x111,id,button.Handle,2,60000,out result)==IntPtr.Zero)throw new InvalidOperationException("Tekla chưa hoàn tất "+label+". Không tự gửi lại lệnh.");}
                catch(Exception ex) {error=ex;}
            }) {IsBackground=true};
            pending(true);worker.Start();
            var timer=Stopwatch.StartNew();
            while(worker.IsAlive&&timer.ElapsedMilliseconds<61000)
            {
                IntPtr prompt=IntPtr.Zero;
                EnumWindows((h,p)=> {uint pid;GetWindowThreadProcessId(h,out pid);if(pid==process.Id&&IsWindowVisible(h)&&Text(h)=="View creation settings changed.")prompt=h;return true;},IntPtr.Zero);
                if(prompt!=IntPtr.Zero&&prompt!=answered)
                {
                    var fields=Fields(prompt);
                    if(!fields.Any(f=>f.Text.Contains("Drawing will be re-created.")&&f.Text.Contains("manual editing")))
                        throw new InvalidOperationException("Hộp xác nhận Tekla khác nội dung đã kiểm chứng. Hãy kiểm tra cửa sổ Tekla.");
                    guard();if(!authorized)authorized=confirm!=null&&confirm();
                    string answer=authorized?"Yes":"No";denied=!authorized;
                    var accept=fields.Single(f=>f.Class=="Button"&&f.Text.Replace("&","")==answer&&IsWindowVisible(f.Handle));
                    if(!PostMessage(accept.Handle,BM_CLICK,IntPtr.Zero,IntPtr.Zero))throw new InvalidOperationException("Không gửi được xác nhận Tekla.");
                    answered=prompt;
                }
                worker.Join(50);
            }
            if(worker.IsAlive)throw new InvalidOperationException("Tekla vẫn đang xử lý "+label+"; chưa gửi lại lệnh.");
            worker.Join();
            if(error!=null)throw error;
            pending(false);
            if(denied)throw new InvalidOperationException("Đã hủy tái tạo bản vẽ.");
            guard();
        }
        internal static string InspectApplyControls()
        {
            string report="";
            var outcome=WithOwnedDialog((p,d,b)=> {
                report="Original="+b.CoordinateSystem+" "+b.X+"/"+b.Y+"/"+b.Z+"\n";
                foreach(var f in Fields(d).Where(f=>IsWindowVisible(f.Handle)))
                    report+=f.Class+" id="+GetDlgCtrlID(f.Handle)+" text="+f.Text+(f.Class=="ComboBox"?" selected="+ComboText(f.Handle)+" count="+Native(f.Handle,0x146,IntPtr.Zero,IntPtr.Zero):"")+"\n";
            });
            if(outcome.Error!=null)throw new InvalidOperationException(outcome.Error);return report;
        }
        static DrawingSettings ReloadFromDrawing(Process process,IntPtr dialog,Action guard)
        {
            guard();
            Akit(process,(t,s)=>t.GetMethod("Callback").Invoke(s,new object[]{"acmd_display_selected_drawing_object_dialog","","main_frame"}));
            var dialogs=Dialogs(process.Id);
            if(dialogs.Count!=1||dialogs[0]!=dialog)throw new InvalidOperationException("Tekla đổi hộp thuộc tính trong lúc xác minh.");
            NavigateAttributes(process,dialog);
            guard();return ReadValues(dialog);
        }
        internal static void VerifyReload()
        {
            var outcome=WithOwnedDialog((p,d,b)=> {
                SelectIndex(d,"Around X:",b.X==0?1:0,4);
                if(ReadValues(d).X==b.X)throw new InvalidOperationException("Test edit did not change X");
                var actual=ReloadFromDrawing(p,d,()=>{});
                if(actual.X!=b.X||actual.Y!=b.Y||actual.Z!=b.Z||actual.CoordinateSystem!=b.CoordinateSystem)
                    throw new InvalidOperationException("Reload does not load drawing values: "+actual.CoordinateSystem+" / "+actual.X+" / "+actual.Y+" / "+actual.Z);
            });
            if(outcome.Error!=null)throw new InvalidOperationException(outcome.Error);
        }
    }
}
#endregion

#region Lưu thiết lập người dùng
namespace TTSK_AutoDim_Plates.Keyplan
{


using System;
using System.IO;
using System.Text;

    // User preference belongs in LocalAppData, never in the Tekla model folder.
    // Only an explicit user toggle writes this file. Errors/drawing changes do not.
    internal sealed class KeyplanPreferences
    {
        readonly string path;
        internal static string DefaultPath {get {return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TTSK","Keyplan","keyplan.cfg");}}
        internal KeyplanPreferences(string path) {this.path=Path.GetFullPath(path);}
        internal bool Load()
        {
            if(!File.Exists(path))return false;
            string value=File.ReadAllText(path).Trim();
            if(value=="auto_apply=1")return true;
            if(value=="auto_apply=0")return false;
            throw new InvalidDataException("Cấu hình keyplan không hợp lệ.");
        }
        internal void Save(bool enabled)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                File.WriteAllText(temporary,enabled?"auto_apply=1\r\n":"auto_apply=0\r\n",new UTF8Encoding(false));
                if(File.Exists(path))File.Replace(temporary,path,null);
                else File.Move(temporary,path);
            }
            finally {if(File.Exists(temporary))File.Delete(temporary);}
        }
    }
}
#endregion
#endregion

#region PHU_KeyplanForm - Giao diện kiểm tra hướng và áp dụng keyplan


namespace TTSK_AutoDim_Plates.Keyplan
{
    using System;
    using System.Drawing;
    using System.Drawing.Drawing2D;
    using System.Globalization;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Threading.Tasks;
    using System.Windows.Forms;

    internal sealed class KeyplanForm : Form
    {
        readonly Func<KeyplanRunRequest,Task<KeyplanRunResult>> run;
        readonly Action<bool> saveAutoApply;
        readonly SlideToggle applyToggle;
        readonly Palette palette;
        readonly Label drawing, status, currentViewStatus, angleTitle;
        readonly CoordinateBadge calibration;
        readonly ReferenceCard referenceFirst, referenceSecond;
        readonly TableLayoutPanel referencesPanel, stack;
        readonly AngleCard[] cards;
        readonly TableLayoutPanel values;
        readonly OutcomeBadge outcome;
        readonly PaintedButton beam, column, x, y, auto, autoModeButton, refresh, copy, settings, gridButton, rotate;
        readonly Panel options;
        readonly ToolTip tips=new ToolTip();
        Snapshot snapshot;
        Angles result;
        QuickState? displayedState;
        bool busy, isColumn, alongY, savedAutoApply, restoringPreference;
        bool autoDirection=true;
        bool autoMode=true;
        int gridId, quarterTurns;
        string identity;
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h,int msg,IntPtr w,IntPtr l);

        internal KeyplanForm(Func<KeyplanRunRequest,Task<KeyplanRunResult>> run,bool dark,bool autoApply=false,Action<bool> saveAutoApply=null)
        {
            this.run=run;this.saveAutoApply=saveAutoApply;savedAutoApply=autoApply;palette=new Palette(dark);
            Text="TTSK · Keyplan";Name="KeyplanForm";ClientSize=new Size(660,590);
            FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.CenterParent;
            ShowInTaskbar=false;AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);
            BackColor=palette.Border;ForeColor=palette.Ink;Padding=new Padding(1);
            Font=new Font("Segoe UI",10);DoubleBuffered=true;KeyPreview=true;

            var chrome=new Panel { Dock=DockStyle.Top,Height=38,BackColor=palette.Bar };
            chrome.Paint+=(s,e)=> { using(var pen=new Pen(palette.Border))e.Graphics.DrawLine(pen,0,chrome.Height-1,chrome.Width,chrome.Height-1); };
            var caption=TextLabel("TTSK    /    SLOT 09",9,palette.BarInk);caption.Padding=new Padding(20,0,0,0);
            MouseEventHandler drag=(s,e)=> { if(e.Button==MouseButtons.Left) { ReleaseCapture();SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero); } };
            caption.MouseDown+=drag;chrome.MouseDown+=drag;
            var close=Button("×");close.AccessibleName="Đóng";close.Dock=DockStyle.Right;close.Width=42;close.Chrome=true;
            close.Click+=delegate { Close(); };chrome.Controls.Add(caption);chrome.Controls.Add(close);

            var footer=new Panel { Dock=DockStyle.Bottom,Height=60,BackColor=palette.Bar,Padding=new Padding(18,12,18,12) };
            footer.Paint+=(s,e)=> { using(var pen=new Pen(palette.Border))e.Graphics.DrawLine(pen,0,0,footer.Width,0); };
            settings=Button("Hiệu chỉnh");settings.Name="Settings";settings.Chrome=true;settings.Dock=DockStyle.Left;settings.Width=116;
            copy=Button("Sao chép");copy.Name="Copy";copy.Chrome=true;copy.Dock=DockStyle.Right;copy.Width=104;copy.Enabled=false;
            refresh=Button("Kiểm tra lại");refresh.Name="Refresh";refresh.Selected=true;refresh.Primary=true;refresh.Dock=DockStyle.Right;refresh.Width=132;
            var gap=new Panel { Dock=DockStyle.Right,Width=10 };
            footer.Controls.Add(settings);footer.Controls.Add(copy);footer.Controls.Add(gap);footer.Controls.Add(refresh);

            var body=new Panel { Dock=DockStyle.Fill,BackColor=palette.Background,Padding=new Padding(24,18,24,16) };
            stack=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=8,Margin=Padding.Empty,AutoScroll=true };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            foreach(int height in new[]{32,48,44,40,34,154,90,16})stack.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
            var heading=new Panel { Dock=DockStyle.Fill,Margin=Padding.Empty };
            heading.Controls.Add(TextLabel("KEYPLAN",16,palette.Accent,true));
            drawing=TextLabel("Đang đọc bản vẽ…",10,palette.Muted);drawing.Dock=DockStyle.Right;drawing.Width=250;drawing.TextAlign=ContentAlignment.MiddleRight;
            heading.Controls.Add(drawing);stack.Controls.Add(heading,0,0);
            var choices=new FlowLayoutPanel { Dock=DockStyle.Fill,WrapContents=false,Padding=new Padding(0,7,0,7),Margin=Padding.Empty };
            beam=Button("Dầm");column=Button("Cột");x=Button("X");y=Button("Y");
            autoModeButton=Button("Tự nhận");autoModeButton.Name="ModeAuto";autoModeButton.Size=new Size(88,36);autoModeButton.Margin=new Padding(0,0,5,0);
            auto=Button("Tự động");auto.Name="DirectionAuto";auto.Size=new Size(88,36);auto.Margin=new Padding(0,0,5,0);
            beam.Name="Beam";column.Name="Column";x.Name="DirectionX";y.Name="DirectionY";
            foreach(var b in new[]{beam,column,x,y}) { b.Size=new Size(76,36);b.Margin=new Padding(0,0,5,0); }
            var separator=TextLabel("Hướng",9,palette.Muted);separator.Dock=DockStyle.None;separator.Size=new Size(84,36);separator.TextAlign=ContentAlignment.MiddleLeft;
            var modes=new FlowLayoutPanel { Dock=DockStyle.Fill,WrapContents=false,Padding=new Padding(0,7,0,7),Margin=Padding.Empty };
            var modeLabel=TextLabel("Phương án",9,palette.Muted);modeLabel.Dock=DockStyle.None;modeLabel.Size=new Size(84,36);modeLabel.TextAlign=ContentAlignment.MiddleLeft;
            modes.Controls.AddRange(new Control[]{modeLabel,autoModeButton,beam,column});
            applyToggle=new SlideToggle(palette) {Name="AutoApply",Text="Tự áp dụng",Size=new Size(216,36),Margin=new Padding(24,0,0,0),Checked=autoApply};
            modes.Controls.Add(applyToggle);
            tips.SetToolTip(applyToggle,"Ghi nhớ bật/tắt cho những lần mở Slot 09 sau, kể cả khi đóng chương trình. Khi bật, tự áp dụng Local và X/Y/Z, tự xác nhận Yes nếu Tekla cần tái tạo bản vẽ.");
            var angleHeading=new Panel {Dock=DockStyle.Fill,Margin=new Padding(0,4,0,4)};
            angleTitle=TextLabel("GÓC XOAY",9,palette.Muted,true);angleTitle.Name="AngleTitle";angleHeading.Controls.Add(angleTitle);
            calibration=new CoordinateBadge(palette) {Dock=DockStyle.Right,Width=250,Name="CoordinateBadge",Text="Đang đọc…"};
            angleHeading.Controls.Add(calibration);stack.Controls.Add(angleHeading,0,4);
            tips.SetToolTip(calibration,"Drawing properties → View creation → Attributes → Coordinate system.");
            choices.Controls.AddRange(new Control[]{separator,auto,x,y});
            stack.Controls.Add(modes,0,1);stack.Controls.Add(choices,0,2);
            currentViewStatus=TextLabel("View hiện tại: Chưa xác minh",11,palette.Muted,true);
            currentViewStatus.Name="CurrentViewStatus";stack.Controls.Add(currentViewStatus,0,3);
            values=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty,Visible=false };
            cards=new[]{new AngleCard("X",palette),new AngleCard("Y",palette),new AngleCard("Z",palette)};
            for(int i=0;i<3;i++) { values.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100F/3));cards[i].Dock=DockStyle.Fill;cards[i].Margin=new Padding(i==0?0:5,0,i==2?0:5,0);values.Controls.Add(cards[i],i,0); }
            var resultHost=new Panel { Dock=DockStyle.Fill,Margin=Padding.Empty,BackColor=palette.Background };
            outcome=new OutcomeBadge(palette) { Dock=DockStyle.Fill,Name="Outcome",Text="Đang kiểm tra…" };
            resultHost.Controls.Add(values);resultHost.Controls.Add(outcome);stack.Controls.Add(resultHost,0,5);
            referencesPanel=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=new Padding(0,14,0,0),Visible=false};
            referencesPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));referencesPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            referenceFirst=new ReferenceCard(palette) {Dock=DockStyle.Fill,Margin=new Padding(0,0,5,0),Name="ReferenceFirst"};
            referenceSecond=new ReferenceCard(palette) {Dock=DockStyle.Fill,Margin=new Padding(5,0,0,0),Name="ReferenceSecond"};
            referencesPanel.Controls.Add(referenceFirst,0,0);referencesPanel.Controls.Add(referenceSecond,1,0);stack.Controls.Add(referencesPanel,0,6);
            status=TextLabel("",9,palette.Amber);status.Name="ResultStatus";status.Padding=new Padding(0,4,0,0);status.TextAlign=ContentAlignment.MiddleCenter;stack.Controls.Add(status,0,7);body.Controls.Add(stack);

            options=new Panel { Dock=DockStyle.Bottom,Height=92,Visible=false,BackColor=palette.Surface,Padding=new Padding(24,10,24,10) };
            var rows=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=2 };
            foreach(int h in new[]{28,40})rows.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            rows.Controls.Add(TextLabel("Chuẩn keyplan · chiều trục hiển thị dưới kết quả",9,palette.Muted),0,0);
            var references=new FlowLayoutPanel { Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty };
            gridButton=Button("Lưới: tự động");gridButton.Size=new Size(292,32);
            rotate=Button("Xoay chuẩn keyplan: 0°");rotate.Size=new Size(270,32);
            references.Controls.AddRange(new Control[]{gridButton,rotate});rows.Controls.Add(references,0,1);options.Controls.Add(rows);
            Controls.Add(body);Controls.Add(options);Controls.Add(footer);Controls.Add(chrome);

            beam.Click+=async delegate { autoMode=false;isColumn=false;await CheckNow(); };column.Click+=async delegate { autoMode=false;isColumn=true;await CheckNow(); };
            autoModeButton.Click+=async delegate { autoMode=true;autoDirection=true;await CheckNow(); };
            x.Click+=async delegate { autoDirection=false;alongY=false;await CheckNow(); };y.Click+=async delegate { autoDirection=false;alongY=true;await CheckNow(); };
            auto.Click+=async delegate { autoDirection=true;await CheckNow(); };
            refresh.Click+=async delegate { await CheckNow(); };
            applyToggle.CheckedChanged+=async delegate {
                if(restoringPreference)return;
                try {if(saveAutoApply!=null)saveAutoApply(applyToggle.Checked);savedAutoApply=applyToggle.Checked;}
                catch(Exception ex) {restoringPreference=true;applyToggle.Checked=savedAutoApply;restoringPreference=false;SetStatus("Chưa lưu được Tự áp dụng: "+ex.Message);return;}
                if(applyToggle.Checked&&!busy)await CheckNow();
            };
            FormClosing+=(s,e)=> { if(busy)e.Cancel=true; };
            settings.Click+=delegate { bool show=!options.Visible;options.Visible=show;ResizeForContent();settings.Text=show?"Thu gọn":"Hiệu chỉnh"; };
            rotate.Click+=async delegate { quarterTurns=(quarterTurns+1)%4;rotate.Text="Xoay chuẩn keyplan: "+quarterTurns*90+"°";await CheckNow(); };
            gridButton.Click+=delegate
            {
                if(snapshot==null)return;
                var menu=new ContextMenuStrip();
                var automatic=new ToolStripMenuItem("Tự động chọn lưới") {Checked=gridId==0};
                automatic.Click+=async delegate { gridId=0;await CheckNow(); };menu.Items.Add(automatic);
                foreach(var candidate in snapshot.Grids.OrderBy(g=>g.Id))
                {
                    var selected=candidate;
                    var item=new ToolStripMenuItem(candidate.ToString()) {Checked=gridId==candidate.Id,Enabled=candidate.Error==null,ToolTipText=candidate.Error};
                    item.Click+=async delegate { gridId=selected.Id;await CheckNow(); };menu.Items.Add(item);
                }
                menu.Closed+=delegate { menu.Dispose(); };menu.Show(gridButton,new Point(0,gridButton.Height));
            };
            copy.Click+=delegate { if(result!=null)try { Clipboard.SetText(result.ToString());copy.Text="Đã chép ✓"; } catch(Exception) { SetStatus("Chưa sao chép được. Hãy thử lại."); } };
            Shown+=async delegate { await CheckNow(); };
            KeyDown+=async (s,e)=> { if(e.KeyCode==Keys.Escape)Close();else if(e.KeyCode==Keys.F5&&!busy) { e.Handled=true;await CheckNow(); } };
            UpdateModes();
        }
        Label TextLabel(string text,float size,Color color,bool bold=false)
        { return new Label { Text=text,Dock=DockStyle.Fill,AutoEllipsis=true,ForeColor=color,BackColor=Color.Transparent,TextAlign=ContentAlignment.MiddleLeft,Margin=Padding.Empty,Font=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular) }; }
        PaintedButton Button(string text) { return new PaintedButton(palette) { Text=text,Font=new Font("Segoe UI",9F,FontStyle.Bold) }; }
        void UpdateModes()
        {
            beam.Selected=!isColumn&&(!autoMode||displayedState.HasValue);column.Selected=isColumn&&(!autoMode||displayedState.HasValue);
            autoModeButton.Selected=autoMode;autoModeButton.Enabled=!busy;
            x.Selected=!isColumn&&!alongY&&(!autoDirection||displayedState.HasValue);
            y.Selected=!isColumn&&alongY&&(!autoDirection||displayedState.HasValue);
            x.Enabled=y.Enabled=!isColumn&&!busy;
            auto.Enabled=!isColumn&&!busy;auto.Selected=!isColumn&&autoDirection;
            x.Text=isColumn?"Trái X":"X";y.Text=isColumn?"Phải Y":"Y";
            foreach(var b in new[]{beam,column,auto,autoModeButton,x,y})b.Invalidate();
        }
        void ClearResult()
        {
            result=null;displayedState=null;copy.Enabled=false;copy.Visible=false;values.Visible=false;
            copy.Text="Sao chép";referencesPanel.Visible=false;
            calibration.Text="Chưa xác minh";calibration.NeedsChange=false;
            currentViewStatus.Text="View hiện tại: Chưa xác minh";currentViewStatus.ForeColor=palette.Muted;
            currentViewStatus.AccessibleDescription="";tips.SetToolTip(currentViewStatus,null);angleTitle.Text="GÓC XOAY";
            outcome.Correct=false;outcome.Visible=true;outcome.Text="Đang kiểm tra…";foreach(var c in cards)c.Value="—";
        }
        async Task CheckNow()
        {
            if(busy)return;busy=true;ClearResult();SetStatus("");
            applyToggle.Enabled=false;
            foreach(var b in new[]{beam,column,refresh,gridButton,rotate})b.Enabled=false;UpdateModes();
            try
            {
                if(applyToggle.Checked)outcome.Text="Đang kiểm tra và áp dụng…";
                var checkedRun=await run(new KeyplanRunRequest {PreviousIdentity=identity,Column=autoMode?(bool?)null:isColumn,
                    AlongY=autoDirection?(bool?)null:alongY,GridId=gridId,QuarterTurns=quarterTurns,AutoApply=applyToggle.Checked,
                    ConfirmRecreate=ConfirmRecreation});
                if(IsDisposed)return;
                if(checkedRun==null||checkedRun.Snapshot==null||checkedRun.Result==null)throw new InvalidOperationException("Chưa nhận được kết quả kiểm tra từ Tekla.");
                var fresh=checkedRun.Snapshot;
                if(checkedRun.ResetOverrides) { gridId=quarterTurns=0;autoMode=autoDirection=true;rotate.Text="Xoay chuẩn keyplan: 0°"; }
                identity=fresh.Identity;
                var output=checkedRun.Result;
                Render(fresh,output);
                if(checkedRun.Note!=null) {SetStatus((checkedRun.Applied?"Đã áp dụng. ":"")+CompactNote(output),checkedRun.Note+"\n"+output.Message);if(output.State==QuickState.Correct)status.ForeColor=palette.Green;}
            }
            catch(Exception ex) { if(!IsDisposed) { ClearResult();outcome.Text="Chưa thể kết luận";SetStatus(ex.Message); } }
            finally
            {
                busy=false;if(!IsDisposed) { applyToggle.Enabled=true;foreach(var b in new[]{beam,column,refresh,gridButton,rotate})b.Enabled=true;UpdateModes(); }
            }
        }
        bool ConfirmRecreation()
        {
            if(IsDisposed)return false;
            if(InvokeRequired)return (bool)Invoke(new Func<bool>(ConfirmRecreation));
            // The user explicitly authorized automatic Yes for Slot 09.
            // Consent is scoped to the active auto-apply operation only.
            return busy&&applyToggle.Checked;
        }
        void Render(Snapshot fresh,QuickResult output)
        {
                snapshot=fresh;drawing.Text=fresh.Mark;tips.SetToolTip(drawing,fresh.Mark);
                calibration.Text=fresh.Settings==null||fresh.Settings.Error!=null?"Chưa xác minh":"Hệ tọa độ · "+fresh.Settings.CoordinateSystem;
                calibration.NeedsChange=false;
                RenderCurrentViewStatus(fresh,output);
                var grid=fresh.Grids.SingleOrDefault(g=>g.Id==gridId);gridButton.Text=grid==null?"Lưới: tự động":"Lưới: "+grid.Name+" · "+grid.Id;
                isColumn=output.IsColumn;
                if(!isColumn)alongY=output.AlongY;
                grid=fresh.Grids.Single(g=>g.Id==output.GridId);
                gridButton.Text=(gridId==0?"Tự động: ":"Lưới: ")+grid.Name+" · "+grid.Id;
                tips.SetToolTip(gridButton,output.SelectionNote+". Bấm để chọn lưới khác hoặc trở về tự động.");
                tips.SetToolTip(auto,output.SelectionNote);
                tips.SetToolTip(autoModeButton,output.SelectionNote);
                displayedState=output.State;
                // The UI never promotes a mathematical candidate to a verified recommendation.
                result=output.CanApply&&(output.State==QuickState.VerifiedSuggestion||output.State==QuickState.IncompleteViews)?output.Angles:null;
                string firstType=output.FirstViewType==null?"":" · "+output.FirstViewType.Replace("View","").ToUpperInvariant();
                string secondType=output.SecondViewType==null?"":" · "+output.SecondViewType.Replace("View","").ToUpperInvariant();
                if(isColumn&&output.FirstViewType!=null&&!fresh.Views.Any(v=>v.Type==output.FirstViewType))firstType=" · CHƯA CÓ VIEW";
                if(isColumn&&output.SecondViewType!=null&&!fresh.Views.Any(v=>v.Type==output.SecondViewType))secondType=" · CHƯA CÓ VIEW";
                referenceFirst.Set(output.ReferenceFirstCaption??(fresh.Overview?"KEYPLAN · X":isColumn?"VIEW TRÁI"+firstType:"TOP · TRỤC ĐỨNG"),output.ReferenceFirst,isColumn?"X":"→");
                referenceSecond.Set(output.ReferenceSecondCaption??(fresh.Overview?"KEYPLAN · Y":isColumn?"VIEW PHẢI"+secondType:"TOP · TRỤC NGANG"),output.ReferenceSecond,isColumn?"Y":"↑");
                referencesPanel.Visible=true;
                tips.SetToolTip(referenceFirst,output.Reference+"\n"+output.SelectionNote);tips.SetToolTip(referenceSecond,output.Reference+"\n"+output.SelectionNote);
                outcome.Correct=output.State==QuickState.Correct;
                outcome.Text=outcome.Correct?"Góc view chính đã đúng":output.State==QuickState.Overview?"Chưa có góc chung cho view chính":output.State==QuickState.LayoutMismatch?"Cần sắp lại view chính":output.State==QuickState.IncompleteViews?"Góc theo view chính hiện có":output.State==QuickState.NoSolution?"Không có bộ góc phù hợp":"Chưa thể xuất góc quay";
                SetStatus(outcome.Correct?"":CompactNote(output),output.Message+"\n"+output.ActionNote);
                tips.SetToolTip(calibration,"Hệ tọa độ hiện đang lưu trong bản vẽ; độc lập với kết quả đối chiếu hướng view.\nDrawing properties → View creation → Attributes → Coordinate system.");
                angleTitle.Text=result==null?"GÓC VIEW CHÍNH":"GÓC VIEW CHÍNH · LOCAL";
                if(result!=null)
                {
                    double[] numbers={result.X,result.Y,result.Z};
                    for(int i=0;i<3;i++)cards[i].Value=numbers[i].ToString("0.###",CultureInfo.InvariantCulture)+"°";
                    copy.Enabled=copy.Visible=true;values.Visible=true;outcome.Visible=false;
                }
        }
        void RenderCurrentViewStatus(Snapshot fresh,QuickResult output)
        {
            var checks=output.ViewChecks;
            var wrong=checks.Where(c=>c.State==ViewAxisState.Misaligned).ToList();
            var unknown=checks.Where(c=>c.State==ViewAxisState.Unverified).ToList();
            bool correct=checks.Count>0&&wrong.Count==0&&unknown.Count==0;
            // Một dòng kết luận; chỉ nêu view cần xử lý, không tạo bảng làm dài form.
            currentViewStatus.Text=correct?"✓ Đúng trục theo keyplan":checks.Count==0?"Chưa có view để kiểm":
                (wrong.Count>0?"Sai trục: "+ViewIssueNames(fresh,wrong):"")+
                (unknown.Count>0?(wrong.Count>0?" · ":"")+"Chưa xác minh: "+ViewIssueNames(fresh,unknown):"");
            currentViewStatus.ForeColor=correct?palette.Green:palette.Amber;
            string details=correct?"Đã kiểm toàn bộ "+checks.Count+" view, gồm Section/Detail.":
                string.Join("\n",checks.Where(c=>c.State!=ViewAxisState.Correct).Select(c=>c.Label+": "+c.Message));
            currentViewStatus.AccessibleDescription=details;tips.SetToolTip(currentViewStatus,details);
        }
        static string ViewIssueNames(Snapshot snapshot,System.Collections.Generic.List<ViewAxisCheck> issues)
        {
            var views=snapshot.AllViews.Count>0?snapshot.AllViews:snapshot.Views;
            var names=issues.Take(2).Select(check=>
            {
                string name=check.Type.Replace("View","")+(string.IsNullOrWhiteSpace(check.Name)?"":" "+check.Name.Trim());
                var same=views.Where(v=>v.Type==check.Type&&v.Name==check.Name).ToList();
                var current=same.FirstOrDefault(v=>v.Id==check.Id);
                if(same.Count==2&&current!=null&&same.All(v=>Geometry.Finite(v.SheetX)&&Geometry.Finite(v.SheetY)))
                {
                    var other=same.FirstOrDefault(v=>v.Id!=check.Id);
                    if(other!=null)
                    {
                        double dx=current.SheetX-other.SheetX,dy=current.SheetY-other.SheetY;
                        if(Math.Max(Math.Abs(dx),Math.Abs(dy))>0.1)
                            return name+(Math.Abs(dx)>Math.Abs(dy)?(dx<0?" (trái)":" (phải)"):(dy>0?" (trên)":" (dưới)"));
                    }
                }
                return name;
            });
            return string.Join(", ",names)+(issues.Count>2?" + "+(issues.Count-2)+" view":"");
        }
        void ResizeForContent()
        {
            // Giữ form trong màn hình khi mở Hiệu chỉnh hoặc có cảnh báo dài.
            int height=(int)Math.Ceiling(stack.RowStyles.Cast<RowStyle>().Sum(r=>r.Height))+stack.Parent.Padding.Vertical+Padding.Vertical;
            foreach(Control control in Controls)if(control.Visible&&(control.Dock==DockStyle.Top||control.Dock==DockStyle.Bottom))height+=control.Height;
            ClientSize=new Size(ClientSize.Width,Math.Min(height,Math.Max(400,Screen.FromControl(this).WorkingArea.Height-24)));
        }
        static string CompactNote(QuickResult output)
        {
            if(!string.IsNullOrWhiteSpace(output.BriefNote))return output.BriefNote;
            string first=(output.FirstViewType??"View X").Replace("View","");
            string second=(output.SecondViewType??"View Y").Replace("View","");
            switch(output.State)
            {
                case QuickState.Correct:return "Các view đúng keyplan.";
                case QuickState.LayoutMismatch:return output.IsColumn?"Đặt "+first+" bên trái, "+second+" bên phải.":"Đặt "+second+" trên "+first+".";
                case QuickState.IncompleteViews:return "Góc theo view chính hiện có; chưa xác nhận đủ cặp "+first+" / "+second+".";
                case QuickState.NoSolution:return "Không tìm được góc phù hợp với các view hiện có.";
                case QuickState.VerifiedSuggestion:
                    if(!output.CompleteViews)return "Góc đề xuất theo view chính; chưa xác nhận đủ cặp "+first+" / "+second+".";
                    return output.ActionNote??"";
                default:return output.Message??"";
            }
        }
        void SetStatus(string message,string details=null)
        {
            string full=(details??message??"").Trim();string brief=(message??"").Trim();
            if(brief.Length>220)brief=brief.Substring(0,217).TrimEnd()+"…";
            status.Text=brief;status.ForeColor=palette.Amber;tips.SetToolTip(status,full);status.AccessibleDescription=full;
            float scale=DeviceDpi/96F;
            float height=status.Text.Length==0?16*scale:Math.Max(44*scale,TextRenderer.MeasureText(status.Text,status.Font,new Size(Math.Max(150,status.Width-8),int.MaxValue),TextFormatFlags.WordBreak).Height+14*scale);
            int row=stack.GetRow(status);
            stack.RowStyles[row].Height=height;
            ResizeForContent();
        }
        protected override void Dispose(bool disposing) { if(disposing)tips.Dispose();base.Dispose(disposing); }

        sealed class SlideToggle : CheckBox
        {
            readonly Palette p;readonly Timer animation=new Timer {Interval=16};float position;
            internal SlideToggle(Palette p)
            {
                this.p=p;Appearance=Appearance.Button;FlatStyle=FlatStyle.Flat;Cursor=Cursors.Hand;
                AccessibleName="Tự áp dụng Local và góc xoay vào bản vẽ";
                SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
                animation.Tick+=(s,e)=> {float target=Checked?1:0;position+=(target-position)*0.35F;if(Math.Abs(position-target)<0.02F){position=target;animation.Stop();}Invalidate();};
            }
            protected override void OnCheckedChanged(EventArgs e) {animation.Start();Invalidate();base.OnCheckedChanged(e);}
            protected override void OnEnabledChanged(EventArgs e) {Invalidate();base.OnEnabledChanged(e);}
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;float scale=DeviceDpi/96F;
                int w=(int)(42*scale),h=(int)(24*scale);var track=new Rectangle(Width-w-2,(Height-h)/2,w,h);
                Color ink=Enabled?p.ButtonInk:p.Muted,fill=Checked?p.PrimaryBack:p.Border;
                using(var path=Rounded(track,h/2))using(var brush=new SolidBrush(fill))e.Graphics.FillPath(brush,path);
                float d=h-6*scale,left=track.Left+3*scale+position*(w-h);
                using(var brush=new SolidBrush(Color.White))e.Graphics.FillEllipse(brush,left,track.Top+3*scale,d,d);
                using(var font=new Font("Segoe UI",9F,FontStyle.Bold))TextRenderer.DrawText(e.Graphics,Text,font,new Rectangle(0,0,track.Left-(int)(10*scale),Height),ink,TextFormatFlags.Right|TextFormatFlags.VerticalCenter);
                if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,ClientRectangle,ink,Parent.BackColor);
            }
            protected override void Dispose(bool disposing) {if(disposing)animation.Dispose();base.Dispose(disposing);}
        }

        // Ordinary buttons follow MainForm.ApplyMainPresentationTheme gradients.
        // Light selections use a soft blue surface; only the main action is filled.
        sealed class Palette
        {
            internal readonly Color Background,Surface,Ink,Muted,Border,Accent,SelectedInk,Bar,BarInk,Green,Amber;
            internal readonly Color ButtonBack,ButtonEnd,ButtonBorder,ButtonInk,ButtonHoverBorder,BadgeBack;
            internal readonly Color SelectionBack,SelectionEnd,SelectionBorder,SelectionInk,PrimaryBack,PrimaryEnd;
            internal readonly Color ReferenceInk,ReferenceCaption,ReferenceSymbol;
            internal Palette(bool dark)
            {
                Background=dark?Color.FromArgb(18,18,18):Color.FromArgb(248,250,252);
                Surface=dark?Color.FromArgb(30,32,36):Color.White;
                Ink=dark?Color.FromArgb(235,235,235):Color.FromArgb(15,23,42);
                Muted=dark?Color.FromArgb(170,165,158):Color.FromArgb(100,116,139);
                Border=dark?Color.FromArgb(70,75,84):Color.FromArgb(208,220,236);
                Accent=dark?Color.FromArgb(201,122,64):Color.FromArgb(62,104,169);
                ButtonBack=dark?Color.FromArgb(29,34,42):Color.White;
                ButtonEnd=dark?Color.FromArgb(22,26,33):Color.FromArgb(239,244,251);
                ButtonBorder=dark?Color.FromArgb(137,96,65):Color.FromArgb(150,174,210);
                ButtonInk=dark?Color.FromArgb(235,173,119):Color.FromArgb(30,58,138);
                ButtonHoverBorder=Accent;
                SelectionBack=dark?Color.FromArgb(215,141,80):Color.FromArgb(230,240,253);
                SelectionEnd=dark?Accent:Color.FromArgb(211,228,248);
                SelectionBorder=dark?Accent:Color.FromArgb(116,151,197);
                SelectionInk=dark?Color.FromArgb(20,16,14):Color.FromArgb(42,76,126);
                PrimaryBack=dark?SelectionBack:ButtonInk;
                PrimaryEnd=dark?Accent:ButtonInk;
                ReferenceInk=Ink;
                ReferenceCaption=Muted;
                ReferenceSymbol=dark?Accent:ButtonInk;
                BadgeBack=dark?Color.FromArgb(30,24,20):Color.FromArgb(235,243,253);
                SelectedInk=dark?Color.FromArgb(20,16,14):Color.White;
                Bar=Surface;
                BarInk=Ink;
                Green=dark?Color.FromArgb(111,218,177):Color.FromArgb(22,124,94);
                Amber=dark?Color.FromArgb(255,191,112):Color.FromArgb(166,88,16);
            }
        }
        sealed class CoordinateBadge : Control
        {
            readonly Palette p;bool needsChange;
            internal bool NeedsChange {get {return needsChange;}set {needsChange=value;Invalidate();}}
            internal CoordinateBadge(Palette p) {this.p=p;DoubleBuffered=true;ResizeRedraw=true;AccessibleRole=AccessibleRole.StaticText;}
            protected override void OnTextChanged(EventArgs e) {base.OnTextChanged(e);AccessibleName=Text;Invalidate();}
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
                float scale=DeviceDpi/96F;
                using(var font=new Font("Segoe UI",9,FontStyle.Bold))
                {
                    int width=Math.Min(Width-2,TextRenderer.MeasureText(Text,font).Width+(int)(24*scale));
                    var rect=new Rectangle(Width-width-1,1,width,Math.Max(2,Height-3));
                    using(var path=Rounded(rect,(int)(6*scale)))using(var brush=new SolidBrush(needsChange?p.BadgeBack:p.Surface))
                    using(var pen=new Pen(needsChange?p.SelectionBorder:p.Border)) {e.Graphics.FillPath(brush,path);e.Graphics.DrawPath(pen,path);}
                    TextRenderer.DrawText(e.Graphics,Text,font,rect,needsChange?p.ButtonInk:p.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
                }
            }
        }
        sealed class ReferenceCard : Control
        {
            readonly Palette p;string caption="",value="",symbol="";
            internal ReferenceCard(Palette p) {this.p=p;DoubleBuffered=true;ResizeRedraw=true;AccessibleRole=AccessibleRole.StaticText;}
            internal void Set(string caption,string value,string symbol)
            {this.caption=caption;this.value=value??"—";this.symbol=symbol;AccessibleName=caption+" · "+symbol+": "+this.value;Invalidate();}
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;float scale=DeviceDpi/96F;
                using(var path=Rounded(new Rectangle(1,1,Width-3,Height-3),(int)(9*scale)))
                using(var brush=new SolidBrush(p.Surface))using(var pen=new Pen(p.Border)) {e.Graphics.FillPath(brush,path);e.Graphics.DrawPath(pen,path);}
                using(var font=new Font("Segoe UI Semibold",22,FontStyle.Regular))
                    TextRenderer.DrawText(e.Graphics,symbol,font,new Rectangle((int)(9*scale),0,(int)(31*scale),Height),p.ReferenceSymbol,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
                int left=(int)(48*scale),right=(int)(12*scale);
                using(var font=new Font("Segoe UI",8,FontStyle.Bold))TextRenderer.DrawText(e.Graphics,caption,font,new Rectangle(left,(int)(10*scale),Width-left-right,(int)(18*scale)),p.ReferenceCaption,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
                using(var font=new Font("Segoe UI Semibold",18,FontStyle.Regular))TextRenderer.DrawText(e.Graphics,value,font,new Rectangle(left,(int)(29*scale),Width-left-right,Height-(int)(36*scale)),p.ReferenceInk,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
            }
        }
        sealed class OutcomeBadge : Control
        {
            readonly Palette p;bool correct;
            internal bool Correct { get { return correct; }set { correct=value;Invalidate(); } }
            internal OutcomeBadge(Palette p) { this.p=p;DoubleBuffered=true;ResizeRedraw=true;AccessibleRole=AccessibleRole.StaticText; }
            protected override void OnTextChanged(EventArgs e) { AccessibleName=Text;Invalidate();base.OnTextChanged(e); }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
                float size=Math.Min(76F*DeviceDpi/96F,Height*.58F),left=(Width-size)/2F,top=7F*DeviceDpi/96F;
                Color ink=correct?p.Green:p.Amber;
                using(var pen=new Pen(ink,Math.Max(3F,size*.08F)))
                {
                    pen.StartCap=pen.EndCap=LineCap.Round;pen.LineJoin=LineJoin.Round;
                    if(correct)e.Graphics.DrawLines(pen,new[]{new PointF(left+size*.12F,top+size*.52F),new PointF(left+size*.39F,top+size*.78F),new PointF(left+size*.9F,top+size*.2F)});
                    else
                    {
                        pen.Width=Math.Max(2F,size*.035F);e.Graphics.DrawEllipse(pen,left+size*.15F,top+size*.12F,size*.7F,size*.7F);
                        using(var font=new Font("Segoe UI",size*.3F,FontStyle.Bold))TextRenderer.DrawText(e.Graphics,"!",font,new Rectangle((int)left,(int)(top+size*.1F),(int)size,(int)(size*.75F)),ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
                    }
                }
                using(var font=new Font("Segoe UI",15,FontStyle.Bold))TextRenderer.DrawText(e.Graphics,Text,font,new Rectangle(8,(int)(top+size),Width-16,Math.Max(1,Height-(int)(top+size))),ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
            }
        }
        static GraphicsPath Rounded(Rectangle rect,int radius)
        {
            int d=radius*2;var path=new GraphicsPath();path.AddArc(rect.X,rect.Y,d,d,180,90);path.AddArc(rect.Right-d,rect.Y,d,d,270,90);
            path.AddArc(rect.Right-d,rect.Bottom-d,d,d,0,90);path.AddArc(rect.X,rect.Bottom-d,d,d,90,90);path.CloseFigure();return path;
        }
        sealed class PaintedButton : Button
        {
            readonly Palette p;bool hover,pressed;
            internal bool Selected,Chrome,Primary;
            internal PaintedButton(Palette p)
            {
                this.p=p;FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;
                SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
                BackColor=p.Surface;ForeColor=p.Ink;AccessibleRole=AccessibleRole.PushButton;
            }
            protected override void OnMouseEnter(EventArgs e) { hover=true;Invalidate();base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover=pressed=false;Invalidate();base.OnMouseLeave(e); }
            protected override void OnMouseDown(MouseEventArgs e) { pressed=e.Button==MouseButtons.Left;Invalidate();base.OnMouseDown(e); }
            protected override void OnMouseUp(MouseEventArgs e) { pressed=false;Invalidate();base.OnMouseUp(e); }
            protected override void OnEnabledChanged(EventArgs e) { Cursor=Enabled?Cursors.Hand:Cursors.Default;Invalidate();base.OnEnabledChanged(e); }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(Parent==null?p.Background:Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
                Color fill=Primary?p.PrimaryBack:Selected?p.SelectionBack:p.ButtonBack;
                Color end=Primary?p.PrimaryEnd:Selected?p.SelectionEnd:p.ButtonEnd;
                Color ink=Enabled?(Primary?p.SelectedInk:Selected?p.SelectionInk:p.ButtonInk):p.Muted;
                if(Enabled&&pressed) {fill=ControlPaint.Dark(fill,0.06F);end=ControlPaint.Dark(end,0.06F);}
                else if(Enabled&&hover) {fill=ControlPaint.Light(fill,0.06F);end=ControlPaint.Light(end,0.06F);}
                var rect=new Rectangle(1,1,Math.Max(1,Width-3),Math.Max(1,Height-3));
                using(var path=Rounded(rect,6))using(var brush=new LinearGradientBrush(rect,fill,end,12F))
                using(var pen=new Pen(Enabled&&hover?p.ButtonHoverBorder:Primary?p.Accent:Selected?p.SelectionBorder:p.ButtonBorder)) { e.Graphics.FillPath(brush,path);e.Graphics.DrawPath(pen,path); }
                TextRenderer.DrawText(e.Graphics,Text,Font,rect,ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
                if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(rect,-4,-4),ink,fill);
            }
        }
        sealed class AngleCard : Control
        {
            readonly Palette p;readonly string axis;string value="—";
            internal string Value { get { return value; }set { this.value=value;AccessibleName="Around "+axis+": "+value;Invalidate(); } }
            internal AngleCard(string axis,Palette p) { this.axis=axis;this.p=p;Name="Angle"+axis;AccessibleRole=AccessibleRole.StaticText;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true); }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
                float scale=DeviceDpi/96F;
                var rect=new Rectangle(1,1,Width-3,Height-3);
                using(var path=Rounded(rect,(int)(12*scale)))using(var brush=new LinearGradientBrush(rect,ControlPaint.Light(p.Surface,0.035F),p.Surface,90F))using(var pen=new Pen(p.Border)) { e.Graphics.FillPath(brush,path);e.Graphics.DrawPath(pen,path); }
                using(var labelFont=new Font("Segoe UI",11,FontStyle.Bold))
                    TextRenderer.DrawText(e.Graphics,axis,labelFont,new Rectangle((int)(18*scale),(int)(13*scale),(int)(24*scale),(int)(26*scale)),p.Ink,TextFormatFlags.Left|TextFormatFlags.VerticalCenter);
                float size=value.Length>7?28:value.Length>5?34:52;
                using(var numberFont=new Font("Segoe UI Semibold",size,FontStyle.Regular))
                    TextRenderer.DrawText(e.Graphics,value,numberFont,new Rectangle((int)(8*scale),(int)(43*scale),Width-(int)(16*scale),Height-(int)(58*scale)),p.PrimaryEnd,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
            }
        }
    }
}
#endregion
