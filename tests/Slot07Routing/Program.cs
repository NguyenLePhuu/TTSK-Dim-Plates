using System;
using System.Threading;
using R = Tekla.Technology.Akit.UserScript.PHU_ColumnProjectRouter;

// Deliberately no Tekla DLL reference or live connection in this test executable.
namespace Tekla.Structures.Model
{
    public class ModelInfo { public string ModelPath; public string ModelName; }
    public class Model
    {
        public bool Connected = true, RejectIdentity;
        public int IdentityReads;
        public ModelInfo Info = new ModelInfo { ModelPath = "C:/OtherProject", ModelName = "Sample" };
        public bool GetConnectionStatus() { return Connected; }
        public ModelInfo GetInfo()
        { IdentityReads++; if (RejectIdentity) throw new Exception("Identity must not be read"); return Info; }
    }
}

class Program
{
    static int count;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); count++; }
    static string Resolve(Tekla.Structures.Model.Model model)
    { string key, message; Check(R.TryResolve(model, out key, out message), message); return key; }
    static int Main()
    {
        try
        {
            var model = new Tekla.Structures.Model.Model();
            Check(Resolve(model) == R.GeneralSystemKey, "Unselected project must retain general route");
            model.RejectIdentity = true; model.IdentityReads = 0;
            using (R.BeginInjaiSelection())
            {
                Check(Resolve(model) == R.InzaiDataCenterKey, "Explicit selection must bypass identity");
                Check(model.IdentityReads == 0, "Selected route must not read project identity");
                var nested = R.BeginInjaiSelection();
                Check(Resolve(model) == R.InzaiDataCenterKey, "Nested selection");
                nested.Dispose(); nested.Dispose();
                Check(Resolve(model) == R.InzaiDataCenterKey, "Idempotent nested disposal must retain outer scope");
                string otherRoute = null; Exception otherError = null;
                var worker = new Thread(() =>
                {
                    try
                    {
                        var otherModel = new Tekla.Structures.Model.Model();
                        string message; R.TryResolve(otherModel, out otherRoute, out message);
                    }
                    catch (Exception ex) { otherError = ex; }
                });
                worker.Start(); worker.Join();
                Check(otherError == null && otherRoute == R.GeneralSystemKey, "Selection must not leak to another thread");
                string key, text;
                Check(!R.TryResolve(null, out key, out text), "Selection must not bypass missing model");
                model.Connected = false;
                Check(!R.TryResolve(model, out key, out text), "Selection must not bypass connection failure");
                model.Connected = true;
            }
            model.RejectIdentity = false;
            Check(Resolve(model) == R.GeneralSystemKey, "Scope cleanup must restore normal route");
            try { using (R.BeginInjaiSelection()) { throw new InvalidOperationException("simulated preflight failure"); } }
            catch (InvalidOperationException) { }
            Check(Resolve(model) == R.GeneralSystemKey, "Failed operation must not leave Inzai enabled");
            var owner = R.BeginInjaiSelection(); Exception wrongThread = null;
            var disposer = new Thread(() => { try { owner.Dispose(); } catch (Exception ex) { wrongThread = ex; } });
            disposer.Start(); disposer.Join();
            Check(wrongThread is InvalidOperationException, "Cross-thread dispose must be rejected");
            Check(Resolve(model) == R.InzaiDataCenterKey, "Cross-thread attempt must not corrupt scope");
            owner.Dispose();
            Check(Resolve(model) == R.GeneralSystemKey, "Owner thread must clean up scope");
            model.Info.ModelPath = "C:/27-011)ATTOKPJ(Data_Center)";
            Check(Resolve(model) == R.InzaiDataCenterKey, "Existing automatic project recognition must remain intact");
            Console.WriteLine("PASS: " + count + " Slot07 routing assertions. No Tekla connection or writes.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
