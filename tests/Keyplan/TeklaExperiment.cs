// Development-only harness. Never linked into the production project.
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using D = Tekla.Structures.Drawing;
using M = Tekla.Structures.Model;
using G = Tekla.Structures.Geometry3d;
using TTSK_AutoDim_Plates.Keyplan;
static class TeklaExperiment
{
    static string Root = @"D:\TTSK_KeyplanValidation_20260927\27-027)Sinjuku_Slope";
    static void Guard(M.Model m) { if(!string.Equals(Path.GetFullPath(m.GetInfo().ModelPath).TrimEnd('\\'),Root.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))throw new Exception("Experiment write refused outside isolated copy: "+m.GetInfo().ModelPath); }
    public static int Run(string[] a)
    {
        var m=new M.Model(); var h=new D.DrawingHandler(); var mh=new M.ModelHandler();
        if(a[1]=="open-copy"&&!m.GetConnectionStatus()) { Console.WriteLine("Open="+mh.Open(Root,false));return 0; }
        if(!m.GetConnectionStatus())throw new Exception("Disconnected");
        Console.WriteLine("Model="+m.GetInfo().ModelPath+" Saved="+mh.IsModelSaved());
        if(a[1]=="status")return 0;
        if(a[1]=="prepare")
        {
            if(m.GetInfo().ModelPath!=@"D:\27-027)Sinjuku_Slope")throw new Exception("Unexpected source");
            // Explicit user authorization for development; preserve pending work before switching.
            if(h.GetActiveDrawing()!=null&&!h.CloseActiveDrawing(true))throw new Exception("Close/save drawing failed");
            if(!mh.Save("Keyplan validation checkpoint","","Preserve user work before isolated tests"))throw new Exception("Save failed");
            Console.WriteLine("Saved="+mh.IsModelSaved());return 0;
        }
        if(a[1]=="open-copy") { if(!mh.IsModelSaved())throw new Exception("Unsaved source"); Console.WriteLine("Open="+mh.Open(Root,false));return 0; }
        if(a[1]=="open-original-drawing")
        {
            if(m.GetInfo().ModelPath!=@"D:\27-027)Sinjuku_Slope")throw new Exception("Not source model");
            var drawings=h.GetDrawings();
            while(drawings.MoveNext()) {var d=drawings.Current as D.AssemblyDrawing;if(d!=null&&d.Mark=="[A-H100-3]") {Console.WriteLine("Original drawing open="+h.SetActiveDrawing(d,true));return 0;}}
            throw new Exception("Original drawing not found");
        }
        Guard(m);
        if(a[1]=="roundtrip-new")
        {
            bool column=a[2]=="column";
            var part=new M.Beam(column?M.Beam.BeamTypeEnum.COLUMN:M.Beam.BeamTypeEnum.BEAM);
            part.StartPoint=new G.Point(50000,50000,0);part.EndPoint=column?new G.Point(50000,50000,4000):new G.Point(54000,50000,0);
            part.Profile.ProfileString=column?"STKR250*250*12":"H150*150*7*10";part.Material.MaterialString="SS400";part.Name=column?"COLUMN":"BEAM";
            if(!part.Insert()||!m.CommitChanges("Isolated property-reader roundtrip"))throw new Exception("New test member failed");
            return Run(new[]{"--experiment","roundtrip",part.GetAssembly().Identifier.ID.ToString(),a[3],a[2]});
        }
        if(a[1]=="read-settings")
        {
            if(h.GetActiveDrawing()!=null)h.CloseActiveDrawing(true);
            var expected=Geometry.AllowedAngles().ToArray();var drawings=h.GetDrawings();int count=0;
            while(drawings.MoveNext())
            {
                var d=drawings.Current as D.AssemblyDrawing;
                if(d==null||d.SheetNumber<9096||d.SheetNumber>9127)continue;
                Guard(m);int index=d.SheetNumber-9096;
                string path=Path.Combine(Root,"attributes","KEYPLAN_TEST_3_"+index+".ad");
                byte[] preset=File.ReadAllBytes(path);
                try
                {
                    // Deliberately make the preset disagree with the drawing. A reader
                    // that reads a filename instead of original drawing properties fails.
                    string decoy=Set(Set(Set(Set(Encoding.GetEncoding(932).GetString(preset),"dv.coord_sys","2"),"dv.coord_sys_x_rotate","90"),"dv.coord_sys_y_rotate","180"),"dv.coord_sys_z_rotate","270");
                    File.WriteAllText(path,decoy,Encoding.GetEncoding(932));
                    if(!h.SetActiveDrawing(d,false,true))throw new Exception("Open read fixture failed");
                    var selector=h.GetDrawingObjectSelector();var objects=d.GetSheet().GetAllObjects(typeof(D.View));
                    D.DrawingObject selected=null;while(objects.MoveNext()){selected=objects.Current as D.DrawingObject;if(selected!=null)break;}
                    if(selected!=null&&!selector.SelectObject(selected))throw new Exception("Selection fixture failed");
                    var original=DrawingSettingsReader.Read();var angle=expected[index];
                    if(original.Error!=null||!original.IsLocal||original.X!=angle.X||original.Y!=angle.Y||original.Z!=angle.Z)
                        throw new Exception("Original settings mismatch "+index+" expected "+angle+" got "+original.CoordinateSystem+" "+original.X+","+original.Y+","+original.Z+" "+original.Error);
                    if(selected!=null) { var after=selector.GetSelected();if(!after.MoveNext()||after.Current.GetType()!=selected.GetType()||after.MoveNext())throw new Exception("Selection not restored"); }
                    Console.WriteLine("PASS original "+index+" "+angle+" despite MODEL decoy preset; selection restored");count++;
                    h.CloseActiveDrawing(false);
                }
                finally { File.WriteAllBytes(path,preset); }
            }
            if(count!=32)throw new Exception("Expected 32 fixtures, read "+count);
            Console.WriteLine("PASS all 32 original tuples, Local/Model decoy isolation and selection restoration.");return 0;
        }
        if(a[1]=="roundtrip")
        {
            if(h.GetActiveDrawing()!=null)h.CloseActiveDrawing(true);
            int assemblyId=int.Parse(a[2]),batch=int.Parse(a[3]);bool column=a[4]=="column";
            var ass=(M.Assembly)m.SelectModelObject(new Tekla.Structures.Identifier(assemblyId));
            string template=File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"../local-zero.ad"),Encoding.GetEncoding(932));
            var angle=new Angles(180,180,90);
            for(int step=0;step<2;step++)
            {
                Guard(m);string name="KEYPLAN_ROUNDTRIP_"+batch+"_"+step;
                string attr=Set(Set(Set(Set(Set(Set(template,"in_file","\""+name+"\""),"out_file","\""+name+"\""),"dv.coord_sys","1"),"dv.coord_sys_x_rotate",angle.X.ToString()),"dv.coord_sys_y_rotate",angle.Y.ToString()),"dv.coord_sys_z_rotate",angle.Z.ToString());
                File.WriteAllText(Path.Combine(Root,"attributes",name+".ad"),attr,Encoding.GetEncoding(932));
                var d=new D.AssemblyDrawing(ass.Identifier,12000+batch*2+step,name);
                if(!d.Insert()||!h.SetActiveDrawing(d,false,true))throw new Exception("Roundtrip drawing failed");
                var snapshot=Reader.Capture();var result=Reader.QuickAnalyze(snapshot,column,false);
                Console.WriteLine("Step="+step+" input="+angle+" state="+result.State+" suggestion="+result.Angles+" count="+result.TestedCount+" message="+result.Message);
                if(step==0) {if(result.State!=QuickState.VerifiedSuggestion)throw new Exception("No verified suggestion");angle=result.Angles;}
                else if(result.State!=QuickState.Correct||result.Angles!=null)throw new Exception("Suggestion failed actual Tekla round trip");
                h.CloseActiveDrawing(true);
            }
            Console.WriteLine("PASS actual Tekla wrong -> suggested properties -> regenerated drawing -> green tick.");return 0;
        }
        if(a[1]=="configure")
        {
            if(h.GetActiveDrawing()!=null)h.CloseActiveDrawing(true);
            var p=(M.Beam)m.SelectModelObject(new Tekla.Structures.Identifier(int.Parse(a[2])));
            if(a[3]=="reverse") {var start=p.StartPoint;p.StartPoint=p.EndPoint;p.EndPoint=start;}
            else if(a[3]=="yaw") {double yaw=37.25*Math.PI/180;var start=p.StartPoint;p.EndPoint=new G.Point(start.X+4000*Math.Cos(yaw),start.Y+4000*Math.Sin(yaw),start.Z);}
            else if(a[3]=="axis-x") {var start=p.StartPoint;p.EndPoint=new G.Point(start.X+4000,start.Y,start.Z);}
            else throw new Exception("Unknown fixture");
            if(!p.Modify()||!m.CommitChanges("Keyplan isolated fixture "+a[3]))throw new Exception("Fixture modify failed");
            Console.WriteLine("Configured "+p.Identifier.ID+" "+a[3]);return 0;
        }
        if(a[1]=="options")
        {
            Console.WriteLine("Version="+Tekla.Structures.TeklaStructuresInfo.GetCurrentProgramVersion()+" Build="+Tekla.Structures.TeklaStructuresInfo.GetBuildNumber());
            string option="";Console.WriteLine("VerticalRead="+Tekla.Structures.TeklaStructuresSettings.GetAdvancedOption("XS_USE_VERTICAL_PLACING_FOR_COLUMNS_IN",ref option)+" Value="+option);
            return 0;
        }
        if(a[1]=="verify")
        {
            string evidenceDir=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"../tekla-validation"));
            int assertions=0;double max=0;
            for(int batch=1;batch<=4;batch++)
            {
                int id=new[]{38175427,39043935,39044063,37473618}[batch-1];
                var p=(M.Part)m.SelectModelObject(new Tekla.Structures.Identifier(id));var cs=p.GetCoordinateSystem();
                var basis=new Frame(new V(cs.AxisX.X,cs.AxisX.Y,cs.AxisX.Z),new V(cs.AxisY.X,cs.AxisY.Y,cs.AxisY.Z));
                File.WriteAllText(Path.Combine(evidenceDir,"basis-"+batch+".txt"),string.Join("\t",new[]{Inv(basis.Right.X),Inv(basis.Right.Y),Inv(basis.Right.Z),Inv(basis.Up.X),Inv(basis.Up.Y),Inv(basis.Up.Z),batch==2?"true":"false"}));
                foreach(string line in File.ReadAllLines(Path.Combine(evidenceDir,"frames-"+batch+".tsv")).Skip(1))
                {
                    var v=line.Split('\t');Func<int,double> n=i=>double.Parse(v[i],System.Globalization.CultureInfo.InvariantCulture);
                    var actual=new Frame(new V(n(4),n(5),n(6)),new V(n(7),n(8),n(9)));
                    var predicted=Geometry.LocalView(basis,batch==2,v[3],new Angles((int)n(0),(int)n(1),n(2)));
                    double error=Geometry.Error(predicted,actual);max=Math.Max(max,error);assertions++;
                    if(error>Geometry.ToleranceDegrees)throw new Exception("Mismatch batch "+batch+" "+string.Join(",",v.Take(4))+" degrees="+error);
                }
            }
            Console.WriteLine("PASS "+assertions+" independently measured Tekla frames. Maximum error degrees="+max);return 0;
        }
        if(a[1]=="inventory")
        {
            var drawings=h.GetDrawings();int limit=0;
            while(drawings.MoveNext())
            {
                var d=drawings.Current as D.AssemblyDrawing;if(d==null||d.SheetNumber>=9000)continue;
                var ass=m.SelectModelObject(d.AssemblyIdentifier) as M.Assembly;var p=ass?.GetMainPart() as M.Beam;if(p==null)continue;
                Console.WriteLine(d.Mark+" assembly="+ass.Identifier.ID+" part="+p.Identifier.ID+" type="+p.Type+" name="+p.Name+" profile="+p.Profile.ProfileString+" X="+p.GetCoordinateSystem().AxisX+" Y="+p.GetCoordinateSystem().AxisY);
                if(++limit>=120)break;
            }
            return 0;
        }
        if(a[1]=="measure")
        {
            if(h.GetActiveDrawing()!=null&&!h.CloseActiveDrawing(true))throw new Exception("Close failed");
            int assemblyId=int.Parse(a[2]),batch=int.Parse(a[3]);
            int count=a.Length>4?int.Parse(a[4]):32;
            var ass=m.SelectModelObject(new Tekla.Structures.Identifier(assemblyId)) as M.Assembly;
            if(ass==null)throw new Exception("Missing assembly");
            var part=(M.Part)ass.GetMainPart();
            var cs=part.GetCoordinateSystem();
            var basis=new Frame(new V(cs.AxisX.X,cs.AxisX.Y,cs.AxisX.Z),new V(cs.AxisY.X,cs.AxisY.Y,cs.AxisY.Z));
            bool vertical=(part as M.Beam)?.Type==M.Beam.BeamTypeEnum.COLUMN;
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"../tekla-validation/basis-"+batch+".txt"),string.Join("\t",new[]{Inv(basis.Right.X),Inv(basis.Right.Y),Inv(basis.Right.Z),Inv(basis.Up.X),Inv(basis.Up.Y),Inv(basis.Up.Z),vertical?"true":"false"}));
            Console.WriteLine("Part="+part.Identifier.ID+" "+part.Name+" X="+cs.AxisX+" Y="+cs.AxisY);
            string template=File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"../local-zero.ad"),Encoding.GetEncoding(932));
            string outPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"../tekla-validation/frames-"+batch+".tsv");
            using(var output=new StreamWriter(outPath,false,Encoding.UTF8))
            {
                output.WriteLine("X\tY\tZ\tView\tRightX\tRightY\tRightZ\tUpX\tUpY\tUpZ\tSheetX\tSheetY");
                int i=0;
                foreach(var angle in Geometry.AllowedAngles().Take(count))
                {
                    Guard(m);
                    string name="KEYPLAN_TEST_"+batch+"_"+i;
                    string attr=template;
                    attr=Set(attr,"in_file","\""+name+"\""); attr=Set(attr,"out_file","\""+name+"\"");
                    attr=Set(attr,"dv.coord_sys","1");
                    attr=Set(attr,"dv.coord_sys_x_rotate",angle.X.ToString());
                    attr=Set(attr,"dv.coord_sys_y_rotate",angle.Y.ToString());
                    attr=Set(attr,"dv.coord_sys_z_rotate",angle.Z.ToString());
                    File.WriteAllText(Path.Combine(Root,"attributes",name+".ad"),attr,Encoding.GetEncoding(932));
                    var drawing=new D.AssemblyDrawing(ass.Identifier,9000+batch*32+i,name);
                    if(!drawing.Insert())throw new Exception("Insert failed "+angle);
                    if(!h.SetActiveDrawing(drawing,false,true))throw new Exception("Open failed "+angle);
                    var views=drawing.GetSheet().GetAllViews();
                    while(views.MoveNext())
                    {
                        var v=views.Current as D.View;if(v==null)continue;
                        var f=v.DisplayCoordinateSystem;
                        output.WriteLine(string.Join("\t",new object[]{angle.X,angle.Y,angle.Z,v.ViewType,Inv(f.AxisX.X),Inv(f.AxisX.Y),Inv(f.AxisX.Z),Inv(f.AxisY.X),Inv(f.AxisY.Y),Inv(f.AxisY.Z),Inv(v.Origin.X),Inv(v.Origin.Y)}));
                        Console.WriteLine(angle+" "+v.ViewType+" X="+f.AxisX+" Y="+f.AxisY);
                    }
                    output.Flush();
                    if(!h.CloseActiveDrawing(true))throw new Exception("Close failed");
                    i++;
                }
            }
            mh.Save("","","Isolated keyplan experiments");
            Console.WriteLine("Evidence="+outPath);return 0;
        }
        if(a[1]=="restore")
        {
            if(h.GetActiveDrawing()!=null)h.CloseActiveDrawing(true);
            mh.Save("","", "Retain isolated validation evidence");
            Console.WriteLine("Restore="+mh.Open(@"D:\27-027)Sinjuku_Slope",false));return 0;
        }
        throw new Exception("Unknown experiment action");
    }
    static string Inv(double v) { return v.ToString("R",System.Globalization.CultureInfo.InvariantCulture); }
    static string Set(string text,string key,string value)
    {
        string pattern="(?m)^"+Regex.Escape(key)+" [^\\r\\n]*";
        if(!Regex.IsMatch(text,pattern))throw new Exception("Missing attribute "+key);
        return Regex.Replace(text,pattern,key+" "+value);
    }
}
