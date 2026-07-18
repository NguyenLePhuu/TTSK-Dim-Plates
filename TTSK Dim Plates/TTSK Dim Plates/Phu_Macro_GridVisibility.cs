#pragma warning disable 1633 // Unrecognized #pragma directive
#pragma reference "Tekla.Macros.Akit"
#pragma reference "Tekla.Macros.Runtime"
#pragma warning restore 1633 // Unrecognized #pragma directive

namespace UserMacros
{
    public sealed class Macro
    {
        private const string CommandFileName = "TTSK_GridVisibility.command";

        [Tekla.Macros.Runtime.MacroEntryPointAttribute()]
        public static void Run(Tekla.Macros.Runtime.IMacroRuntime runtime)
        {
            string commandPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                CommandFileName);

            try
            {
                string command = System.IO.File.ReadAllText(commandPath).Trim();

                Tekla.Macros.Akit.IAkitScriptHost akit =
                    runtime.Get<Tekla.Macros.Akit.IAkitScriptHost>();

                if (string.Equals(command, "OPEN", System.StringComparison.OrdinalIgnoreCase))
                {
                    akit.ValueChange("view_dial", "gr_view_grid_on", "1");
                    akit.ValueChange("view_dial", "gr_bottom_middle", "1");
		    akit.ValueChange("view_dial", "gr_vnp_collect_by", "0");
                }
                else if (string.Equals(command, "FIT", System.StringComparison.OrdinalIgnoreCase))
                {
                    akit.ValueChange("view_dial", "gr_bottom_middle", "0");
                    akit.ValueChange("view_dial", "gr_view_grid_on", "0");
                }
                else if (string.Equals(command, "FIT_COMPLETE", System.StringComparison.OrdinalIgnoreCase))
                {
                    akit.ValueChange("view_dial", "gr_vnp_collect_by", "4");
                }
                else if (string.Equals(command, "MARK_OFFSET", System.StringComparison.OrdinalIgnoreCase))
                {
                    akit.ValueChange("view_dial", "gr_view_grid_on", "1");
                    akit.ValueChange("view_dial", "gr_top_middle", "1");
                    akit.ValueChange("view_dial", "gr_bottom_middle", "0");
		    akit.ValueChange("view_dial", "gr_vnp_collect_by", "4");
                }
                else
                {
                    System.IO.File.WriteAllText(
                        commandPath,
                        "ERROR|Lệnh Grid Visibility không hợp lệ.");
                    return;
                }

                akit.PushButton("view_modify", "view_dial");
                akit.PushButton("view_apply", "view_dial");
                akit.PushButton("view_ok", "view_dial");

                System.IO.File.WriteAllText(commandPath, "DONE");
            }
            catch (System.Exception ex)
            {
                try
                {
                    System.IO.File.WriteAllText(
                        commandPath,
                        "ERROR|" + ex.GetType().Name + ": " + ex.Message);
                }
                catch
                {
                }
            }
        }
    }
}
