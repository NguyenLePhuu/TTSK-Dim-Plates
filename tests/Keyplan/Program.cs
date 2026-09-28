using System;
using System.Reflection;
using D = Tekla.Structures.Drawing;
using M = Tekla.Structures.Model;
using G = Tekla.Structures.Geometry3d;
using Tekla.Technology.Akit.UserScript;
using TTSK_AutoDim_Plates.Keyplan;
static class Program
{
    static void Dump(string label, object obj)
    {
        Console.WriteLine(label+": "+obj);
        if(obj==null)return;
        foreach(var p in obj.GetType().GetProperties(BindingFlags.Public|BindingFlags.Instance))
            if(p.GetIndexParameters().Length==0) try { Console.WriteLine("  "+p.Name+"="+p.GetValue(obj,null)); } catch(Exception e){Console.WriteLine(p.Name+": "+e.GetBaseException().Message);}
    }
    static void CS(string label,G.CoordinateSystem cs) { Console.WriteLine(label+" origin="+cs.Origin+" X="+cs.AxisX+" Y="+cs.AxisY); }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            Console.OutputEncoding=System.Text.Encoding.UTF8;
            if(args.Length>0&&args[0]=="--drawing-grid-audit") {
                var handler=new D.DrawingHandler();var drawing=handler.GetActiveDrawing();Console.WriteLine("READ ONLY VISIBLE GRID AUDIT "+drawing.Mark);
                var views=drawing.GetSheet().GetAllViews();while(views.MoveNext()) {
                    var view=views.Current as D.View;if(view==null)continue;Console.WriteLine("VIEW "+view.ViewType+" sheet="+view.Origin+" R="+view.DisplayCoordinateSystem.AxisX+" U="+view.DisplayCoordinateSystem.AxisY);
                    var lines=view.GetAllObjects(typeof(D.GridLine));while(lines.MoveNext()) {
                        var line=lines.Current as D.GridLine;if(line==null)continue;Dump("Start grid label",line.StartLabel);Dump("End grid label",line.EndLabel);
                    }
                }return 0;
            }
            if(args.Length>1&&args[0]=="--restore-active") {
                var handler=new D.DrawingHandler();var active=handler.GetActiveDrawing();if(active==null||active.Mark!=args[1])throw new Exception("Active mark mismatch");
                string mark=active.Mark;var drawings=handler.GetDrawings();D.Drawing found=null;
                while(drawings.MoveNext()){var drawing=drawings.Current as D.Drawing;if(drawing!=null&&drawing.Mark==mark){found=drawing;break;}}
                if(found==null)throw new Exception("Drawing not found in drawing list: "+mark);
                if(!handler.CloseActiveDrawing(false))throw new Exception("Could not close stale active drawing");
                if(!handler.SetActiveDrawing(found,true))throw new Exception("Could not reopen drawing from drawing list");
                var check=Reader.Capture();Console.WriteLine("RESTORED "+check.Mark+" views="+check.Views.Count+" main="+check.MainPart+" state="+Reader.Analyze(check).State);return 0;
            }
            if(args.Length>1&&args[0]=="--single-part-check") {
                var model=new M.Model();var handler=new D.DrawingHandler();var original=handler.GetActiveDrawing();
                if(original==null||original.Mark!=args[1])throw new Exception("Active drawing mismatch");
                var part=PHU_MainPartResolver.Resolve(model,original);if(part==null)throw new Exception("No source part");
                Console.WriteLine("DRAWING-ONLY TEST: temporary SinglePartDrawing for existing part "+part.Identifier.ID+"; preserve/reopen "+original.Mark);
                D.SinglePartDrawing trial=null;bool inserted=false;
                try {
                    if(!handler.CloseActiveDrawing(true))throw new Exception("Cannot preserve original drawing");
                    trial=new D.SinglePartDrawing(part.Identifier,9876,"standard");trial.Name="TTSK_KEYPLAN_TEMP_"+Guid.NewGuid().ToString("N");
                    if(!(inserted=trial.Insert()))throw new Exception("Cannot create isolated drawing");
                    if(!handler.SetActiveDrawing(trial,false,true))throw new Exception("Cannot open trial drawing");
                    var capture=Reader.CaptureGeometry();Console.WriteLine("CAPTURE "+capture.DrawingKind+" "+capture.MainPart);
                    var run=KeyplanApply.Run(new KeyplanRunRequest {AutoApply=true,ConfirmRecreate=()=>true});
                    Console.WriteLine("RUN "+run.Result.State+" "+run.Result.Angles+" applied="+run.Applied+" "+run.Result.Message);
                    Console.WriteLine("Original="+run.Snapshot.Settings.CoordinateSystem+"/"+run.Snapshot.Settings.X+"/"+run.Snapshot.Settings.Y+"/"+run.Snapshot.Settings.Z+" vertical="+run.Snapshot.VerticalColumn);
                    foreach(var view in run.Snapshot.Views)Console.WriteLine(view.Type+" Right="+view.Frame.Right+" Up="+view.Frame.Up);
                } finally {
                    if(trial!=null&&handler.GetActiveDrawing()!=null&&handler.GetActiveDrawing().IsSameDatabaseObject(trial))handler.CloseActiveDrawing(false);
                    if(inserted&&!trial.Delete())Console.WriteLine("WARNING trial drawing remains: "+trial.Name);
                    if(!handler.SetActiveDrawing(original,false,true))throw new Exception("Cannot restore original drawing");
                    Console.WriteLine("RESTORED "+handler.GetActiveDrawing().Mark);
                }
                return 0;
            }
            if(args.Length>2&&args[0]=="--measure-current") {
                var before=Reader.Capture();if(before.Mark!=args[1]||!before.Settings.IsLocal||before.Settings.AllowedAngles==null)throw new Exception("Drawing/Local preflight mismatch");
                var handler=new D.DrawingHandler();var drawing=handler.GetActiveDrawing();Action guard=()=>{if(!handler.GetActiveDrawing().IsSameDatabaseObject(drawing))throw new Exception("Active drawing changed");};
                string folder=System.IO.Path.GetFullPath(args[2]);System.IO.Directory.CreateDirectory(folder);
                Func<double,string> inv=n=>n.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
                var basis=before.MainFrame;System.IO.File.WriteAllText(System.IO.Path.Combine(folder,"basis-9.txt"),string.Join("\t",new[]{inv(basis.Right.X),inv(basis.Right.Y),inv(basis.Right.Z),inv(basis.Up.X),inv(basis.Up.Y),inv(basis.Up.Z),before.VerticalColumn?"true":"false"}));
                Console.WriteLine("MUTATING DRAWING ONLY: 32 rotations on "+before.Identity+" "+before.Mark+"; restore "+before.Settings.AllowedAngles);
                var stored=before.Settings;
                try {
                    using(var file=new System.IO.StreamWriter(System.IO.Path.Combine(folder,"frames-9.tsv"))) {
                        file.WriteLine("X\tY\tZ\tView\tRightX\tRightY\tRightZ\tUpX\tUpY\tUpZ\tSheetX\tSheetY");
                        foreach(var a in Geometry.AllowedAngles()) {
                            guard();if(!KeyplanApply.Same(stored,a))DrawingSettingsReader.ApplyAngles(stored,a,guard,false,()=>true);
                            stored=DrawingSettingsReader.Read();if(!KeyplanApply.Same(stored,a))throw new Exception("Stored tuple mismatch");
                            var after=Reader.CaptureGeometry();if(Geometry.Error(after.MainFrame,before.MainFrame)>1e-9||after.Identity!=before.Identity)throw new Exception("Reference geometry changed");
                            foreach(var view in after.Views) {
                                var f=view.Frame;double error=Geometry.Error(f,Geometry.LocalView(before.MainFrame,before.VerticalColumn,view.Type,a));
                                if(error>Geometry.ToleranceDegrees)throw new Exception("Rotation mismatch: "+a+" "+view.Type+" error="+error);
                                file.WriteLine(string.Join("\t",new[]{a.X.ToString(),a.Y.ToString(),a.Z.ToString(),view.Type,inv(f.Right.X),inv(f.Right.Y),inv(f.Right.Z),inv(f.Up.X),inv(f.Up.Y),inv(f.Up.Z),inv(view.SheetX),inv(view.SheetY)}));
                            }
                            file.Flush();Console.WriteLine("PASS "+a);
                        }
                    }
                } finally {guard();stored=DrawingSettingsReader.Read();if(!KeyplanApply.Same(stored,before.Settings.AllowedAngles))DrawingSettingsReader.ApplyAngles(stored,before.Settings.AllowedAngles,guard,false,()=>true);}
                Console.WriteLine("PASS all 32 tuples, actual saved values + actual frames; restored original Local tuple.");return 0;
            }
            if(args.Length>0&&args[0]=="--generic-hypotheses") {
                var snapshot=Reader.Capture();snapshot.RotationSupportError=null;snapshot.Views.Find(v=>v.Type=="FrontView").SheetX=0;snapshot.Views.Find(v=>v.Type=="TopView").SheetX=100;
                foreach(bool vertical in new[]{false,true})foreach(bool column in new[]{false,true})foreach(bool y in new[]{false,true}) {
                    if(column&&y)continue;snapshot.VerticalColumn=vertical;
                    try {var result=Reader.Analyze(snapshot,column,y);Console.WriteLine("vertical="+vertical+" column="+column+" Y="+y+" "+result.State+" "+result.Angles);}
                    catch(Exception ex){Console.WriteLine(ex.Message);}
                }return 0;
            }
            if(args.Length>0&&args[0]=="--part-geometry") {
                var model=new M.Model();var drawing=new D.DrawingHandler().GetActiveDrawing();var part=PHU_MainPartResolver.Resolve(model,drawing);
                Dump("Drawing",drawing);Dump("Main part",part);CS("Main CS",part.GetCoordinateSystem());
                if(part is M.Beam)Console.WriteLine("Beam enum="+((M.Beam)part).Type);
                foreach(string name in new[]{"LENGTH","WIDTH","HEIGHT","PROFILE.WIDTH","PROFILE.HEIGHT","PROFILE.TYPE","RADIUS"}) {double val=0;string str="";Console.WriteLine(name+" double="+part.GetReportProperty(name,ref val)+":"+val+" string="+part.GetReportProperty(name,ref str)+":"+str);}
                var solid=part.GetSolid();Console.WriteLine("Solid min="+solid.MinimumPoint+" max="+solid.MaximumPoint);
                var faces=solid.GetFaceEnumerator();int vertices=0;while(faces.MoveNext()){var face=faces.Current as Tekla.Structures.Solid.Face;var loops=face.GetLoopEnumerator();while(loops.MoveNext()){var loop=loops.Current as Tekla.Structures.Solid.Loop;var points=loop.GetVertexEnumerator();while(points.MoveNext()){if(vertices++<100)Console.WriteLine("vertex="+points.Current);}}}
                Console.WriteLine("Vertices="+vertices);return 0;
            }
            if(args.Length>1&&args[0]=="--run-live") {
                var before=Reader.CaptureGeometry();if(before.Mark!=args[1])throw new Exception("Active drawing mismatch: "+before.Mark);
                Console.WriteLine("MUTATING DRAWING ONLY, ONE DIALOG: "+before.Identity+" "+before.Mark);
                var run=KeyplanApply.Run(new KeyplanRunRequest {AutoApply=true,QuarterTurns=args.Length>2?int.Parse(args[2]):0,ConfirmRecreate=()=>true});
                if(run.Snapshot.Identity!=before.Identity||run.Snapshot.MainStart.ToString()!=before.MainStart.ToString()||run.Snapshot.MainEnd.ToString()!=before.MainEnd.ToString()||Geometry.Error(run.Snapshot.MainFrame,before.MainFrame)>1e-9)throw new Exception("Drawing/model geometry changed unexpectedly");
                Console.WriteLine("APPLIED="+run.Applied+" STORED="+run.Snapshot.Settings.CoordinateSystem+"/"+run.Snapshot.Settings.X+"/"+run.Snapshot.Settings.Y+"/"+run.Snapshot.Settings.Z+" STATE="+run.Result.State);
                var verify=DrawingSettingsReader.Read();if(verify.Error!=null||verify.CoordinateSystem!=run.Snapshot.Settings.CoordinateSystem||verify.X!=run.Snapshot.Settings.X||verify.Y!=run.Snapshot.Settings.Y||verify.Z!=run.Snapshot.Settings.Z)throw new Exception("Independent reopen did not confirm saved result");
                Console.WriteLine("PASS independent reopen confirms saved settings; main member geometry unchanged.");return 0;
            }
            if(args.Length>0&&args[0]=="--verify-reload") {DrawingSettingsReader.VerifyReload();Console.WriteLine("PASS same dialog reloads actual drawing values; unsaved X discarded, no Modify.");return 0;}
            if(args.Length>0&&args[0]=="--akit-methods") {
                var script=Assembly.LoadFrom("C:/Program Files/Tekla Structures/2025.0/bin/Akit5.dll").GetType("Tekla.Technology.Akit.IScript",true);
                foreach(var method in script.GetMethods())if(method.Name.Contains("Tree")||method.Name.Contains("Check")||method.Name.Contains("Button")||method.Name.Contains("Value")||method.Name.Contains("User"))Console.WriteLine(method);
                return 0;
            }
            if(args.Length>1&&args[0]=="--apply-live") {
                var before=Reader.Capture();if(before.Mark!=args[1])throw new Exception("Active drawing mismatch: "+before.Mark);
                var candidate=Reader.Analyze(before);
                Console.WriteLine("MUTATING DRAWING ONLY: "+before.Identity+" "+before.Mark+" original="+before.Settings.CoordinateSystem+"/"+before.Settings.X+"/"+before.Settings.Y+"/"+before.Settings.Z+" target="+candidate.Angles);
                if(candidate.Angles==null)throw new Exception("No proposed angles");
                var after=KeyplanApply.Apply(new ApplyRequest {Identity=before.Identity,Column=candidate.IsColumn,AlongY=candidate.AlongY,GridId=candidate.GridId,Angles=candidate.Angles,ConfirmRecreate=()=>true});
                Console.WriteLine("AFTER "+after.Settings.CoordinateSystem+"/"+after.Settings.X+"/"+after.Settings.Y+"/"+after.Settings.Z+" state="+Reader.Analyze(after).State);
                return 0;
            }
            if(args.Length>0&&args[0]=="--portable") {
                var assembly=Assembly.LoadFrom(System.IO.Path.GetFullPath(args[1]));
                var reader=assembly.GetType("TTSK_AutoDim_Plates.Keyplan.Reader",true);
                var snapshot=reader.GetMethod("Capture").Invoke(null,null);
                var settings=snapshot.GetType().GetField("Settings").GetValue(snapshot);
                string mode=(string)settings.GetType().GetField("CoordinateSystem").GetValue(settings);
                Console.WriteLine("PORTABLE ORIGINAL "+mode+" error="+settings.GetType().GetField("Error").GetValue(settings));
                var output=reader.GetMethod("Analyze").Invoke(null,new object[]{snapshot,null,null,0,0});
                Func<string,object> field=n=>output.GetType().GetField(n).GetValue(output);
                string state=field("State").ToString();
                Console.WriteLine("PORTABLE AUTO "+((bool)field("IsColumn")?"COLUMN":(bool)field("AlongY")?"BEAM Y":"BEAM X")+" "+state+" "+field("Angles")+"\n"+field("Reference")+"\n"+field("SelectionNote"));
                if(mode!="Local"&&state=="Correct")throw new Exception("Portable falsely passed non-Local drawing");
                var manual=reader.GetMethod("QuickAnalyze").Invoke(null,new object[]{snapshot,field("IsColumn"),field("AlongY"),field("GridId"),0,false});
                bool generic=(bool)snapshot.GetType().GetField("GenericMember").GetValue(snapshot);
                if(!generic&&(manual.GetType().GetField("State").GetValue(manual).ToString()!=state||
                    Convert.ToString(manual.GetType().GetField("Angles").GetValue(manual))!=Convert.ToString(field("Angles"))))throw new Exception("Portable automatic/manual mismatch");
                return 0;
            }
            if(args.Length>0&&args[0]=="--properties") {
                var settings=DrawingSettingsReader.Read();Console.WriteLine("ORIGINAL "+settings.CoordinateSystem+" X="+settings.X+" Y="+settings.Y+" Z="+settings.Z+" ERROR="+settings.Error);return settings.Error==null?0:1;
            }
            if(args.Length>0&&args[0]=="--inspect-apply") {Console.WriteLine(DrawingSettingsReader.InspectApplyControls());return 0;}
            if(args.Length>0&&(args[0]=="--preview-apply"||args[0]=="--preview-matrix")) {
                var handler=new D.DrawingHandler();var drawing=handler.GetActiveDrawing();
                Action guard=()=> {var active=handler.GetActiveDrawing();if(active==null||drawing==null||!active.IsSameDatabaseObject(drawing))throw new Exception("Drawing changed during preview");};
                var before=Reader.Capture();var result=Reader.Analyze(before);
                var angles=args.Length==4?new Angles(int.Parse(args[1]),int.Parse(args[2]),int.Parse(args[3])):result.Angles;
                if(angles==null)throw new Exception("No candidate");
                var candidates=args[0]=="--preview-matrix"?Geometry.AllowedAngles():new[]{angles};
                foreach(var candidate in candidates) {
                    guard();bool recreate=DrawingSettingsReader.ApplyAngles(before.Settings,candidate,guard,true);guard();
                    var after=DrawingSettingsReader.Read();
                    if(after.Error!=null||after.CoordinateSystem!=before.Settings.CoordinateSystem||after.X!=before.Settings.X||after.Y!=before.Settings.Y||after.Z!=before.Settings.Z)throw new Exception("Preview changed drawing settings");
                    Console.WriteLine("PASS "+candidate+"; native fields filled/read back and Cancelled; drawing properties unchanged. Recreate required="+recreate);
                }
                return 0;
            }
            if(args.Length>0&&args[0]=="--experiment") return TeklaExperiment.Run(args);
            if(args.Length>0&&args[0]=="--measured") { MeasuredTests.Run(args[1]);return 0; }
            if(args.Length>0&&args[0]=="--tests") { Regression.Run();return 0; }
            if(args.Length>0&&args[0]=="--live") { Regression.Live();return 0; }
            if(args.Length>0&&args[0]=="--ui") { Regression.Ui(args[1]);return 0; }
            if(args.Length>0&&args[0]=="--ui-live") { Regression.UiLive(args[1]);return 0; }
            Console.WriteLine("KEYPLAN AUDIT - READ ONLY; no drawing/model/work-plane mutation");
            var m=new M.Model();var h=new D.DrawingHandler();
            Console.WriteLine("Model="+m.GetConnectionStatus()+" Drawing="+h.GetConnectionStatus());
            if(!m.GetConnectionStatus()||!h.GetConnectionStatus())throw new Exception("Disconnected");
            var d=h.GetActiveDrawing();if(d==null)throw new Exception("No active drawing");
            Console.WriteLine("Model path="+m.GetInfo().ModelPath); Dump("Drawing",d);
            var p=PHU_MainPartResolver.Resolve(m,d);if(p==null)throw new Exception("No main part");
            Console.WriteLine("MainPart="+p.Identifier.ID+" "+p.Name);CS("Part CS/current",p.GetCoordinateSystem());
            Console.WriteLine("Current->Global="+m.GetWorkPlaneHandler().GetCurrentTransformationPlane().TransformationMatrixToGlobal);
            var en=d.GetSheet().GetAllViews();while(en.MoveNext()) { var v=en.Current as D.View;if(v==null)continue; Console.WriteLine("VIEW "+v.Name+" type="+v.ViewType+" sheet="+v.Origin);CS(" display",v.DisplayCoordinateSystem);CS(" view",v.ViewCoordinateSystem);Dump(" view attrs",v.Attributes); }
            var grids=m.GetModelObjectSelector().GetAllObjectsWithType(M.ModelObject.ModelObjectEnum.GRID);
            while(grids.MoveNext()) {var g=grids.Current as M.Grid;if(g!=null){Dump("GRID",g);CS("grid cs/current",g.GetCoordinateSystem());}}
            return 0;
        } catch(Exception e){Console.WriteLine(e);return 1;}
    }
}
