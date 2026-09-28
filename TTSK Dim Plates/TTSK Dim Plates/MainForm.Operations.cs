using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TTSK_AutoDim_Plates
{
    public partial class MainForm
    {
        private readonly TeklaOperationDispatcher teklaOperations = new TeklaOperationDispatcher();
        private volatile bool operationRunning;
        private void Ui(Action action) { if (InvokeRequired) Invoke(action); else action(); }
        private T ReadUi<T>(Func<T> read) { return InvokeRequired ? (T)Invoke(read) : read(); }

        // Legacy synchronous implementations run on the worker. The UI only awaits,
        // never Task.Wait/Result or a nested DoEvents loop.
        private bool DispatchTeklaCommand(string name, Action work)
        {
            if (teklaOperations.IsWorkerThread) return false;
            StartTeklaCommand(name, work);
            return true;
        }
        private async void StartTeklaCommand(string name, Action work)
        {
            await ExecuteTeklaCommandAsync(name, work);
        }
        private async Task ExecuteTeklaCommandAsync(string name, Action work)
        {
            if (operationRunning || _isBatchRunning || _pdfCommandRunning || _snapshotExportRunning
                || dimensionCheckOpen || _updateDialogOpen || _updaterHandoffClosing) return;
            operationRunning = true;
            var input = new OperationInputFilter(this);
            Application.AddMessageFilter(input);
            try
            {
                SetMainStatus(name + ": đang chạy… Bạn vẫn có thể di chuyển cửa sổ và mở bảng công cụ.", MainStatusKind.Information);
                await teklaOperations.RunAsync(work);
            }
            catch (Exception ex) { SetMainStatus(name + ": " + ex.GetBaseException().Message, MainStatusKind.Error); }
            finally
            {
                Application.RemoveMessageFilter(input);
                operationRunning = false;
            }
        }
        private bool IsOperationNavigation(Control target)
        {
            for (var c = target; c != null && c != this; c = c.Parent)
            {
                if (c == slideHandle || c == toolHomeButton || c == themeSwitch || c == pinTopMostButton
                    || c == slideDimTool || c == slideLineTool || c == slideGridTool
                    || c == slideArrangeTool || c == slideAutoDimTool || c == btnDictionary)
                    return true;
                if (Equals(c.Tag, "OperationNavigation") || c == dgvDrawings) return true;
                if (toolNavigationButtons.Contains(c as SafeRoundedButton)
                    || autoDimPageButtons.Contains(c as SafeRoundedButton)) return true;
                if (c == japaneseDictionaryPanel) return true;
            }
            return false;
        }
        private sealed class OperationInputFilter : IMessageFilter
        {
            private readonly MainForm owner;
            internal OperationInputFilter(MainForm owner) { this.owner = owner; }
            public bool PreFilterMessage(ref Message message)
            {
                if (!owner.operationRunning && !owner._pdfCommandRunning) return false;
                bool input = (message.Msg >= 0x0100 && message.Msg <= 0x0109)
                    || (message.Msg >= 0x0201 && message.Msg <= 0x020E);
                if (!input) return false;
                var control = Control.FromChildHandle(message.HWnd);
                if (control == null || control.FindForm() != owner) return false;
                if (owner._isBatchRunning && control == owner.btnRun) return false;
                return !owner.IsOperationNavigation(control);
            }
        }
    }
}
