using System;
using System.IO;
using System.Reflection;
using TTSK_AutoDim_Plates.Updater;
[assembly: AssemblyVersion("1.0.0.0")]
namespace TTSK_AutoDim_Plates
{
    // Test-only normal entry point. Production Program.Main and every updater source are compiled unchanged.
    internal static class ProgramTeklaStartup
    {
        public static int Run(string[] args)
        {
            string target = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            if (args.Length == 2 && args[0] == "--fixture-parent")
            {
                TTSK_Dim_Plates.Properties.Settings.Default.ManualScaleDenominator = "175";
                TTSK_Dim_Plates.Properties.Settings.Default.Save();
                var metadata = ReleaseMetadata.LoadFromFile(Path.Combine(args[1], "release.json"));
                var manager = new UpdateManager(target);
                return manager.LaunchWorkerAndHandoff(args[1], new GitHubReleaseInfo {
                    Version = UpdateVersion.Parse(metadata.version), TagName = metadata.tag
                }, () => true).GetAwaiter().GetResult() ? 0 : 1;
            }
            File.WriteAllText(Path.Combine(target, "fixture-restarted.txt"), UpdateManager.ResolveCurrentVersion(target).ToString());
            File.WriteAllText(Path.Combine(target, "fixture-user-setting.txt"), TTSK_Dim_Plates.Properties.Settings.Default.ManualScaleDenominator);
            return 0;
        }
    }
}
