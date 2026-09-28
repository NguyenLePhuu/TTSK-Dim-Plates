using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using D = Tekla.Structures.Drawing;
using M = Tekla.Structures.Model;

class Program
{
    const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static Assembly App;
    static int Result;
    static int UiThread;
    [ThreadStatic] static int ThreadState;
    static object Get(object o,string n) { return o.GetType().GetField(n,Flags).GetValue(o); }
    static void Set(object o,string n,object v) { o.GetType().GetField(n,Flags).SetValue(o,v); }
    static object Call(object o,string n,params object[] args) { return o.GetType().GetMethod(n,Flags).Invoke(o,args); }
    static void Check(bool test,string message) { if(!test)throw new Exception(message); }
    static Task Run(object worker,Action action)
    {
        return (Task)worker.GetType().GetMethod("RunAsync",Flags,null,new[]{typeof(Action)},null).Invoke(worker,new object[]{action});
    }
    [STAThread] static int Main(string[] args)
    {
        App=Assembly.LoadFrom(System.IO.Path.GetFullPath(args[0]));
        Control.CheckForIllegalCrossThreadCalls=true;
        UiThread=Thread.CurrentThread.ManagedThreadId;
        using(var host=new Control())
        {
            var handle=host.Handle;
            host.BeginInvoke((Action)(async ()=>
            {
                try { await Tests(args.Contains("--live-readonly"));Console.WriteLine("PASS: dispatcher and responsive MainForm checks"); }
                catch(Exception ex){Result=1;Console.Error.WriteLine(ex);}
                finally{Application.ExitThread();}
            }));
            Application.Run();
        }
        return Result;
    }
    static async Task Tests(bool live)
    {
        var worker=(IDisposable)Activator.CreateInstance(App.GetType("TTSK_AutoDim_Plates.TeklaOperationDispatcher",true),true);
        try
        {
            int workerId=0;var order=new List<int>();
            var first=Run(worker,()=>
            {
                workerId=Thread.CurrentThread.ManagedThreadId;
                Check(workerId!=UiThread&&Thread.CurrentThread.GetApartmentState()==ApartmentState.STA,"Not a separate STA");
                ThreadState=71;order.Add(1);
                for(int i=0;i<15;i++){Application.DoEvents();Thread.Sleep(10);}
                Check(order.Count==1&&ThreadState==71,"Reentrant operation / ThreadStatic lost");
                order.Add(2);
            });
            var second=Run(worker,()=>{Check(Thread.CurrentThread.ManagedThreadId==workerId&&ThreadState==71,"Worker identity changed");order.Add(3);});
            await Task.WhenAll(first,second);
            Check(string.Join(",",order)=="1,2,3","Operations overlapped");
            var router=App.GetType("Tekla.Technology.Akit.UserScript.PHU_ColumnProjectRouter",true);
            bool failed=false;
            try { await Run(worker,()=>{using((IDisposable)router.GetMethod("BeginInjaiSelection").Invoke(null,null)){throw new InvalidOperationException("expected");}}); }
            catch(InvalidOperationException){failed=true;}
            Check(failed,"Worker exception lost");
            await Run(worker,()=>Check((int)router.GetField("_selectedInjaiDepth",Flags).GetValue(null)==0,"Injai scope leaked after exception"));
            if(live)
                await Run(worker,()=>
                {
                    var model=new M.Model();var drawingHandler=new D.DrawingHandler();
                    Check(model.GetConnectionStatus()&&drawingHandler.GetConnectionStatus(),"Tekla worker connection failed");
                    var drawing=drawingHandler.GetActiveDrawing();Check(drawing!=null,"No active drawing");
                    var before=model.GetWorkPlaneHandler().GetCurrentTransformationPlane().TransformationMatrixToGlobal.ToString();
                    int views=0,parts=0;var e=drawing.GetSheet().GetAllViews();
                    while(e.MoveNext()){var view=e.Current as D.View;if(view==null)continue;views++;var p=view.GetAllObjects(typeof(D.Part));while(p.MoveNext())parts++;}
                    Check(before==model.GetWorkPlaneHandler().GetCurrentTransformationPlane().TransformationMatrixToGlobal.ToString(),"Work plane changed");
                    Console.WriteLine("READ ONLY worker: "+drawing.Mark+", "+views+" views, "+parts+" parts; no Modify/Commit/work-plane changes.");
                });
        }
        finally { worker.Dispose(); }
        // Disposed dispatchers must reject rather than hang or execute callbacks.
        bool rejected=false;try{await Run(worker,()=>{});}catch(ObjectDisposedException){rejected=true;}
        Check(rejected,"Disposed dispatcher accepted work");
        var draining=(IDisposable)Activator.CreateInstance(App.GetType("TTSK_AutoDim_Plates.TeklaOperationDispatcher",true),true);
        using(var entered=new ManualResetEventSlim())
        {
            bool finished=false;
            var inFlight=Run(draining,()=>{entered.Set();Thread.Sleep(200);finished=true;});
            for(int i=0;i<100&&!entered.IsSet;i++)await Task.Delay(10);
            Check(entered.IsSet,"Worker did not start");
            var pending=Run(draining,()=>{throw new Exception("Queued operation must not execute after disposal");});
            draining.Dispose();
            await inFlight;
            bool pendingRejected=false;try{await pending;}catch(ObjectDisposedException){pendingRejected=true;}
            Check(finished&&pendingRejected,"Shutdown aborted work or lost a queued completion");
        }

        using(var form=(Form)Activator.CreateInstance(App.GetType("TTSK_AutoDim_Plates.MainForm",true)))
        {
            var handle=form.Handle; // Keep the test form hidden; do not run Shown/update/journal hooks.
            Call(form,"AddDrawingGridRow",1,"TEST","","test");
            int pulses=0;var timer=new System.Windows.Forms.Timer{Interval=20};
            timer.Tick+=(s,e)=>pulses++;
            timer.Start();
            try
            {
                var running=(Task)Call(form,"ExecuteTeklaCommandAsync","TEST",(Action)(()=>
                {
                    Check(Thread.CurrentThread.ManagedThreadId!=UiThread,"Command stayed on UI thread");
                    for(int i=0;i<12;i++)
                    {
                        Call(form,"SetAutoDimResult","worker "+i);
                        Call(form,"SetGridResult",0,"RUNNING "+i,Color.Blue);
                        Thread.Sleep(80);
                    }
                }));
                await Task.Delay(100);
                Check((bool)Get(form,"operationRunning")&&form.Enabled,"Main form disabled while working");
                Check(!(bool)Call(form,"CanApplyUpdateNow"),"Updater/journal could race Tekla work");
                var closing=new FormClosingEventArgs(CloseReason.UserClosing,false);
                Call(form,"OnFormClosing",closing);Check(closing.Cancel,"Closing can abort a drawing operation");
                bool duplicate=false;
                await (Task)Call(form,"ExecuteTeklaCommandAsync","duplicate",(Action)(()=>duplicate=true));
                Check(!duplicate,"Second command admitted");
                var location=form.Location;form.Location=new Point(location.X+30,location.Y+15);
                Check(form.Location!=location,"Window cannot move");
                Call(form,"ToggleSlideTools");Call(form,"OpenAutoDimensionPanel");Call(form,"SetAutoDimPage",1);
                Check((bool)Get(form,"slideToolsOpen")&&(int)Get(form,"selectedAutoDimPage")==1,"Sidebar navigation frozen");
                var filter=(IMessageFilter)Activator.CreateInstance(form.GetType().GetNestedType("OperationInputFilter",Flags),Flags,null,new object[]{form},null);
                var arrow=(Control)Get(form,"slideHandle");var command=(Control)Get(form,"btnRun");
                var click=Message.Create(arrow.Handle,0x0201,IntPtr.Zero,IntPtr.Zero);
                Check(!filter.PreFilterMessage(ref click),"Sidebar input blocked");
                click=Message.Create(command.Handle,0x0201,IntPtr.Zero,IntPtr.Zero);
                Check(filter.PreFilterMessage(ref click),"Conflicting command input allowed");
                await running;
                Check(pulses>=15,"UI heartbeat starved: "+pulses);
                Check(!(bool)Get(form,"operationRunning")&&form.Enabled,"Busy state not released");
                Console.WriteLine("UI heartbeat ticks during 960 ms command: "+pulses+"; move/sidebar/page navigation passed.");
                // Simulate a batch drawing boundary without mutating any Tekla drawing.
                var batch=(Task)Call(form,"ExecuteTeklaCommandAsync","BATCH TEST",(Action)(()=>
                {
                    Set(form,"_isBatchRunning",true);
                    try{Thread.Sleep(500);Check((bool)Get(form,"_stopRequested"),"Stop request not visible on worker");}
                    finally{Set(form,"_isBatchRunning",false);Set(form,"_stopRequested",false);}
                }));
                await Task.Delay(80);
                Call(form,"btnRun_Click",command,EventArgs.Empty);
                Check((bool)Get(form,"_stopRequested")&&!batch.IsCompleted,"STOP queued behind current drawing");
                await batch;
                var fault=(Task)Call(form,"ExecuteTeklaCommandAsync","FAULT TEST",(Action)(()=>{throw new InvalidOperationException("injected failure");}));
                await fault;
                Check(!(bool)Get(form,"operationRunning"),"Fault leaked busy state");
                bool recovered=false;await (Task)Call(form,"ExecuteTeklaCommandAsync","RECOVERY",(Action)(()=>recovered=true));
                Check(recovered,"Next command cannot run after error");
                // Exercise the real public-slot routing boundary, with a fixture engine only.
                FakeDimensionSlot.ThreadId=0;
                Call(form,"RunExternalAutoDimSlot",typeof(FakeDimensionSlot).FullName);
                Check((bool)Get(form,"operationRunning"),"External slot did not enter common dispatcher");
                var deadline=Stopwatch.StartNew();
                while((bool)Get(form,"operationRunning")&&deadline.ElapsedMilliseconds<5000)await Task.Delay(20);
                Check(!(bool)Get(form,"operationRunning")&&FakeDimensionSlot.ThreadId!=UiThread&&FakeDimensionSlot.ThreadId!=0,"Slot routing stayed synchronous");
                Check(((Label)Get(form,"lblStatus")).Text.Contains("fixture complete"),"Slot result not returned to UI");
            }
            finally{timer.Stop();timer.Dispose();}
        }
    }
}

public static class FakeDimensionSlot
{
    public static int ThreadId;
    public static bool LastRunSucceeded { get { return true; } }
    public static string LastRunMessage { get { return "fixture complete"; } }
    public static void Run() { ThreadId=Thread.CurrentThread.ManagedThreadId;Thread.Sleep(250); }
}
