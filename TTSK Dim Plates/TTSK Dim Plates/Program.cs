using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace TTSK_AutoDim_Plates
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            string teklaBinPath = FindTeklaBinPath();
            if (string.IsNullOrEmpty(teklaBinPath))
            {
                MessageBox.Show(
                    "Không tìm thấy Tekla Structures 2025.\r\n\r\n"
                        + "Hãy cài Tekla Structures 2025 SP7 hoặc đặt biến môi trường "
                        + "TeklaBinPath trỏ tới thư mục bin của Tekla.",
                    "TTSK Dim Plates",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return 1;
            }

            AppDomain.CurrentDomain.AssemblyResolve += (sender, resolveArgs) =>
                ResolveTeklaAssembly(resolveArgs, teklaBinPath);

            if (args != null && args.Length == 2 && args[0] == "--snapshot-png-worker")
                return Tekla.Technology.Akit.UserScript.PHU_CopyDrawingSnapshot_Temp.RenderFolder(args[1], teklaBinPath);

            if (
                args != null
                && args.Length > 0
                && String.Equals(
                    args[0],
                    "--hole-mark-post-dim-worker",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return Tekla
                    .Technology
                    .Akit
                    .UserScript
                    .PHU_HoleMarkPostDimensionService
                    .RunWorker(args);
            }

            // Chế độ Worker cách ly tiến trình để in màu qua Tekla DpmPrinter mà không làm thay đổi DPI của UI chính
            if (
                args != null
                && args.Length >= 3
                && String.Equals(
                    args[0],
                    "--print-color-worker",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return DrawingPdfPrinter.ExecuteColorPrintWorker(args[1], args[2]);
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }

        private static string FindTeklaBinPath()
        {
            string configuredPath = Environment.GetEnvironmentVariable("TeklaBinPath");
            string[] candidates =
            {
                configuredPath,
                @"C:\Program Files\Tekla Structures\2025.0\bin",
                @"C:\TeklaStructures\2025.0\bin"
            };

            foreach (string candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                if (
                    File.Exists(Path.Combine(candidate, "Tekla.Structures.dll"))
                    && File.Exists(Path.Combine(candidate, "Tekla.Structures.Drawing.dll"))
                    && File.Exists(Path.Combine(candidate, "Tekla.Structures.Model.dll"))
                )
                {
                    return candidate;
                }
            }

            return null;
        }

        private static Assembly ResolveTeklaAssembly(ResolveEventArgs args, string teklaBinPath)
        {
            string assemblyName = new AssemblyName(args.Name).Name;
            if (string.IsNullOrEmpty(assemblyName))
            {
                return null;
            }

            string assemblyPath = Path.Combine(teklaBinPath, assemblyName + ".dll");
            if (!File.Exists(assemblyPath))
            {
                return null;
            }

            try
            {
                return Assembly.LoadFrom(assemblyPath);
            }
            catch (FileLoadException)
            {
                return null;
            }
            catch (BadImageFormatException)
            {
                return null;
            }
        }
    }
}
