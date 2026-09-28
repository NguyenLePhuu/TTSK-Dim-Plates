using System;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using TTSK_AutoDim_Plates.Keyplan;
static class Regression
{
    static int count;
    static readonly V X=new V(1,0,0),Y=new V(0,1,0),Z=new V(0,0,1);
    static void Check(bool ok,string why) {count++;if(!ok)throw new Exception("FAIL: "+why);}
    static void Reject(Action run,string why) {bool failed=false;try{run();}catch(InvalidOperationException){failed=true;}Check(failed,why);}
    internal static void Run()
    {
        foreach(double invalid in new[]{-360.0,-90,1,37,89.999999,90.000001,359,360,450,double.NaN,double.PositiveInfinity})Reject(()=>new Angles(0,0,invalid),"reject Z "+invalid);
        foreach(int invalid in new[]{-90,1,45,360,450})Reject(()=>new Angles(invalid,0,0),"reject X");
        foreach(int invalid in new[]{-180,90,270,360})Reject(()=>new Angles(0,invalid,0),"reject Y");
        var key=new Frame(X,Y);var bx=Geometry.Targets(false,false,key);var by=Geometry.Targets(false,true,key);var col=Geometry.Targets(true,false,key);
        Check(Geometry.Error(bx[0],new Frame(X,Z))<1e-8&&Geometry.Error(bx[1],new Frame(X,Y))<1e-8,"X keyplan rules");
        Check(Geometry.Error(by[0],new Frame(Y,Z))<1e-8&&Geometry.Error(by[1],new Frame(Y,X*(-1)))<1e-8,"Y keyplan arrow");
        Check(Geometry.Error(col[0],bx[0])<1e-8&&Geometry.Error(col[1],by[0])<1e-8,"column elevations");
        Reject(()=>Geometry.Targets(false,false,new Frame(X,Y*(-1))),"left-handed keyplan rejected");
        Reject(()=>new Frame(X,X),"parallel directions");Reject(()=>new V(double.NaN,0,0).Unit(),"NaN vector");
        var grid=Geometry.ParseGrid("0 8000 4475 3525 2*4000 3300","S15 S16 S16A S17 S17A S18 S19",false);
        Check(grid.Last().Position==27300&&grid[4].Position==20000,"relative grid repeats");
        Check(Geometry.ParseGrid("5900 0","Roof Base",true).First().Label=="Base","height not lexical label");
        Check(Geometry.ParseGrid("0 -100 -50","A B C",false).First().Label=="C","negative spacing");
        Reject(()=>Geometry.ParseGrid("0 100","A",false),"missing labels");Reject(()=>Geometry.ParseGrid("0 100","A A",false),"duplicate labels");
        Reject(()=>Geometry.ParseGrid("1e308 1e308","A B",false),"coordinate overflow");
        Check(Geometry.Angle(new V(1e300,0,0).Unit(),X)<1e-9,"stable large-vector normalization");
        var s=Fixture();var quick=Reader.QuickAnalyze(s,false,false);
        Check(quick.State==QuickState.Correct&&quick.Angles==null&&quick.TestedCount==0,"already correct only tick");
        s.MainFrame=null;Check(Reader.QuickAnalyze(s,false,false).State==QuickState.MissingData,"cannot verify original angles without Local basis");
        s=Fixture();s.Settings=null;Check(Reader.QuickAnalyze(s,false,false).State==QuickState.MissingData,"matching geometry cannot bypass unknown original settings");
        s=Fixture();s.Settings.Error="unreadable";Check(Reader.QuickAnalyze(s,false,false).State==QuickState.MissingData,"failed property read cannot pass");
        s=Fixture();s.Settings.CoordinateSystem="Model";quick=Reader.QuickAnalyze(s,false,false);
        Check(quick.State==QuickState.VerifiedSuggestion&&quick.DirectionsMatch&&quick.Angles!=null&&quick.Message.Contains("Model → Local"),"MODEL matching views must recommend LOCAL, never tick");
        s=Fixture();s.Settings.Z=90;quick=Reader.QuickAnalyze(s,false,false);
        Check(quick.State==QuickState.VerifiedSuggestion&&quick.Angles.Z==0,"matching view with wrong original angle must not pass");
        foreach(double invalid in new[]{-360.0,45,360,double.NaN}) {s=Fixture();s.Settings.Z=invalid;Check(Reader.QuickAnalyze(s,false,false).State==QuickState.VerifiedSuggestion,"original angle outside whitelist never tick");}
        s=Fixture();s.Views[1].SheetY=-100;quick=Reader.QuickAnalyze(s,false,false);
        Check(quick.State==QuickState.LayoutMismatch&&quick.Angles==null,"layout alone no rotation advice");
        s=Fixture();s.Views[0].Frame=new Frame(X*(-1),Z);s.Views[1].Frame=new Frame(X*(-1),Y*(-1));
        quick=Reader.QuickAnalyze(s,false,false);
        Check(quick.State==QuickState.VerifiedSuggestion&&quick.TestedCount==32&&quick.Angles.ToString()==new Angles(0,0,0).ToString(),"absolute zero from model basis despite wrong current views");
        s.MainFrame=null;quick=Reader.QuickAnalyze(s,false,false);Check(quick.State==QuickState.MissingData&&quick.Angles==null,"no invented main part basis");
        s=Fixture();s.Views[0].Unsupported=true;Reject(()=>Reader.QuickAnalyze(s,false,false),"unsupported projection");
        s=Fixture();s.Grids.Add(new GridReference {Id=2,Name="Other",Error="bad"});Check(Reader.QuickAnalyze(s,false,false).GridId==1,"auxiliary invalid grid does not block valid keyplan");
        Check(Reader.QuickAnalyze(s,false,false,1).State==QuickState.Correct,"explicit grid");
        foreach(double yaw in new[]{37.25,-83.4,180.0})
        {
            s=Fixture();var turned=key.Spin(yaw);s.Grids[0].Axes.ForEach(v=>v.Direction=v.Family=="Grid X"?turned.Right:turned.Up);
            s.MainFrame=new Frame(turned.Right,Z);var target=Geometry.Targets(false,false,turned);s.Views[0].Frame=target[0];s.Views[1].Frame=target[1];
            Check(Reader.QuickAnalyze(s,false,false).State==QuickState.Correct,"rotated keyplan direct check");
            s.Views[0].Frame=bx[0];s.Views[1].Frame=bx[1];quick=Reader.QuickAnalyze(s,false,false);
            Check(quick.State==QuickState.VerifiedSuggestion&&quick.Angles.X==0&&quick.Angles.Y==0&&quick.Angles.Z==0,"rotated reference absolute solution");
        }
        s=Fixture();s.MainFrame=new Frame(Z,X*(-1));s.VerticalColumn=true;s.Views[0].SheetX=100;s.Views[1].SheetX=0;
        quick=Reader.QuickAnalyze(s,true,false);Check(quick.State==QuickState.VerifiedSuggestion,"column computes both elevations");
        s.Views[0].Frame=Geometry.LocalView(s.MainFrame,true,"FrontView",quick.Angles);s.Views[1].Frame=Geometry.LocalView(s.MainFrame,true,"TopView",quick.Angles);
        s.Settings.X=quick.Angles.X;s.Settings.Y=quick.Angles.Y;s.Settings.Z=quick.Angles.Z;
        Check(Reader.QuickAnalyze(s,true,false).State==QuickState.Correct,"suggested column pair now green tick");
        foreach(double slope in new[]{0.001,0.01,1.23,37.0})
        {
            s=Fixture();double a=slope*Math.PI/180;s.MainFrame=new Frame(X,Z*Math.Cos(a)+Y*Math.Sin(a));s.Views[0].Frame=new Frame(Y,Z);
            quick=Reader.QuickAnalyze(s,false,false);Check(quick.State==QuickState.VerifiedSuggestion&&quick.Angles.X==0&&quick.Angles.Y==0&&quick.Angles.Z==0,"profile roll preserves projected grid order without rounding");
            s.Views[0].Frame=Geometry.LocalView(s.MainFrame,false,"FrontView",quick.Angles);s.Views[1].Frame=Geometry.LocalView(s.MainFrame,false,"TopView",quick.Angles);
            Check(Reader.QuickAnalyze(s,false,false).State==QuickState.Correct,"rolled Local view pair and original settings agree");
        }
        s=Fixture();s.MainFrame=new Frame(Y,new V(0.02150796,0,0.999768677));s.Settings.CoordinateSystem="Model";
        s.Views[0].Frame=new Frame(Y,Z);s.Views[1].Frame=new Frame(Y,X*(-1));
        s.Grids[0].Axes.ForEach(v=>v.Label=v.Family=="Grid X"?(v.Position==0?"S17":"S17A"):(v.Position==0?"SG-1":"SF-1"));
        var userX=Geometry.Targets(false,false,new Frame(X,Y));
        Check(V.Dot(X*4000,userX[0].Right)>0&&V.Dot(Z*5900,userX[0].Up)>0,"user X Front: S17 left -> S17A right; 2FL below -> 3FL above");
        Check(V.Dot(X*4000,userX[1].Right)>0&&V.Dot(Y*3990,userX[1].Up)>0,"user X Top: S17 left -> S17A right; SG-1 below -> SF-1 above");
        var userY=Geometry.Targets(false,true,new Frame(X,Y));
        Check(V.Dot(Y*3990,userY[0].Right)>0&&V.Dot(Z*5900,userY[0].Up)>0,"user Y Front: SG-1 left -> SF-1 right; floors upwards");
        Check(V.Dot(Y*3990,userY[1].Right)>0&&V.Dot(X*(-4000),userY[1].Up)>0,"user Y Top: SG-1 left -> SF-1 right; S17A below -> S17 above");
        quick=Reader.QuickAnalyze(s,false,true);
        Check(quick.State==QuickState.VerifiedSuggestion&&quick.Angles.X==0&&quick.Angles.Y==0&&quick.Angles.Z==0,"user-confirmed Y view returns LOCAL 0/0/0");
        Check(Reader.QuickAnalyze(s,false,false).State==QuickState.NoSolution,"X remains a separate keyplan direction, never inferred from beam axis");
        s.Settings.CoordinateSystem="Local";Check(Reader.QuickAnalyze(s,false,true).State==QuickState.VerifiedSuggestion,"changed original settings without regenerating views cannot tick");
        s.Views[0].Frame=Geometry.LocalView(s.MainFrame,false,"FrontView",quick.Angles);s.Views[1].Frame=Geometry.LocalView(s.MainFrame,false,"TopView",quick.Angles);
        Check(Reader.QuickAnalyze(s,false,true).State==QuickState.Correct,"user-confirmed regenerated Y sample ticks");
        s.Views[0].Frame=new Frame(s.Views[0].Frame.Right,s.Views[0].Frame.Up*(-1));
        Check(Reader.QuickAnalyze(s,false,true).State!=QuickState.Correct,"inverted floor order can never tick");
        s=Fixture();s.MainFrame=new Frame(X,new V(0,1,1));quick=Reader.QuickAnalyze(s,false,false);
        Check(quick.State==QuickState.NoSolution&&quick.TestedCount==32,"45 degree equal viewing sides are ambiguous, not nearest solution");
        s=Fixture();s.MainFrame=new Frame(new V(1,.2,0),Z);quick=Reader.QuickAnalyze(s,false,false);
        Check(quick.State==QuickState.NoSolution&&quick.TestedCount==32,"skew grid on paper has no exact permitted rotation");
        foreach(double noise in new[]{Geometry.ToleranceDegrees/2,Geometry.ToleranceDegrees*2})
        {
            var scan=Geometry.SearchLocal(new Frame(X,Z),false,new[]{"FrontView","TopView"},new[]{bx[0].Spin(noise),bx[1].Spin(noise)});
            Check((scan.Matches.Count>0)==(noise<Geometry.ToleranceDegrees),"tolerance boundary");
        }
        AutoSelection();ModeRecognition();IncompleteAndApply();GenericGeometry();ActualViewPairs();ExhaustiveTests.Run();Console.WriteLine("PASS "+count+" semantic, failure and keyplan checks.");
    }
    static List<V> Box(V axis,V width,double length,double breadth,double thickness,V origin)
    {
        V normal=V.Cross(axis,width);var points=new List<V>();
        foreach(int x in new[]{-1,1})foreach(int y in new[]{-1,1})foreach(int z in new[]{-1,1})points.Add(origin+axis*(x*length/2)+width*(y*breadth/2)+normal*(z*thickness/2));
        return points;
    }
    static void GenericGeometry()
    {
        foreach(double turn in new[]{0,37.613,90,180,270.0})foreach(bool standing in new[]{false,true})foreach(int sign in new[]{-1,1})
        {
            var plan=new Frame(X,Y).Spin(turn);V axis=standing?Z*sign:plan.Right*sign,width=standing?plan.Up:Z;
            var vertices=Box(axis,width,350,300,16,new V(1e7,-4e6,11335));
            var shape=KeyplanShape.Measure(vertices);
            Check(shape.UniqueAxis&&Math.Abs(V.Dot(shape.Axis,axis))>1-1e-10,"solid principal direction independent of contour order/Local basis");
            var reordered=KeyplanShape.Measure(vertices.AsEnumerable().Reverse().Concat(vertices).Concat(vertices));
            Check(Math.Abs(V.Dot(shape.Axis,reordered.Axis))>1-1e-10&&reordered.VertexCount==8,"face vertex repetitions and ordering do not change recognition");
            var s=Fixture();s.GenericMember=true;s.Shape=shape;s.MainFrame=new Frame(width,axis);
            Check(Reader.RecognizeColumn(s)==standing,"recognition uses solid, not arbitrary contour Local X");
        }
        var square=KeyplanShape.Measure(Box(X,Y,300,300,16,new V()));Check(!square.UniqueAxis,"square solid has no invented unique long edge");
        var f=Fixture();f.GenericMember=true;f.Shape=square;Reject(()=>Reader.Analyze(f),"ambiguous geometry asks manual convention instead of guessing");
        Check(Reader.Analyze(f,false,false).State==QuickState.Correct,"manual mode and direction rescue ambiguous generic shape");
        Reject(()=>KeyplanShape.Measure(new[]{X,Y,Z}),"invalid geometry rejected");
        Reject(()=>KeyplanShape.Measure(new[]{X,Y,Z,new V(double.NaN,0,0)}),"NaN solid rejected");
        var local=new Frame(Z*(-1),new V(0.792158,-0.610316,0));
        f=Fixture();f.GenericMember=true;f.Shape=KeyplanShape.Measure(Box(Z,local.Up,350,300,16,new V()));f.MainFrame=local;f.VerticalColumn=true;f.Settings.CoordinateSystem="Model";
        f.Views[0].Frame=new Frame(local.Up*(-1),Z);f.Views[0].SheetX=244;f.Views[0].SheetY=232;
        f.Views[1].Frame=new Frame(local.Normal,Z);f.Views[1].SheetX=241;f.Views[1].SheetY=350;
        var r=Reader.Analyze(f);Check(r.State==QuickState.VerifiedSuggestion&&r.Angles.ToString()==new Angles(0,0,0).ToString(),"current ContourPlate yields Local zero with corrected pair assignment");
        f.Views[1].SheetX=f.Views[0].SheetX;r=Reader.Analyze(f);Check(r.State==QuickState.VerifiedSuggestion&&!r.LayoutMatches,"generic aligned stacked views still yield angles without throwing");f.Views[1].SheetX=241;
        f.Settings.CoordinateSystem="Local";foreach(var view in f.Views)view.Frame=Geometry.LocalView(local,true,view.Type,r.Angles);
        r=Reader.Analyze(f);Check(r.State==QuickState.LayoutMismatch&&!r.LayoutMatches,"correct plate directions do not falsely certify stacked layout");
        f.Views[0].SheetX=0;f.Views[1].SheetX=100;r=Reader.Analyze(f);Check(r.State==QuickState.Correct,"correct plate properties, directions and layout verified");
        f.Views[1].Frame=f.Views[1].Frame.Spin(5);r=Reader.Analyze(f);Check(r.State==QuickState.MissingData&&!r.CanApply&&r.Angles==null,"generic rotation mismatch detected, never silent retry");
        f=Fixture();f.Overview=true;f.Settings=null;f.MainPart="5 references";r=Reader.Analyze(f);
        Check(r.State==QuickState.Overview&&r.DirectionsMatch&&!r.CanApply&&r.Angles==null,"GA comparison has no invented global Local tuple");
        f.Views.Add(new ViewReference {Id=12,Type="ModelView",Frame=new Frame(Y,Z)});r=Reader.Analyze(f);
        Check(r.Message.Contains("3 view")&&r.DirectionsMatch,"overview checks every real view independent of ViewType name");
        f.Views[2].Frame=f.Views[2].Frame.Spin(7);r=Reader.Analyze(f);Check(!r.DirectionsMatch&&r.Message.Contains("View 12"),"overview identifies wrong view rather than passing whole drawing");
        f.Views[2].Frame=new Frame(Z,Y*(-1));r=Reader.Analyze(f);Check(r.Message.Contains("xoay trên giấy")&&!r.CanApply,"overview supplies explicit separate paper rotation, never global XYZ");
    }
    static void IncompleteAndApply()
    {
        foreach(int remaining in new[]{0,1})foreach(bool top in new[]{false,true})
        {
            var s=Fixture();s.Views=remaining==0?new List<ViewReference>():new List<ViewReference>{s.Views[top?1:0]};
            s.Settings.CoordinateSystem="Model";var r=Reader.Analyze(s);
            Check(r.State==QuickState.VerifiedSuggestion&&r.Angles.ToString()==new Angles(0,0,0).ToString()&&r.CanApply&&!r.CompleteViews,"incomplete beam solves full target pair independent of available view");
            Check(r.TestedCount==32,"incomplete view must keep full angle constraints");
            var request=new ApplyRequest {Identity=s.Identity,Angles=r.Angles};KeyplanApply.Validate(request,s,r);
            s.Settings.CoordinateSystem="Local";r=Reader.Analyze(s);Check(r.State!=QuickState.Correct,"incomplete drawing never green full validation");
            KeyplanApply.Validate(request,s,r);
            s.Settings.Error="unreadable";r=Reader.Analyze(s);
            Check(r.State==QuickState.MissingData&&r.Message.Contains("unreadable"),"missing-view warning preserves actual settings error");
            Reject(()=>KeyplanApply.Validate(request,s,r),"unreadable properties cannot be written");
        }
        var f=Fixture();f.Views.Add(new ViewReference {Type="TopView",Frame=new Frame(X*(-1),Y*(-1)),SheetY=200});
        var result=Reader.Analyze(f);Check(!result.DirectionsMatch&&!result.CompleteViews&&result.State!=QuickState.Correct,"all duplicate views checked, wrong third not silently discarded");
        f.Views.Add(new ViewReference {Type="BackView",Frame=new Frame(X,Z)});result=Reader.Analyze(f);
        Check(!result.CanApply&&result.Angles==null&&result.Message.Contains("4 view"),"extra actual views are explicit; no invented pair/tuple");
        f=Fixture();f.Views.RemoveAt(1);f.Views[0].Unsupported=true;result=Reader.Analyze(f);
        Check(!result.CanApply&&result.State!=QuickState.Correct,"single reflected view cannot authorize update");
        f=Fixture();f.MainFrame=new Frame(Z,X*(-1));f.VerticalColumn=true;f.Settings.CoordinateSystem="Model";
        f.Views.Clear();result=Reader.Analyze(f);
        Check(result.Angles!=null&&result.CanApply&&result.State!=QuickState.Correct,"zero-view column uniquely solves Front/Top assignment from Local basis, never certifies missing views");
        f.Views.Add(new ViewReference {Type="FrontView",Frame=new Frame(Y,Z)});result=Reader.Analyze(f);
        Check(result.IsColumn&&result.CanApply&&result.Angles!=null,"single column elevation determines family and permits full pair solving");
        var full=Fixture();full.MainFrame=f.MainFrame;full.VerticalColumn=true;full.Settings.CoordinateSystem="Model";
        full.Views[0].Frame=new Frame(Y,Z);full.Views[0].SheetX=100;full.Views[1].Frame=new Frame(X,Z);full.Views[1].SheetX=0;
        Check(result.Angles.ToString()==Reader.Analyze(full).Angles.ToString(),"single column agrees with complete real left/right assignment");
        f.Views[0].Frame=new Frame(X,Y);result=Reader.Analyze(f);Check(result.CanApply&&!result.DirectionsMatch,"column end-on current view does not prevent unique full Local solution");
        f=Fixture();f.Settings.CoordinateSystem="Model";result=Reader.Analyze(f);var req=new ApplyRequest {Identity=f.Identity,Angles=result.Angles};
        KeyplanApply.Validate(req,f,result);Check(true,"valid fresh candidate accepted");
        req.Identity="other";Reject(()=>KeyplanApply.Validate(req,f,result),"active drawing switch rejected");req.Identity=f.Identity;
        req.Angles=new Angles(90,0,0);Reject(()=>KeyplanApply.Validate(req,f,result),"stale candidate rejected");
        f.Settings.CoordinateSystem="Local";f.Settings.X=90;result.State=QuickState.NoSolution;
        Reject(()=>KeyplanApply.Validate(req,f,result),"same stored numbers cannot bypass failed current geometry");
        result.State=QuickState.VerifiedSuggestion;result.Angles=req.Angles;result.CanApply=false;
        Reject(()=>KeyplanApply.Validate(req,f,result),"uncertain mapping rejected even with numeric candidate");
        f=Fixture();f.MainFrame=new Frame(X,new V(0,0.1,1));f.Views.RemoveAt(1);
        f.Views[0].Frame=Geometry.LocalView(f.MainFrame,false,"FrontView",new Angles(0,0,0));
        result=Reader.Analyze(f);Check(result.State==QuickState.IncompleteViews&&KeyplanApply.Same(f.Settings,result.Angles),"single rolled view preserves verified current tuple despite unrolled synthetic targets");
    }
    static void ActualViewPairs()
    {
        var s=Fixture();s.MainFrame=new Frame(Z,X*(-1));s.VerticalColumn=true;
        s.MainStart=new V(1000,1000,0);s.MainEnd=new V(1000,1000,1200);s.Settings.X=270;
        foreach(var axis in s.Grids[0].Axes)axis.Label=axis.Family=="Grid X"?(axis.Position==0?"S17A":"S18"):(axis.Position==0?"SG-1":"SF-1");
        s.Views=new List<ViewReference> {
            new ViewReference {Id=10,Type="FrontView",Frame=new Frame(Y,Z),SheetX=100,SheetY=100},
            new ViewReference {Id=11,Type="BottomView",Frame=new Frame(X*(-1),Z),SheetX=300,SheetY=100}
        };
        var plan=Reader.Analyze(s);
        Check(plan.State==QuickState.VerifiedSuggestion&&plan.Angles.ToString()==new Angles(0,0,0).ToString(),"Front LEFT / Bottom RIGHT needs 0/0/0, never hypothetical Top/Front 270/0/0");
        Check(plan.FirstViewType=="FrontView"&&plan.SecondViewType=="BottomView"&&plan.CompleteViews&&plan.CanApply,"angle plan binds actual generated types to real left/right positions");
        Check(plan.ReferenceFirst=="S17A → S18"&&plan.ReferenceSecond=="SG-1 → SF-1","target grid labels come from the same pair plan");
        var reversed=s.WithViews(s.Views.AsEnumerable().Reverse().ToList());Check(Reader.Analyze(reversed).Angles.ToString()==plan.Angles.ToString(),"API enumeration order does not swap column roles");
        var actual=s.WithViews(new List<ViewReference> {
            new ViewReference {Id=21,Type="FrontView",Frame=new Frame(X,Z),SheetX=100,SheetY=100},
            new ViewReference {Id=22,Type="BottomView",Frame=new Frame(Y,Z),SheetX=300,SheetY=100}
        });actual.Settings=new DrawingSettings {CoordinateSystem="Local"};
        Check(Reader.Analyze(actual).State==QuickState.Correct,"independent expected Front/Bottom frames satisfy labels after 0/0/0");
        Check(Reader.VerifyApplied(s,plan,actual,plan.Angles).State==QuickState.Correct,"post-apply checks actual generated geometry, not only saved numbers");
        actual.Views[1].Type="TopView";Reject(()=>Reader.VerifyApplied(s,plan,actual,plan.Angles),"Tekla automatically replacing Bottom by Top invalidates plan even when XYZ saved");
        actual.Views[1].Type="BottomView";actual.Views[0].Frame=new Frame(Y,Z);
        Reject(()=>Reader.VerifyApplied(s,plan,actual,plan.Angles),"wrong actual left grid rejected after successful property save");
        actual.Views[0].Frame=new Frame(X,Z);actual.Views[1].Frame=new Frame(Y*(-1),Z);
        Reject(()=>Reader.VerifyApplied(s,plan,actual,plan.Angles),"reversed actual right grid rejected");
        actual.Views[1].Frame=new Frame(Y,Z);actual.Views[0].SheetX=400;
        Check(Reader.VerifyApplied(s,plan,actual,plan.Angles).State==QuickState.LayoutMismatch,"native reordering yields layout warning instead of a different unverified angle");
        actual.Views.RemoveAt(1);Reject(()=>Reader.VerifyApplied(s,plan,actual,plan.Angles),"native missing view detected after Modify");
        s.Views[1].Type="EndView";plan=Reader.Analyze(s);Check(plan.Angles==null&&!plan.CanApply,"Front+unknown actual view never outputs surrogate 270 as applicable tuple");
        foreach(string front in new[]{"FrontView","BackView"})foreach(string side in new[]{"TopView","BottomView"})
        foreach(bool swapped in new[]{false,true})foreach(int sign in new[]{-1,1})
        {
            var basis=new Frame(Z*sign,X*(-1));string[] types=swapped?new[]{side,front}:new[]{front,side};
            var target=Geometry.Targets(true,false,new Frame(X,Y));var search=Geometry.SearchKeyplan(basis,true,types,target);
            foreach(var candidate in search.Matches)
                Check(Geometry.KeyplanError(Geometry.LocalView(basis,true,types[0],candidate.Angles),target[0])<=Geometry.ToleranceDegrees&&
                    Geometry.KeyplanError(Geometry.LocalView(basis,true,types[1],candidate.Angles),target[1])<=Geometry.ToleranceDegrees,"all four eligible column families honor their actual side and upward grid");
        }
    }
    static void AutoSelection()
    {
        string note;
        foreach(double turn in new[]{0,37.25,90,180,270.0})foreach(bool y in new[]{false,true})foreach(int sign in new[]{-1,1})
        {
            var s=Fixture();var key=new Frame(X,Y).Spin(turn);
            s.Grids[0].Origin=new V(12000,-30000,1500);
            s.Grids[0].Axes.ForEach(a=>a.Direction=a.Family=="Grid X"?key.Right:key.Up);
            V direction=(y?key.Up:key.Right)*sign;
            s.MainFrame=new Frame(direction,Z);s.MainStart=s.Grids[0].Origin+key.Right*1000+key.Up*1000;
            s.MainEnd=s.MainStart.Value+direction*2000;
            Check(Reader.BeamAlongY(s,key)==y,"auto follows rotated grid/member, insensitive to reversed endpoints");
            var automatic=Reader.QuickAnalyze(s,false,!y,0,0,true);var manual=Reader.QuickAnalyze(s,false,y);
            Check(automatic.AlongY==y&&automatic.State==manual.State&&Equals(automatic.Angles==null?null:automatic.Angles.ToString(),manual.Angles==null?null:manual.Angles.ToString()),"auto chooses direction before unchanged solver");
            Check(Reader.BeamAlongY(s,key.Spin(90))!=y,"quarter-turn calibration changes auto basis");
        }
        var f=Fixture();f.MainFrame=new Frame(new V(1,1,0),Z);
        Reject(()=>Reader.QuickAnalyze(f,false,false,0,0,true),"exact diagonal asks manual direction");
        Check(Reader.QuickAnalyze(f,false,false).TestedCount==32,"manual remains available for diagonal");
        f.MainFrame=new Frame(Z,X);Reject(()=>Reader.BeamAlongY(f,new Frame(X,Y)),"vertical has no automatic beam direction");
        f=Fixture();f.MainFrame=new Frame(new V(1,2,3),new V(-2,1,0));Check(Reader.BeamAlongY(f,new Frame(X,Y)),"sloped beam uses horizontal projection");
        f=Fixture();var distant=Fixture().Grids[0];distant.Id=2;distant.Origin=new V(100000,0,0);f.Grids.Add(distant);
        Check(Reader.SelectGrid(f,0,out note).Id==1,"remote grid loses to local grid");f.Grids.Reverse();
        Check(Reader.SelectGrid(f,0,out note).Id==1,"grid enumeration order irrelevant");
        Check(Reader.SelectGrid(f,2,out note).Id==2,"explicit grid overrides ranking");Reject(()=>Reader.SelectGrid(f,99,out note),"stale manual grid never silently falls back");
        var local=Fixture().Grids[0];local.Id=3;local.Axes.ForEach(a=>a.Position*=0.1);f.Grids.Add(local);
        Reject(()=>Reader.SelectGrid(f,0,out note),"larger grid cannot overrule conflicting labelled planes");
        f.Grids.Remove(local);
        var duplicate=Fixture().Grids[0];duplicate.Id=4;f.Grids.Add(duplicate);
        Check(Reader.SelectGrid(f,0,out note).Id==1,"identical grid copies do not block automatic selection");
        // Find ID explicitly: prior reverse-order check deliberately shuffled the grids.
        var main=f.Grids.Single(grid=>grid.Id==1);
        main.Axes.Add(new AxisReference {Family="Grid X",Label="X3",Position=16000,Direction=X});
        main.Axes.Add(new AxisReference {Family="Grid Y",Label="Y3",Position=16000,Direction=Y});
        Check(Reader.SelectGrid(f,0,out note).Id==1,"larger compatible superset wins over an identical local subset");
        f=Fixture();f.MainStart=new V(-982,10025,3770);f.MainEnd=new V(-982,11555,3770);f.MainFrame=new Frame(Y,X*(-1));
        var g=f.Grids[0];g.Axes.Clear();
        foreach(var t in Geometry.ParseGrid("0 5600","1 2",false))g.Axes.Add(new AxisReference{Family="Grid X",Label=t.Label,Position=t.Position,Direction=X});
        foreach(var t in Geometry.ParseGrid("7300 4700","B C",false))g.Axes.Add(new AxisReference{Family="Grid Y",Label=t.Label,Position=t.Position,Direction=Y});
        var r=Reader.QuickAnalyze(f,false,false,0,0,true);
        Check(r.AlongY&&r.Reference.Contains("B → C")&&r.Reference.Contains("1 (dưới)")&&!r.Reference.Contains("1 → 2"),"user sample Y: vertical B/C and single lower 1");
        r=Reader.QuickAnalyze(f,false,false);Check(!r.AlongY&&!r.Reference.Contains("B → C"),"manual X reference has separate family mapping");
        r=Reader.QuickAnalyze(f,false,true,0,2);Check(r.Reference.Contains("C → B")&&r.Reference.Contains("1 (trên)"),"180 degree reference reverses order and side");
        f=Fixture();f.MainStart=new V(2000,2000,0);f.MainEnd=new V(6000,2000,0);
        r=Reader.QuickAnalyze(f,false,false);Check(r.Reference.Contains("X1 → X2")&&r.Reference.Contains("Y1 (dưới)"),"X beam nearest transverse grid");
        f.MainStart=new V(6000,2000,0);f.MainEnd=new V(2000,2000,0);
        Check(Reader.QuickAnalyze(f,false,false).Reference==r.Reference,"labels use midpoint not start/end order");
        f=Fixture();f.MainFrame=new Frame(Z,X*(-1));f.VerticalColumn=true;f.Views[0].SheetX=100;f.Views[1].SheetX=0;
        var c=Reader.QuickAnalyze(f,true,false);var columnAuto=Reader.QuickAnalyze(f,true,true,0,0,true);
        Check(c.State==columnAuto.State&&c.Angles.ToString()==columnAuto.Angles.ToString()&&c.Reference==columnAuto.Reference,"column unaffected by beam automatic option");
    }
    static void ModeRecognition()
    {
        foreach(double yaw in new[]{0,37.25,90,180.0})foreach(int sign in new[]{-1,1})foreach(bool column in new[]{false,true})
        {
            var s=Fixture();var key=new Frame(X,Y).Spin(yaw);V axis=(column?Z:key.Right)*sign;
            s.MainStart=new V(1200,-800,7200);s.MainEnd=s.MainStart.Value+axis*866;
            s.MainFrame=column?new Frame(axis,key.Right*(-1)):new Frame(axis,Z);s.VerticalColumn=column;
            s.MainPart=column?"BEAM":"COLUMN"; // Incorrect names/types must not drive recognition.
            s.Grids[0].Axes.ForEach(v=>v.Direction=v.Family=="Grid X"?key.Right:key.Up);
            s.Views[0].SheetX=100;s.Views[1].SheetX=0;
            Check(Reader.RecognizeColumn(s)==column,"physical 3D axis selects mode despite misleading part name");
            var actual=Reader.Analyze(s);var manual=Reader.QuickAnalyze(s,column,false);
            Check(actual.IsColumn==column&&actual.State==manual.State&&Equals(actual.Angles==null?null:actual.Angles.ToString(),manual.Angles==null?null:manual.Angles.ToString()),"auto mode agrees with explicit geometry convention");
            if(actual.Angles!=null)
            {
                var a=actual.Angles;
                s.Views.ForEach(v=>v.Frame=Geometry.LocalView(s.MainFrame,s.VerticalColumn,v.Type,a));
                s.Settings.X=a.X;s.Settings.Y=a.Y;s.Settings.Z=a.Z;
                Check(Reader.Analyze(s).State==QuickState.Correct,"auto suggestion passes only after matching original Local settings and both views");
            }
        }
        var f=Fixture();f.MainFrame=new Frame(Z,X*(-1));f.VerticalColumn=true;f.Views[0].SheetX=100;
        Check(Reader.Analyze(f).IsColumn,"vertical BEAM uses column without X/Y ambiguity");
        var forced=Reader.Analyze(f,false,false);Check(!forced.IsColumn,"manual beam mode is never silently replaced by column");
        f=Fixture();f.MainFrame=new Frame(new V(1,0,1),Y);
        Reject(()=>Reader.Analyze(f),"equal vertical/horizontal dominance asks manual convention");
        Check(Reader.Analyze(f,false,false)!=null,"manual mode can analyze 45 degree member");
        f=Fixture();f.MainFrame=new Frame(new V(1,0,2),Y);f.Views[0].SheetX=100;Check(Reader.RecognizeColumn(f),"steep member chooses column convention");
        Check(Reader.Analyze(f).State!=QuickState.Correct,"recognition alone cannot certify skew views");
        f=Fixture();f.MainFrame=new Frame(new V(2,0,1),Y);Check(!Reader.RecognizeColumn(f),"shallow member chooses beam convention");
        f=Fixture();f.MainStart=new V();Reject(()=>Reader.Analyze(f),"partial endpoints rejected");
        f.MainEnd=new V();Reject(()=>Reader.Analyze(f),"zero length rejected");
        f.MainEnd=Y*1000;Reject(()=>Reader.Analyze(f),"contradictory endpoint and Local basis rejected");
        f=Fixture();f.Settings=null;Check(Reader.Analyze(f).State==QuickState.MissingData,"automatic cannot bypass missing drawing properties");
        f=Fixture();f.RotationSupportError="unsupported";Check(Reader.Analyze(f).Angles==null,"automatic cannot bypass unverified rotation support");
        f=Fixture();f.Views[0].Unsupported=true;Reject(()=>Reader.Analyze(f),"automatic cannot bypass unsupported view projection");
        // Read-only observed geometry from C-csb20W-13: Tekla calls this BEAM,
        // but its 866 mm axis is vertical and Top is the LEFT elevation.
        f=Fixture();f.Mark="[C-csb20W-13]";f.MainPart="BEAM";
        f.MainStart=new V(-472.001,11555.00001,7235);f.MainEnd=new V(-472.001,11555.00001,8101);
        f.MainFrame=new Frame(Z,new V(-1,-0.000001,0));f.VerticalColumn=true;f.Settings.CoordinateSystem="Model";
        f.Views[0].SheetX=174.3247;f.Views[1].SheetX=69.0124;f.Views.ForEach(v=>v.SheetY=539.0148);
        f.Views[0].Frame=new Frame(new V(1,0.000001,0),Z);f.Views[1].Frame=new Frame(new V(0.000001,-1,0),Z);
        var measured=Reader.Analyze(f);
        Check(measured.IsColumn&&measured.State==QuickState.VerifiedSuggestion&&measured.Angles.ToString()==new Angles(270,0,0).ToString(),"live vertical BEAM sample automatically yields column Local 270/0/0");
        Check(Reader.QuickAnalyze(f,false,false).State==QuickState.NoSolution&&Reader.QuickAnalyze(f,false,true).State==QuickState.NoSolution,"live sample fails both beam conventions for geometric reasons");
        f.Views.Reverse();Check(Reader.Analyze(f).Angles.ToString()==measured.Angles.ToString(),"column result independent of view enumeration order");
        f.Settings.CoordinateSystem="Local";f.Settings.X=270;
        f.Views.ForEach(v=>v.Frame=Geometry.LocalView(f.MainFrame,true,v.Type,measured.Angles));
        Check(Reader.Analyze(f).State==QuickState.Correct,"live sample simulated regenerated views and Local settings verify");
        f.Views[0].Frame=new Frame(f.Views[0].Frame.Right*(-1),f.Views[0].Frame.Up);
        Check(Reader.Analyze(f).State!=QuickState.Correct,"automatic column never certifies reversed horizontal grid labels");
    }
    static Snapshot Fixture()
    {
        var grid=new GridReference {Id=1,Name="Keyplan",Floors=new List<GridTick>{new GridTick {Label="2FL",Position=0},new GridTick {Label="3FL",Position=5900}}};
        foreach(var family in new[]{"Grid X","Grid Y"})for(int i=1;i<=2;i++)grid.Axes.Add(new AxisReference {Family=family,Label=(family=="Grid X"?"X":"Y")+i,Position=(i-1)*8000,Direction=family=="Grid X"?X:Y});
        return new Snapshot {Identity="fixture",Mark="[TEST]",MainPart="BEAM",Settings=new DrawingSettings {CoordinateSystem="Local"},MainFrame=new Frame(X,Z),Grids=new List<GridReference>{grid},Views=new List<ViewReference>{new ViewReference {Id=10,Type="FrontView",Frame=new Frame(X,Z),SheetY=0},new ViewReference {Id=11,Type="TopView",Frame=new Frame(X,Y),SheetY=100}}};
    }
    internal static void Live()
    {
        var s=Reader.Capture();Console.WriteLine("READ ONLY "+s.Mark+" / "+s.MainPart+" / "+s.ModelPath);
        Console.WriteLine("Original="+s.Settings.CoordinateSystem+" X="+s.Settings.X+" Y="+s.Settings.Y+" Z="+s.Settings.Z+" Error="+s.Settings.Error);
        Console.WriteLine("Main right="+s.MainFrame.Right+" up="+s.MainFrame.Up+" vertical="+s.VerticalColumn+" support="+s.RotationSupportError);
        Console.WriteLine("Endpoints="+s.MainStart+" / "+s.MainEnd);
        foreach(var g in s.Grids)Console.WriteLine("GRID "+g+" error="+g.Error);
        var automatic=Reader.Analyze(s);
        Console.WriteLine("AUTO "+(automatic.IsColumn?"COLUMN":automatic.AlongY?"BEAM Y":"BEAM X")+" grid="+automatic.GridId+" "+automatic.State+" "+automatic.Angles+" dir="+automatic.DirectionsMatch+" layout="+automatic.LayoutMatches+"\n"+automatic.Reference+"\n"+automatic.SelectionNote+"\n"+automatic.Message+"\nACTION="+automatic.ActionNote);
        foreach(var v in s.Views)Console.WriteLine(v+" right="+v.Frame.Right+" up="+v.Frame.Up+" center="+v.SheetX+","+v.SheetY);
        foreach(bool column in new[]{false,true})foreach(bool alongY in new[]{false,true})
        {
            if(column&&alongY)continue;
            try {var r=Reader.QuickAnalyze(s,column,alongY);Console.WriteLine((column?"COLUMN":alongY?"BEAM Y":"BEAM X")+" "+r.State+" "+r.Angles+" scanned="+r.TestedCount+" "+r.Message);}catch(Exception e){Console.WriteLine(e.Message);}
        }
    }
    internal static void Ui(string path)
    {
        Application.EnableVisualStyles();
        foreach(bool dark in new[]{false,true})
        {
            var s=Fixture();
            using(var form=new KeyplanForm(FixtureRunner(()=>s),dark))
            {
                var flags=BindingFlags.NonPublic|BindingFlags.Instance;Func<string,object> field=n=>typeof(KeyplanForm).GetField(n,flags).GetValue(form);
                Action<string> click=n=>{((Button)field(n)).PerformClick();Application.DoEvents();};string output=path.Replace(".png",dark?"-dark":"-light");
                form.Show();Application.DoEvents();Check((QuickState?)field("displayedState")==QuickState.Correct,"auto check on open");
                Check(field("result")==null&&!((Control)field("values")).Visible&&!((Control)field("copy")).Visible,"tick hides numbers and copy");
                Save(form,output+"-correct.png");click("settings");Save(form,output+"-settings.png");click("settings");
                s.Settings.CoordinateSystem="Model";click("refresh");
                Check((QuickState?)field("displayedState")==QuickState.VerifiedSuggestion&&field("result")!=null,"MODEL with matching geometry shows XYZ and mode change");Save(form,output+"-model.png");s.Settings.CoordinateSystem="Local";
                s.Views[0].Frame=new Frame(X*(-1),Z);s.Views[1].Frame=new Frame(X*(-1),Y*(-1));click("refresh");
                Check((QuickState?)field("displayedState")==QuickState.VerifiedSuggestion&&field("result")!=null,"wrong drawing displays absolute XYZ");Save(form,output+"-angles.png");
                click("y");Check((bool)field("alongY"),"switch checks immediately");Check((QuickState?)field("displayedState")==QuickState.NoSolution&&field("result")==null,"no solution clears prior recommendation");Save(form,output+"-no-solution.png");
                click("refresh");Check(!(bool)field("autoDirection")&&(bool)field("alongY"),"manual Y persists on refresh");
                click("auto");Check((bool)field("autoDirection")&&!(bool)field("alongY"),"return auto chooses X again");
                click("y");s.Identity="new drawing";click("refresh");Check((bool)field("autoDirection")&&!(bool)field("alongY"),"new drawing clears old override");
                click("column");Check(!((Button)field("x")).Enabled&&!((Button)field("y")).Enabled&&!((Button)field("auto")).Enabled,"column fixed views");
                click("beam");s.MainFrame=new Frame(new V(1,1,0),Z);click("auto");Check(field("result")==null&&field("displayedState")==null,"ambiguous auto clears prior result");
                click("x");Check(!(bool)field("autoDirection")&&field("displayedState")!=null,"manual rescues ambiguous automatic direction");
                s.MainFrame=new Frame(Z,X*(-1));s.VerticalColumn=true;s.Views[0].SheetX=100;s.Views[1].SheetX=0;
                click("autoModeButton");Check((bool)field("autoMode")&&(bool)field("isColumn")&&field("result")!=null,"auto mode routes vertical BEAM to column and returns angles");
                Save(form,output+"-auto-column.png");
                click("beam");Check(!(bool)field("autoMode")&&!(bool)field("isColumn"),"manual mode overrides automatic column");
                click("refresh");Check(!(bool)field("autoMode")&&!(bool)field("isColumn"),"manual mode persists on refresh");
                click("column");click("autoModeButton");Check((bool)field("autoMode")&&(bool)field("isColumn"),"one click restores full automatic mode from manual column");
                s.Identity="another beam";s.MainFrame=new Frame(X,Z);s.VerticalColumn=false;click("refresh");
                Check((bool)field("autoMode")&&!(bool)field("isColumn"),"new drawing resets old column mode");
                form.Close();
            }
        }
        Console.WriteLine("PASS UI automatic checks, themes, recommendations and stale-result clearing.");
        UiApply(path);
    }
    static void UiApply(string path)
    {
        foreach(bool dark in new[]{false,true})
        {
            var s=Fixture();s.Settings.CoordinateSystem="Model";int writes=0;bool fail=false;
            var pending=new TaskCompletionSource<Snapshot>();bool delayed=false;
            bool saved=false;int saves=0;
            using(var form=new KeyplanForm(FixtureRunner(()=>s,request=> {
                writes++;
                if(fail)throw new InvalidOperationException("Test: chưa xác nhận sau Modify; không tự thử lại.");
                if(delayed)return pending.Task;
                s.Settings=new DrawingSettings {CoordinateSystem="Local",X=request.Angles.X,Y=request.Angles.Y,Z=request.Angles.Z};return Task.FromResult(s);
            }),dark,false,value=>{saved=value;saves++;}))
            {
                var flags=BindingFlags.NonPublic|BindingFlags.Instance;Func<string,object> field=n=>typeof(KeyplanForm).GetField(n,flags).GetValue(form);
                Action refresh=()=>{((Button)field("refresh")).PerformClick();Application.DoEvents();};
                var toggle=(CheckBox)field("applyToggle");form.Show();Application.DoEvents();
                Check(!toggle.Checked&&writes==0,"automatic writer OFF on opening");
                toggle.Checked=true;Application.DoEvents();Check(writes==1&&(QuickState?)field("displayedState")==QuickState.Correct,"toggle sends one fresh request then rechecks output");
                Check(((Label)field("status")).Text.Contains("Đã áp dụng")&&saved&&saves==1,"post-write success visible; explicit ON preference saved once");
                Save(form,path.Replace(".png",dark?"-dark-applied.png":"-light-applied.png"));
                refresh();Check(writes==1,"same settings never reapply");
                s.Settings.CoordinateSystem="Model";s.Identity="switched";refresh();Check(toggle.Checked&&writes==2&&saves==1,"new drawing follows persistent ON without writing preference again");
                s.Views.RemoveAt(1);s.Settings.CoordinateSystem="Model";refresh();
                Check(writes==3&&(QuickState?)field("displayedState")==QuickState.IncompleteViews&&((Control)field("values")).Visible,"one-view drawing applies settings but still shows angles and missing-view warning");
                Save(form,path.Replace(".png",dark?"-dark-one-view.png":"-light-one-view.png"));
                toggle.Checked=false;s.Settings.CoordinateSystem="Model";fail=true;toggle.Checked=true;Application.DoEvents();
                Check(writes==4&&toggle.Checked&&saved&&form.Visible&&field("result")==null,"error keeps user preference, leaves form open and clears stale angles");
                Application.DoEvents();Check(writes==4,"error does not trigger automatic retry");
                toggle.Checked=false;refresh();Check(writes==4&&!saved,"explicit OFF persists and prevents writes");
                fail=false;delayed=true;toggle.Checked=true;Application.DoEvents();form.Close();
                Check(form.Visible&&!toggle.Enabled&&(bool)field("busy"),"cannot close or reenter while a write is pending");
                s.Settings=new DrawingSettings {CoordinateSystem="Local"};pending.SetResult(s);Application.DoEvents();
                Check(!(bool)field("busy")&&toggle.Enabled,"controls recover after async writer completes");
                form.Close();
            }
            s.Settings.CoordinateSystem="Model";
            using(var reopened=new KeyplanForm(FixtureRunner(()=>s,request=> {
                writes++;s.Settings=new DrawingSettings {CoordinateSystem="Local",X=request.Angles.X,Y=request.Angles.Y,Z=request.Angles.Z};return Task.FromResult(s);
            }),dark,saved,value=>saved=value))
            {
                int old=writes;reopened.Show();Application.DoEvents();Check(writes==old+1,"reopened form immediately auto-applies saved ON");
                var toggle=(CheckBox)typeof(KeyplanForm).GetField("applyToggle",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(reopened);
                toggle.Checked=false;reopened.Close();
            }
            using(var off=new KeyplanForm(FixtureRunner(()=>s,request=>{throw new Exception("OFF must not write");}),dark,saved))
            {off.Show();Application.DoEvents();Check(!saved,"explicit OFF survives next form");off.Close();}
        }
        PreferenceRoundtrip();
        Console.WriteLine("PASS UI auto-apply: persistent ON/OFF, one run on open, no duplicate writes, failure retains preference, no automatic retries, busy-close guard and fresh configuration reload.");
    }
    static Func<KeyplanRunRequest,Task<KeyplanRunResult>> FixtureRunner(Func<Snapshot> read,Func<ApplyRequest,Task<Snapshot>> write=null)
    {
        return async request=> {
            var s=read();var output=KeyplanApply.Analyze(s,request);
            bool reset=request.PreviousIdentity!=null&&s.Identity!=request.PreviousIdentity;
            var result=new KeyplanRunResult {Snapshot=s,Result=output,ResetOverrides=reset};
            if(request.AutoApply&&write!=null&&output.CanApply&&output.State==QuickState.VerifiedSuggestion&&output.Angles!=null&&!KeyplanApply.Same(s.Settings,output.Angles))
            {
                Check(request.ConfirmRecreate!=null&&request.ConfirmRecreate(),"active auto-apply authorizes recreation without a message box");
                s=await write(new ApplyRequest {Identity=s.Identity,Column=output.IsColumn,AlongY=output.AlongY,GridId=output.GridId,QuarterTurns=reset?0:request.QuarterTurns,Angles=output.Angles});
                result.Snapshot=s;result.Result=KeyplanApply.Analyze(s,request);result.Applied=true;result.Note="Đã áp dụng Local · X / Y / Z. ";
            }
            return result;
        };
    }
    static void PreferenceRoundtrip()
    {
        string path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"ttsk-keyplan-"+Guid.NewGuid().ToString("N")+".cfg");
        try {
            var preferences=new KeyplanPreferences(path);Check(!preferences.Load(),"missing preference defaults OFF");
            preferences.Save(true);Check(new KeyplanPreferences(path).Load(),"new preference instance loads saved ON");
            preferences.Save(false);Check(!new KeyplanPreferences(path).Load(),"new preference instance loads saved OFF");
            System.IO.File.WriteAllText(path,"broken");bool failed=false;try{preferences.Load();}catch(System.IO.InvalidDataException){failed=true;}Check(failed,"corrupt preference never silently enables writes");
            preferences.Save(true);Check(preferences.Load(),"atomic rewrite repairs preference");
        } finally {if(System.IO.File.Exists(path))System.IO.File.Delete(path);}
    }
    internal static void UiLive(string path)
    {
        var snapshot=Reader.Capture();Application.EnableVisualStyles();
        using(var form=new KeyplanForm(FixtureRunner(()=>snapshot),true))
        { form.Show();Application.DoEvents();Save(form,path);form.Close(); }
        Console.WriteLine("READ ONLY UI snapshot: "+snapshot.Mark);
    }
    static void Save(Form form,string path) {using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(path);}}
}
