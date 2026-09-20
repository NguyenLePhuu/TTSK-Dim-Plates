using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using D = Tekla.Structures.Drawing;

public sealed class RevisionAudit : MarshalByRefObject
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static object Get(object o, string n) { var p=o.GetType().GetProperty(n,Flags);return p==null?null:p.GetValue(o,null); }
    static void Dump(object o,StringBuilder b,int depth)
    {
        b.Append(new string(' ',depth)).Append(o.GetType().Name);
        foreach(string n in new[]{"Id","ObjectType","GroupType","Text","Position","StartPoint","EndPoint","Angle","Pen","Points"})
        { var v=Get(o,n);if(v!=null)b.Append(" "+n+"="+v); }
        b.AppendLine();
        var list=Get(o,"Primitives") as IEnumerable;
        if(list!=null)foreach(var c in list)Dump(c,b,depth+1);
    }
    public string Read(string path)
    {
        var a=Assembly.LoadFrom(Path.Combine(Path.GetDirectoryName(typeof(D.Drawing).Assembly.Location),"DPMPrinter.dll"));
        var t=a.GetType("Tekla.Structures.DPMPrinter.DpmData");var d=Activator.CreateInstance(t);
        t.GetMethod("LoadFromFile").Invoke(d,new object[]{path});
        var b=new StringBuilder();foreach(var v in (IEnumerable)Get(d,"Views"))Dump(v,b,0);return b.ToString();
    }
    public static void Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var r=Tekla.Technology.Akit.UserScript.PHU_DimensionRevision.Analyze(false);
        string bin=Path.GetDirectoryName(typeof(D.Drawing).Assembly.Location);
        var domain=AppDomain.CreateDomain("Audit",null,new AppDomainSetup{ApplicationBase=bin,ConfigurationFile=Path.Combine(bin,"TeklaStructures.exe.config")});
        try
        {
            var reader=(RevisionAudit)domain.CreateInstanceFromAndUnwrap(typeof(RevisionAudit).Assembly.Location,typeof(RevisionAudit).FullName);
            File.WriteAllText(Path.Combine(folder,"old.txt"),reader.Read(r.BaselinePath));

        }
        finally{AppDomain.Unload(domain);}
        var b=new StringBuilder(r.Report());
        var views=r.Drawing.GetSheet().GetAllViews();
        while(views.MoveNext())
        {
            var v=views.Current as D.View;if(v==null)continue;
            b.AppendLine("VIEW "+((Tekla.Structures.Identifier)Get(v,"Identifier")).ID+" "+v.ViewType+" "+v.Name+" origin="+v.Origin+" frame="+v.FrameOrigin+" w="+v.Width+" h="+v.Height);
            var objs=v.GetAllObjects();while(objs.MoveNext())
            {
                var o=objs.Current;b.AppendLine(o.GetType().Name+" "+((Tekla.Structures.Identifier)Get(o,"Identifier")).ID);
                var set=o as D.StraightDimensionSet;if(set!=null){var dims=set.GetObjects();while(dims.MoveNext()){var s=dims.Current as D.StraightDimension;if(s!=null)b.AppendLine(" CHILD dist="+s.Distance+" normal="+s.UpDirection+" id="+((Tekla.Structures.Identifier)Get(s,"Identifier")).ID+" "+s.StartPoint+" "+s.EndPoint+" "+String.Concat(s.Value.Cast<object>().OfType<D.TextElement>().Select(e=>e.Value)));}}
                var cloud=o as D.Cloud;if(cloud!=null)b.AppendLine(" CLOUD bulge="+cloud.Bulge+" line="+cloud.Attributes.Line.Color+" "+cloud.Attributes.Line.Type);
                var line=o as D.Line;if(line!=null)b.AppendLine(" LINE "+line.StartPoint+" "+line.EndPoint+" "+line.Attributes.Line.Color+" "+line.Attributes.Line.Type);
            }
        }
        var sheet=r.Drawing.GetSheet().GetAllObjects();while(sheet.MoveNext()){var o=sheet.Current;var c=o as D.Cloud;var l=o as D.Line;if(c!=null)b.AppendLine("SHEET CLOUD "+((Tekla.Structures.Identifier)Get(c,"Identifier")).ID+" bulge="+c.Bulge+" "+c.Attributes.Line.Color+" "+c.Attributes.Line.Type+" points="+String.Join(";",c.Points.Cast<Tekla.Structures.Geometry3d.Point>()));if(l!=null)b.AppendLine("SHEET LINE "+((Tekla.Structures.Identifier)Get(l,"Identifier")).ID+" "+l.StartPoint+" "+l.EndPoint+" "+l.Attributes.Line.Color+" "+l.Attributes.Line.Type);}
        File.WriteAllText(Path.Combine(folder,"objects.txt"),b.ToString());Console.WriteLine("Read-only audit: "+folder);
    }
}
