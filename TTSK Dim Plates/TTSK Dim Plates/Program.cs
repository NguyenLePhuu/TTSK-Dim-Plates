using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace TTSK_AutoDim_Plates
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            string teklaBinPath = FindTeklaBinPath();
            if (string.IsNullOrEmpty(teklaBinPath))
            {
                MessageBox.Show(
                    "Không tìm thấy Tekla Structures 2025.\r\n\r\n" +
                    "Hãy cài Tekla Structures 2025 SP7 hoặc đặt biến môi trường " +
                    "TeklaBinPath trỏ tới thư mục bin của Tekla.",
                    "TTSK Dim Plates",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            AppDomain.CurrentDomain.AssemblyResolve +=
                (sender, args) => ResolveTeklaAssembly(args, teklaBinPath);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
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

                if (File.Exists(Path.Combine(candidate, "Tekla.Structures.dll")) &&
                    File.Exists(Path.Combine(candidate, "Tekla.Structures.Drawing.dll")) &&
                    File.Exists(Path.Combine(candidate, "Tekla.Structures.Model.dll")))
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
