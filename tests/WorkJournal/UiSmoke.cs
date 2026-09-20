using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using System.Xml.Serialization;
using TTSK_AutoDim_Plates;

internal static class WorkJournalUiSmoke
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string name = new AssemblyName(e.Name).Name + ".dll";
            foreach (string dir in new[] { AppDomain.CurrentDomain.BaseDirectory, @"C:\Program Files\Tekla Structures\2025.0\bin" })
            {
                string file = Path.Combine(dir, name);
                if (File.Exists(file)) return Assembly.LoadFrom(file);
            }
            return null;
        };
        Run(Path.GetFullPath(args[0]), args.Length > 1 && args[1] == "dark");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Run(string root, bool dark)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string journal = Path.Combine(root, "ui-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(journal);
        using (var main = new MainForm())
        {
            var type = typeof(MainForm);
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (bool mode in new[] { true, false, true, false })
            {
                type.GetField("_darkMode", flags).SetValue(main, mode);
                type.GetMethod("ApplyTheme", flags).Invoke(main, null);
                foreach (string glyph in new[] { "workJournalIcon", "workJournalFortune" })
                {
                    var label = (Label)type.GetField(glyph, flags).GetValue(main);
                    Color expected = mode ? Color.FromArgb(226, 232, 240) : Color.White;
                    if (label.ForeColor != expected) throw new Exception("Footer theme regression: " + glyph);
                }
            }
            type.GetField("_darkMode", flags).SetValue(main, dark);
            type.GetField("workJournalFolder", flags).SetValue(main, journal);
            var session = (WorkSession)type.GetField("workSession", flags).GetValue(main);
            var completed = new WorkSession { Id = Guid.NewGuid().ToString("N"), Opened = DateTime.Today.AddHours(8), Closed = DateTime.Today.AddHours(17) };
            completed.Projects.Add(new WorkProject { Day = DateTime.Today, FirstSeen = completed.Opened, Number = "27-006",
                Name = "グレープストーン桶川工場　開発棟増築工事", ModelPath = "sample" });
            completed.Projects.Add(new WorkProject { Day = DateTime.Today, FirstSeen = DateTime.Today.AddHours(13), Number = "27-007",
                Name = "Dự án B (minh họa)", ModelPath = "sample-B" });
            using (var file = File.Create(Path.Combine(journal, completed.Id + ".xml"))) new XmlSerializer(typeof(WorkSession)).Serialize(file, completed);
            var prior = new WorkSession { Id = Guid.NewGuid().ToString("N"), Opened = DateTime.Today.AddDays(-1).AddHours(8), Closed = DateTime.Today.AddDays(-1).AddHours(18) };
            prior.Projects.Add(new WorkProject { Day = prior.Opened.Date, Number = "27-006", Name = completed.Projects[0].Name, ModelPath = "second-model-copy" });
            using (var file = File.Create(Path.Combine(journal, prior.Id + ".xml"))) new XmlSerializer(typeof(WorkSession)).Serialize(file, prior);
            int captures = 0;
            using (var timer = new Timer { Interval = 800 })
            {
                timer.Tick += delegate
                {
                    var report = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f != main && f.Text.Contains("Nhật ký"));
                    if (report == null) return;
                    timer.Stop();
                    report.Text = "KIỂM THỬ GIAO DIỆN — dữ liệu minh họa";
                    using (var bitmap = new Bitmap(report.Width, report.Height))
                    {
                        report.DrawToBitmap(bitmap, new Rectangle(0, 0, report.Width, report.Height));
                        bitmap.Save(Path.Combine(root, "work-journal-form" + (dark ? "-dark" : "-light") + ".png"));
                    }
                    var labels = Descendants(report).OfType<Label>().ToList();
                    var copyFields = Descendants(report).OfType<TextBox>().ToList();
                    if (copyFields.Count(l => l.Text == "27-006") != 1
                        || !labels.Any(l => l.Text == "17:00") || !labels.Any(l => l.Text == "18:00")) throw new Exception("Missing grouped project card.");
                    if (copyFields.Count != 4 || copyFields.Any(f => !f.ReadOnly || !f.ShortcutsEnabled || f.BorderStyle != BorderStyle.None))
                        throw new Exception("Project code/name must support selection and copying without native borders.");
                    foreach (var field in copyFields) { field.SelectAll(); if (field.SelectedText != field.Text) throw new Exception("Cannot select project text."); field.Select(0, 0); }
                    if (Descendants(report).Any(c => c is DateTimePicker || c is MonthCalendar)) throw new Exception("Native date chrome remains.");
                    var datePicker = Descendants(report).First(c => c.GetType().Name == "JournalDatePicker");
                    datePicker.GetType().GetMethod("OpenCalendar", flags).Invoke(datePicker, null);
                    Application.DoEvents();
                    var popup = (ToolStripDropDown)datePicker.GetType().GetField("popup", flags).GetValue(datePicker);
                    using (var bitmap = new Bitmap(popup.Width, popup.Height))
                    {
                        popup.DrawToBitmap(bitmap, new Rectangle(0, 0, popup.Width, popup.Height));
                        bitmap.Save(Path.Combine(root, "work-journal-calendar" + (dark ? "-dark" : "-light") + ".png"));
                    }
                    var calendar = ((ToolStripControlHost)popup.Items[0]).Control;
                    var dayButton = Descendants(calendar).OfType<Button>().First(b => b.Tag is DateTime && ((DateTime)b.Tag).Day == 2);
                    var selected = (DateTime)dayButton.Tag;
                    dayButton.PerformClick();
                    if ((DateTime)datePicker.GetType().GetProperty("Value", flags).GetValue(datePicker) != selected) throw new Exception("Custom calendar did not select date.");
                    if (popup.Visible) throw new Exception("Calendar should close on selection.");
                    if (report.FormBorderStyle != FormBorderStyle.None) throw new Exception("Native title bar remains.");
                    var maximizeButton = Descendants(report).OfType<Button>().First(b => b.AccessibleName == "Phóng to / khôi phục");
                    maximizeButton.PerformClick();
                    if (report.WindowState != FormWindowState.Maximized) throw new Exception("Custom maximize failed.");
                    maximizeButton.PerformClick();
                    if (report.WindowState != FormWindowState.Normal) throw new Exception("Custom restore failed.");
                    if (labels.Any(l => l.Text.Contains("Phiên") || l.Text.Contains("quy đổi"))) throw new Exception("Verbose content remains.");
                    report.Size = report.MinimumSize;
                    Application.DoEvents();
                    foreach (var flow in Descendants(report).OfType<FlowLayoutPanel>())
                        foreach (Control child in flow.Controls)
                            if (child.Bottom > flow.ClientSize.Height || child.Right > flow.ClientSize.Width)
                                throw new Exception("Clipped filter control: " + child.Text);
                    var scrollArea = Descendants(report).First(c => c.GetType().Name == "JournalScrollArea");
                    scrollArea.GetType().GetMethod("ScrollWheel", flags).Invoke(scrollArea,
                        new object[] { scrollArea, new MouseEventArgs(MouseButtons.None, 0, 0, 0, -120) });
                    if ((int)scrollArea.GetType().GetField("offset", flags).GetValue(scrollArea) <= 0)
                        throw new Exception("Custom scrolling did not reach content below the viewport.");
                    captures++;
                    report.Close();
                };
                timer.Start();
                type.GetMethod("ShowWorkJournal", flags).Invoke(main, null);
            }
            if (captures != 1) throw new Exception("Report did not open.");
            Console.WriteLine("UI PASS: grouped cards, Japanese names, repeated project shown once, filter layout at minimum size.");
            foreach (var project in session.Projects)
                Console.WriteLine("READ-ONLY TEKLA: " + project.Number + " | " + project.Name);
            main.Show();
            Application.DoEvents();
            using (var bitmap = new Bitmap(main.Width, main.Height))
            {
                main.DrawToBitmap(bitmap, new Rectangle(0, 0, main.Width, main.Height));
                bitmap.Save(Path.Combine(root, "work-journal-main.png"));
            }
            FormClosingEventHandler cancel = (s, e) => e.Cancel = true;
            main.FormClosing += cancel;
            main.Close();
            if (session.Closed.HasValue) throw new Exception("Cancelled close must not record an end time.");
            main.FormClosing -= cancel;
            main.Close();
            if (!session.Closed.HasValue) throw new Exception("Accepted close must record an end time.");
            using (var file = File.OpenRead(Path.Combine(journal, session.Id + ".xml")))
            {
                var saved = (WorkSession)new XmlSerializer(typeof(WorkSession)).Deserialize(file);
                if (saved.Closed != session.Closed) throw new Exception("Closing timestamp not persisted.");
            }
            Console.WriteLine("LIFECYCLE PASS: cancelled close ignored; accepted close durably persisted in isolated test folder.");
        }
    }

    static System.Collections.Generic.IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
}
