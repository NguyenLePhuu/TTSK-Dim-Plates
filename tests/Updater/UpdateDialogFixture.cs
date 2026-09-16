using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using TTSK_AutoDim_Plates.Updater;

internal static class UpdateDialogFixture
{
    private static T Field<T>(UpdateDialog dialog, string name) where T : class
    {
        return (T)typeof(UpdateDialog).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dialog);
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();
            Directory.CreateDirectory(args[0]);
            foreach (bool dark in new[] { false, true })
            {
                int downloads = 0, applied = 0;
                var release = new GitHubReleaseInfo { Source = "GoogleDrive", Version = new UpdateVersion(1, 0, 10),
                    TagName = "v1.0.10", ZipSizeBytes = 4 * 1024 * 1024, Body = "Cải thiện cập nhật tự động." };
                using (var dialog = new UpdateDialog(release, new UpdateVersion(1, 0, 9), dark, () => true,
                    (info, progress, ct) => {
                        downloads++;
                        if (downloads > 1) return Task.FromResult("fixture-staging");
                        // A queued progress event must not erase the failure/retry state.
                        progress.Report(new UpdateDownloadProgress { StatusMessage = "stale-progress" });
                        return Task.FromException<string>(new IOException("private-provider-diagnostic"));
                    }, (path, info) => { applied++; return Task.CompletedTask; }))
                {
                    dialog.StartAutomatically = true;
                    dialog.StartPosition = FormStartPosition.Manual;
                    dialog.Location = new Point(-2000, -2000);
                    dialog.Show();
                    Application.DoEvents();
                    var retry = Field<Button>(dialog, "btnUpdate");
                    string status = Field<Label>(dialog, "lblStatus").Text;
                    string version = Field<Label>(dialog, "lblVersionInfo").Text;
                    if (!retry.Enabled || retry.Text != "Thử lại" || status.Contains("private-provider") ||
                        status.Contains("stale-progress") || !status.Contains("Thử lại"))
                        throw new Exception("Download failure did not leave a clean retry state.");
                    if (!version.Contains("v1.0.10") || version.Contains("Google") || version.Contains("GitHub"))
                        throw new Exception("Version display leaked provider details or lost selected version.");
                    using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
                    {
                        dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        bitmap.Save(Path.Combine(args[0], dark ? "update-retry-dark.png" : "update-retry-light.png"));
                    }
                    retry.PerformClick();
                    Application.DoEvents();
                    if (downloads != 2 || applied != 1) throw new Exception("Retry did not complete download and handoff.");
                }
            }
            Console.WriteLine("PASS: inline failure, stale progress ignored, retry succeeds, light/dark rendered.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }
}
