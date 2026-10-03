using System;
using System.Threading.Tasks;

namespace TTSK_AutoDim_Plates
{
    public partial class MainForm
    {
        private bool batchShortcutPending;

        private async void RunDimensionCheckShortcut()
        {
            await RunLoadedBatchShortcutAsync("Check DIM", RunDimensionCheckCore);
        }

        private async void RunCheckScaleShortcut()
        {
            await RunLoadedBatchShortcutAsync("Check Scale", () =>
                StartTeklaCommand("Check Scale", RunScaleCheck));
        }

        // Finish loading on the Tekla worker before opening a UI report or
        // starting another operation. Never consume a stale previous selection.
        private async Task RunLoadedBatchShortcutAsync(string name, Action run)
        {
            if (batchShortcutPending || !CanApplyUpdateNow() || dimensionCheckOpen
                || _updateDialogOpen || _updaterHandoffClosing || IsDisposed || Disposing) return;
            batchShortcutPending = true;
            try
            {
                HideCheckMenu();
                HidePrintMenu();
                bool ready = false;
                await ExecuteTeklaCommandAsync("Load / " + name, () =>
                {
                    LoadContextDrawingsCore();
                    ready = _selectedDrawings.Count > 0;
                });
                if (!ready || IsDisposed || Disposing || !CanApplyUpdateNow()
                    || dimensionCheckOpen || _updateDialogOpen || _updaterHandoffClosing) return;
                run();
            }
            catch (Exception ex)
            {
                SetMainStatus("Shortcut " + name + ": " + ex.GetBaseException().Message, MainStatusKind.Error);
            }
            finally { batchShortcutPending = false; }
        }
    }
}
