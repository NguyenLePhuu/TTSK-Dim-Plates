using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using TTSK_AutoDim_Plates;

internal static class LiveArrowPreview
{
    [STAThread]
    static void Main()
    {
        string repo = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", ".."));
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            foreach (string extension in new[] { ".exe", ".dll" })
            {
                string path = Path.Combine(repo, "portable", new AssemblyName(e.Name).Name + extension);
                if (File.Exists(path)) return Assembly.LoadFrom(path);
            }
            return null;
        };
        ShowPreviews();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void ShowPreviews()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var sessions = new List<WorkSession>();
        DateTime first = new DateTime(2026, 3, 1);
        for (int i = 0; i < 31; i++) sessions.Add(new WorkSession {
            Id = Guid.NewGuid().ToString("N"), Opened = first.AddDays(i).AddHours(8), Closed = first.AddDays(i).AddHours(17).AddMinutes(i),
            Projects = new List<WorkProject> { new WorkProject { Day = first.AddDays(i), Number = "27-006",
                Name = "グレープストーン桶川工場　開発棟増築工事" } }
        });
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var type = typeof(WorkSession).Assembly.GetType("TTSK_AutoDim_Plates.WorkJournalReportForm");
        foreach (bool dark in new[] { false, true })
        using (var form = (Form)Activator.CreateInstance(type, flags, null,
            new object[] { new Func<List<WorkSession>>(() => sessions), dark }, CultureInfo.InvariantCulture))
        {
            foreach (string field in new[] { "from", "to" })
            {
                var picker = type.GetField(field, flags).GetValue(form);
                picker.GetType().GetProperty("Value", flags).SetValue(picker, field == "from" ? first : first.AddDays(30));
            }
            form.Text = "KIỂM TRA TRỰC TIẾP · " + (dark ? "DARK" : "LIGHT") + " · PORTABLE · dữ liệu minh họa";
            Application.Run(form);
        }
    }
}
