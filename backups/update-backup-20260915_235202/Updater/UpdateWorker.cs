using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace TTSK_AutoDim_Plates.Updater
{
    public static class UpdateWorker
    {
        public const string ModeApplyUpdate = "--apply-update", ModeRecovery = "--recovery-update";
        public static bool SilentMode { get; set; }

        public static int Run(string[] args)
        {
            string target = null;
            try
            {
                if (args == null || args.Length == 0 || (args[0] != ModeApplyUpdate && args[0] != ModeRecovery)) throw new InvalidDataException("Invalid worker mode.");
                bool recovery = args[0] == ModeRecovery;
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                for (int i = 1; i < args.Length; i++)
                {
                    if (args[i] == "--silent") { SilentMode = true; continue; }
                    string key = args[i];
                    if (i + 1 >= args.Length || values.ContainsKey(key) ||
                        Array.IndexOf(new[] { "--target", "--staging", "--wait-pid", "--parent-start-ticks", "--version", "--session", "--ready-signal" }, key) < 0)
                        throw new InvalidDataException("Invalid/duplicate worker argument.");
                    values.Add(key, args[++i]);
                }
                target = Path.GetFullPath(values["--target"]).TrimEnd('\\');
                if (recovery)
                {
                    using (var gate = new UpdateLock(target))
                    {
                        if (!gate.TryAcquireUpdateLock(10000)) throw new IOException("Another update is running.");
                        int result = RecoverLocked(target);
                        if (result != 0) return result;
                        gate.Release();
                        return Restart(target);
                    }
                }
                string staging = Path.GetFullPath(values["--staging"]).TrimEnd('\\');
                string session = values["--session"];
                Guid guid;
                if (!Guid.TryParseExact(session, "N", out guid)) throw new InvalidDataException("Invalid session.");
                string readyName = @"Local\TTSK_Update_Ready_" + session;
                if (values["--ready-signal"] != readyName) throw new InvalidDataException("Invalid handshake.");
                int pid = int.Parse(values["--wait-pid"]);
                long ticks = long.Parse(values["--parent-start-ticks"]);
                if (pid <= 0 || ticks <= 0) throw new InvalidDataException("Parent identity is required.");
                using (var parent = Process.GetProcessById(pid))
                {
                    if (parent.StartTime.ToUniversalTime().Ticks != ticks ||
                        !Path.GetFullPath(parent.MainModule.FileName).Equals(Path.Combine(target, "TTSK Dim Plates.exe"), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Parent does not match this installation.");
                }
                if (!AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\').Equals(staging, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Apply worker must execute from staging.");
                var meta = ReleaseMetadata.LoadFromFile(Path.Combine(staging, "release.json"));
                if (meta == null || meta.version != values["--version"]) throw new InvalidDataException("Selected version does not match package.");
                using (var gate = new UpdateLock(target))
                using (var ready = EventWaitHandle.OpenExisting(readyName))
                using (var proceed = EventWaitHandle.OpenExisting(@"Local\TTSK_Update_Proceed_" + session))
                using (var cancel = EventWaitHandle.OpenExisting(@"Local\TTSK_Update_Cancel_" + session))
                {
                    if (!gate.TryAcquireUpdateLock(5000)) throw new IOException("Another updater owns this target.");
                    if (File.Exists(UpdateJournal.GetJournalPath(target))) throw new IOException("Recover the previous update first.");
                    AssertNoInstances(target, pid);
                    var transaction = new UpdateTransaction(target, staging, session, meta);
                    transaction.PreflightCheck();
                    UpdateSecurity.Log(target, "READY " + session + " version " + meta.version);
                    ready.Set();
                    if (WaitHandle.WaitAny(new WaitHandle[] { cancel, proceed }, 15000) != 1) throw new IOException("Handoff cancelled or timed out.");
                    if (!UpdateLock.WaitForParentExit(pid, ticks, 30000) || cancel.WaitOne(0)) throw new IOException("Parent did not exit cleanly.");
                    AssertNoInstances(target, 0);
                    transaction.Execute();
                    // Release before launch: otherwise the new application rejects its own startup.
                    gate.Release();
                    return Restart(target);
                }
            }
            catch (Exception ex)
            {
                if (target != null) UpdateSecurity.Log(target, ex.ToString());
                ShowError(ex.Message);
                return 1;
            }
        }

        private static int Restart(string target)
        {
            try
            {
                using (var process = Process.Start(new ProcessStartInfo(Path.Combine(target, "TTSK Dim Plates.exe")) { WorkingDirectory = target, UseShellExecute = false })) { }
                return 0;
            }
            catch (Exception ex)
            {
                UpdateSecurity.Log(target, "Runtime ready; restart failed: " + ex);
                ShowError("Runtime đã sẵn sàng nhưng khởi động lại thất bại. Mở TTSK thủ công. " + ex.Message);
                return 2;
            }
        }

        public static IDisposable RegisterInstance(string target)
        {
            string dir = Path.Combine(Path.GetDirectoryName(UpdateJournal.GetJournalPath(target)), "instances");
            UpdateSecurity.AssertNoReparseAncestors(dir);
            Directory.CreateDirectory(dir);
            using (var self = Process.GetCurrentProcess())
                return new FileStream(Path.Combine(dir, self.Id + "-" + self.StartTime.ToUniversalTime().Ticks + ".lease"),
                    FileMode.Create, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose);
        }

        internal static void AssertNoInstances(string target, int allowedParent)
        {
            string dir = Path.Combine(Path.GetDirectoryName(UpdateJournal.GetJournalPath(target)), "instances");
            if (Directory.Exists(dir))
                foreach (string file in Directory.GetFiles(dir, "*.lease"))
                {
                    if (allowedParent > 0 && Path.GetFileName(file).StartsWith(allowedParent + "-", StringComparison.Ordinal)) continue;
                    try { using (var probe = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } }
                    catch (IOException) { throw new IOException("Another instance is using this installation. Close it before updating."); }
                }
            var processes = UpdateLock.FindRunningInstancesForTarget(target, Process.GetCurrentProcess().Id);
            try { foreach (var process in processes) if (process.Id != allowedParent) throw new IOException("Close all other TTSK instances before updating."); }
            finally { foreach (var process in processes) process.Dispose(); }
        }

        // Called while admission is locked, before loading any Tekla types. Recovery must not overwrite the running target EXE.
        public static int StartRecoveryFromStaging(string target)
        {
            var journal = UpdateJournal.LoadFromFile(UpdateJournal.GetJournalPath(target));
            if (journal == null) return 1;
            if (journal.State == UpdateTransactionState.Committed || journal.State == UpdateTransactionState.RolledBack)
            { UpdateJournal.DeleteJournal(UpdateJournal.GetJournalPath(target)); return RestartAfterExit(target); }
            string staging = journal.StagingDirectory;
            string error;
            if (string.IsNullOrEmpty(staging) || UpdateTransaction.Overlaps(target, staging) ||
                !UpdateChecksumManifest.ValidateDirectory(staging, ReleaseMetadata.LoadFromFile(Path.Combine(staging, "release.json")), out error, true))
                throw new InvalidDataException("Recovery staging unavailable. Keep backup at " + journal.BackupDirectory + "; restore manually with app closed.");
            using (var p = Process.Start(new ProcessStartInfo(Path.Combine(staging, "TTSK Dim Plates.exe")) {
                Arguments = ModeRecovery + " --target \"" + target + "\"", WorkingDirectory = staging, UseShellExecute = false })) { }
            return 0;
        }

        private static int RestartAfterExit(string target)
        {
            // A completed journal only needed cleanup; do not recursively launch while holding admission.
            ShowError("Phiên cập nhật đã kết thúc. Vui lòng mở lại TTSK.");
            return 0;
        }

        public static int ExecuteRecovery(string targetDir)
        {
            try
            {
                using (var gate = new UpdateLock(targetDir))
                {
                    if (!gate.TryAcquireUpdateLock(5000)) throw new IOException("Recovery target is locked.");
                    return RecoverLocked(targetDir);
                }
            }
            catch (Exception ex) { UpdateSecurity.Log(targetDir, "Recovery: " + ex); ShowError(ex.Message); return 1; }
        }

        private static int RecoverLocked(string targetDir)
        {
            string path = UpdateJournal.GetJournalPath(targetDir);
            var journal = UpdateJournal.LoadFromFile(path);
            if (journal == null) return 0;
            if (!Path.GetFullPath(journal.TargetDirectory).Equals(Path.GetFullPath(targetDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Journal target mismatch.");
            // Startup admission exits promptly after launching us; allow only a bounded wait for it.
            var wait = Stopwatch.StartNew();
            while (true)
            {
                try { AssertNoInstances(targetDir, 0); break; }
                catch (IOException) { if (wait.ElapsedMilliseconds >= 5000) throw; Thread.Sleep(100); }
            }
            UpdateTransaction.Rollback(journal);
            UpdateJournal.DeleteJournal(path);
            return 0;
        }

        private static void ShowError(string message)
        {
            Console.Error.WriteLine(message);
            if (!SilentMode) MessageBox.Show(message, "Cập nhật TTSK Dim Plates", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
