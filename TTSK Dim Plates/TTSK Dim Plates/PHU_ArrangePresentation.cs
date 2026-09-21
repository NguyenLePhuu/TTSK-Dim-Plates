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

namespace TTSK_AutoDim_Plates
{
    // Read the current rendered sheet, including layout tables and linked keyplans that
    // ContainerView.GetAllViews/GetAllObjects do not expose. Never read a saved snapshot.
    internal static class PHU_ArrangePresentation
    {
        [Serializable]
        public sealed class Data
        {
            public List<Rect> Obstacles = new List<Rect>();
            public List<int> ViewIds = new List<int>();
            public List<double> ViewX = new List<double>(), ViewY = new List<double>();
        }
        internal static Data Read(int[] viewIds, double width, double height)
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
                return (Data)reader.ReadCurrent(viewIds,width,height,protectedTables.ToArray());
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

        internal static Data Parse(IEnumerable raw,int[] viewIds,double width,double height,int[] protectedTableIds=null)
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
            public object ReadCurrent(int[] drawingViewIds, double paperWidth, double paperHeight,int[] protectedTableIds)
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
                        return PHU_ArrangePresentation.Parse(raw, drawingViewIds, paperWidth, paperHeight,protectedTableIds);
                    }
                    finally { channelType.GetMethod("ShutdownAsync").Invoke(channel,null); }
                }
            }


    }
}
