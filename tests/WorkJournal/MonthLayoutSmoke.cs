using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using TTSK_AutoDim_Plates;

internal static class MonthLayoutSmoke
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string root = Path.GetFullPath(args[0]);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        DateTime first = new DateTime(2026, 3, 1);
        int checks = 0;
        foreach (bool dark in new[] { false, true })
        foreach (int count in new[] { 22, 31 })
        {
            var sessions = Enumerable.Range(0, count).Select(i => new WorkSession {
                Id = Guid.NewGuid().ToString("N"), Opened = first.AddDays(i).AddHours(8), Closed = first.AddDays(i).AddHours(17).AddMinutes(i),
                Projects = new List<WorkProject> { new WorkProject { Day = first.AddDays(i), Number = "27-006",
                    Name = "グレープストーン桶川工場　開発棟増築工事" } }
            }).ToList();
            var type = typeof(WorkSession).Assembly.GetType("TTSK_AutoDim_Plates.WorkJournalReportForm");
            using (var form = (Form)Activator.CreateInstance(type, flags, null,
                new object[] { new Func<List<WorkSession>>(() => sessions), dark }, CultureInfo.InvariantCulture))
            {
                foreach (string field in new[] { "from", "to" })
                {
                    object picker = type.GetField(field, flags).GetValue(form);
                    picker.GetType().GetProperty("Value", flags).SetValue(picker, field == "from" ? first : first.AddDays(30));
                }
                form.Text = "MINH HỌA · " + count + " ngày · " + (dark ? "Dark" : "Light");
                form.Show();
                Application.DoEvents();
                foreach (Size size in new[] { new Size(960, 750), new Size(780, 480), new Size(1280, 900) })
                {
                    form.Size = size;
                    Application.DoEvents();
                    var all = Descendants(form).ToList();
                    var next = all.OfType<Button>().Single(b => b.AccessibleName == "Các ngày tiếp theo");
                    var previous = all.OfType<Button>().Single(b => b.AccessibleName == "Các ngày trước");
                    while (previous.Enabled) previous.PerformClick();
                    foreach (var arrow in new[] { previous, next })
                    {
                        Color expected = (Color)arrow.Parent.GetType().GetField("Fill", flags).GetValue(arrow.Parent);
                        foreach (bool hover in new[] { false, true })
                        {
                            arrow.GetType().GetMethod(hover ? "OnMouseEnter" : "OnMouseLeave", flags).Invoke(arrow, new object[] { EventArgs.Empty });
                            using (var bitmap = new Bitmap(arrow.Width, arrow.Height))
                            {
                                arrow.DrawToBitmap(bitmap, new Rectangle(Point.Empty, arrow.Size));
                                foreach (var corner in new[] { new Point(0, 0), new Point(arrow.Width - 1, 0),
                                    new Point(0, arrow.Height - 1), new Point(arrow.Width - 1, arrow.Height - 1) })
                                    if (bitmap.GetPixel(corner.X, corner.Y).ToArgb() != expected.ToArgb())
                                        throw new Exception("Arrow corner does not match card background.");
                            }
                        }
                        arrow.GetType().GetMethod("OnMouseLeave", flags).Invoke(arrow, new object[] { EventArgs.Empty });
                    }
                    var visited = new HashSet<string>();
                    int pages = 0;
                    do
                    {
                        Application.DoEvents();
                        var tiles = Descendants(form).Where(c => c.GetType().Name == "JournalSurface"
                            && c.AccessibleName != null && c.Visible).ToList();
                        if (tiles.Count > 12 || tiles.Count == 0 || tiles.Select(c => c.Top).Distinct().Count() > 2)
                            throw new Exception("More than two rows or unexpected empty page.");
                        foreach (var tile in tiles)
                        {
                            if (tile.Right > tile.Parent.ClientSize.Width || tile.Bottom > tile.Parent.ClientSize.Height
                                || tile.Left < 0 || tile.Top < 0) throw new Exception("Tile overflow.");
                            if (tile.Parent.Height > 400) throw new Exception("Monthly card grew too tall.");
                            foreach (Control label in tile.Controls)
                                if (label.Right > tile.ClientSize.Width || label.Bottom > tile.ClientSize.Height) throw new Exception("Clipped date or time.");
                            if (!visited.Add(tile.AccessibleName)) throw new Exception("Repeated date across pages.");
                        }
                        pages++;
                        if (!next.Enabled) break;
                        next.PerformClick();
                    } while (pages < 10);
                    if (visited.Count != count) throw new Exception("Pagination lost a date.");
                    string text = (string)type.GetField("reportText", flags).GetValue(form);
                    foreach (var s in sessions)
                        if (!text.Contains(s.Closed.Value.ToString("dd/MM/yyyy"))) throw new Exception("Export omitted a paginated date.");
                    while (previous.Enabled) previous.PerformClick();
                    if (size.Width == 960)
                    {
                        using (var bitmap = new Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                            bitmap.Save(Path.Combine(root, "month-" + count + (dark ? "-dark" : "-light") + ".png"));
                        }
                    }
                    Console.WriteLine("PASS " + count + " days / " + size.Width + "px / " + (dark ? "dark" : "light") + " / " + pages + " pages");
                    checks++;
                }
                form.Close();
            }
        }
        Console.WriteLine("PASS: " + checks + " monthly layout cases; complete export, all dates reachable, two-row limit, no clipped tiles/text.");
    }
    static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
}
