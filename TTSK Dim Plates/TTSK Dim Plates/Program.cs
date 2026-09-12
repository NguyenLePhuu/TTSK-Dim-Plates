using System;
using System.IO;
using System.Windows.Forms;
using TTSK_AutoDim_Plates.Updater;

namespace TTSK_AutoDim_Plates
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // 1. Kiểm tra chế độ Worker Cập nhật / Phục hồi độc lập trước khi tải bất kỳ thư viện Tekla nào
            if (args != null && args.Length > 0 &&
                (string.Equals(args[0], UpdateWorker.ModeApplyUpdate, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(args[0], UpdateWorker.ModeRecovery, StringComparison.OrdinalIgnoreCase)))
            {
                return UpdateWorker.Run(args);
            }

            string appDir = AppDomain.CurrentDomain.BaseDirectory;

            try
            {
                // Serialize admission with worker READY. Keep a process lease until normal/worker exit.
                using (var admission = new UpdateLock(appDir))
                {
                    if (!admission.TryAcquireUpdateLock(0)) return 1;
                    string journalPath = UpdateJournal.GetJournalPath(appDir);
                    if (File.Exists(journalPath))
                    {
                        var journal = UpdateJournal.LoadFromFile(journalPath);
                        if (journal == null) throw new InvalidDataException("Recovery journal is invalid.");
                        if (journal.State == UpdateTransactionState.Committed || journal.State == UpdateTransactionState.RolledBack)
                            UpdateJournal.DeleteJournal(journalPath);
                        else return UpdateWorker.StartRecoveryFromStaging(appDir);
                    }
                    using (var lease = UpdateWorker.RegisterInstance(appDir))
                    {
                        admission.Release();
                        return ProgramTeklaStartup.Run(args);
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateSecurity.Log(appDir, "Startup gate: " + ex);
                MessageBox.Show("Không thể mở TTSK an toàn: " + ex.Message, "TTSK Dim Plates");
                return 1;
            }
        }
    }
}
