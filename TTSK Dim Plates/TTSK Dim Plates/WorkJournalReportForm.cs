using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using System.Text;
using System.Runtime.InteropServices;

namespace TTSK_AutoDim_Plates
{
    internal sealed class WorkJournalReportForm : Form
    {
        private readonly Func<List<WorkSession>> load;
        private readonly JournalDatePicker from, to;
        private readonly bool darkMode;
        private readonly TableLayoutPanel cards;
        private readonly Label status;
        private readonly Button export;
        private readonly Color surface, ink, muted, accent, rowColor, actionText, frameBorder;
        private readonly Font headingFont = new Font("Segoe UI", 24F, FontStyle.Bold);
        private readonly Font captionFont = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        private readonly Font codeFont = new Font("Segoe UI", 21F, FontStyle.Bold);
        private readonly Font nameFont = new Font("Segoe UI", 12F);
        private readonly Font dateFont = new Font("Segoe UI", 9.5F);
        private readonly Font timeFont = new Font("Segoe UI", 20F, FontStyle.Bold);
        private readonly Timer dateTimer;
        private DateTime shownDay = DateTime.Today;
        private string reportText = "";

        internal WorkJournalReportForm(Func<List<WorkSession>> load, bool dark)
        {
            this.load = load;
            darkMode = dark;
            surface = dark ? Color.FromArgb(28, 31, 37) : Color.White;
            ink = dark ? Color.FromArgb(241, 239, 235) : Color.FromArgb(32, 42, 52);
            muted = dark ? Color.FromArgb(157, 160, 169) : Color.FromArgb(114, 123, 132);
            accent = dark ? Color.FromArgb(222, 150, 89) : Color.FromArgb(30, 58, 138);
            actionText = dark ? Color.FromArgb(24, 26, 31) : Color.White;
            frameBorder = dark ? Color.FromArgb(73, 56, 43) : Color.FromArgb(205, 212, 223);
            rowColor = dark ? Color.FromArgb(43, 50, 61) : Color.FromArgb(237, 243, 252);
            Text = "TTSK · Nhật ký dự án";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(960, 750);
            MinimumSize = new Size(780, 480);
            Font = new Font("Segoe UI", 10F);
            BackColor = dark ? Color.FromArgb(19, 21, 26) : Color.FromArgb(242, 243, 241);
            ForeColor = ink;
            ShowIcon = false;
            FormBorderStyle = FormBorderStyle.None;
            Padding = new Padding(1);
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
            var frame = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            frame.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            frame.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            frame.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(frame);
            frame.Controls.Add(CreateTitleBar(), 0, 0);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(30, 26, 30, 14), ColumnCount = 1, RowCount = 4 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            layout.Margin = Padding.Empty;
            frame.Controls.Add(layout, 0, 1);
            var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
            heading.Controls.Add(new Label { Text = "Nhật ký dự án", AutoSize = true, Font = headingFont, ForeColor = ink, Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 0);
            export = MakeButton("Xuất TXT", true);
            export.Dock = DockStyle.Fill;
            export.Margin = new Padding(0, 5, 0, 11);
            heading.Controls.Add(export, 1, 0);
            layout.Controls.Add(heading, 0, 0);

            var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 12), WrapContents = true };
            from = MakeDate(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1));
            to = MakeDate(DateTime.Today);
            from.AccessibleName = "Từ ngày";
            to.AccessibleName = "Đến ngày";
            var range = new JournalSurface { Fill = surface, Radius = 16, Size = new Size(362, 66), Margin = new Padding(0, 0, 14, 0) };
            from.SetBounds(12, 4, 154, 58);
            to.SetBounds(196, 4, 154, 58);
            range.Controls.AddRange(new Control[] { from, to, new Label { Text = "→", Location = new Point(170, 26),
                Size = new Size(24, 24), ForeColor = muted, BackColor = surface, TextAlign = ContentAlignment.MiddleCenter } });
            var week = MakeButton("Tuần này", false);
            var month = MakeButton("Tháng này", false);
            var refresh = MakeButton("Làm mới", false);
            week.Margin = month.Margin = refresh.Margin = new Padding(0, 16, 8, 0);
            filters.Controls.AddRange(new Control[] { range, week, month, refresh });
            layout.Controls.Add(filters, 0, 1);

            var scroll = new JournalScrollArea { Dock = DockStyle.Fill, Margin = Padding.Empty, ForeColor = muted };
            cards = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1, RowCount = 0, Margin = Padding.Empty, Padding = new Padding(0, 0, 2, 0) };
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            scroll.SetContent(cards);
            layout.Controls.Add(scroll, 0, 2);
            status = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = muted, TextAlign = ContentAlignment.BottomLeft, Margin = Padding.Empty };
            layout.Controls.Add(status, 0, 3);

            from.ValueChanged += delegate { Render(); };
            to.ValueChanged += delegate { Render(); };
            week.Click += delegate { from.Value = DateTime.Today.AddDays(-(((int)DateTime.Today.DayOfWeek + 6) % 7)); to.Value = DateTime.Today; };
            month.Click += delegate { from.Value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1); to.Value = DateTime.Today; };
            refresh.Click += delegate { Render(); };
            export.Click += delegate { Export(); };
            // Follow today's date across midnight only when the user was viewing through today.
            dateTimer = new Timer { Interval = 1000 };
            dateTimer.Tick += delegate
            {
                if (shownDay == DateTime.Today) return;
                bool followToday = to.Value.Date == shownDay;
                shownDay = DateTime.Today;
                if (followToday) to.Value = shownDay;
                else Render();
            };
            dateTimer.Start();
            Render();
        }

        private JournalDatePicker MakeDate(DateTime value)
        {
            return new JournalDatePicker(surface, ink, muted, accent, actionText, rowColor) {
                Value = value, Size = new Size(146, 34), Margin = new Padding(0, 0, 16, 4) };
        }

        private Control CreateTitleBar()
        {
            var bar = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = surface };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 132, Margin = Padding.Empty, WrapContents = false };
            var minimize = new WindowButton { Kind = 0, AccessibleName = "Thu nhỏ" };
            var maximize = new WindowButton { Kind = 1, AccessibleName = "Phóng to / khôi phục" };
            var close = new WindowButton { Kind = 2, AccessibleName = "Đóng" };
            foreach (var button in new[] { minimize, maximize, close })
            {
                button.Size = new Size(44, 38);
                button.ForeColor = ink;
                button.BackColor = surface;
                button.HoverColor = rowColor;
                buttons.Controls.Add(button);
            }
            minimize.Click += delegate { WindowState = FormWindowState.Minimized; };
            maximize.Click += delegate { ToggleMaximize(); };
            close.Click += delegate { Close(); };
            var title = new Label { Text = Text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(16, 0, 0, 0), ForeColor = muted, AutoEllipsis = true };
            TextChanged += delegate { title.Text = Text; };
            MouseEventHandler drag = (s, e) =>
            {
                if (e.Button == MouseButtons.Left && e.Clicks == 1)
                {
                    ReleaseCapture();
                    SendMessage(Handle, 0x00A1, new IntPtr(2), IntPtr.Zero);
                }
            };
            title.MouseDown += drag;
            bar.MouseDown += drag;
            title.DoubleClick += delegate { ToggleMaximize(); };
            bar.DoubleClick += delegate { ToggleMaximize(); };
            bar.Controls.Add(title);
            bar.Controls.Add(buttons);
            return bar;
        }

        private void ToggleMaximize()
        {
            MaximizedBounds = Screen.FromControl(this).WorkingArea;
            WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        }

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(frameBorder)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg != 0x0084 || WindowState == FormWindowState.Maximized) return;
            long point = m.LParam.ToInt64();
            Point p = PointToClient(new Point((short)(point & 0xffff), (short)((point >> 16) & 0xffff)));
            const int edge = 6;
            bool left = p.X < edge, right = p.X >= ClientSize.Width - edge;
            bool top = p.Y < edge, bottom = p.Y >= ClientSize.Height - edge;
            if (top) m.Result = new IntPtr(left ? 13 : right ? 14 : 12);
            else if (bottom) m.Result = new IntPtr(left ? 16 : right ? 17 : 15);
            else if (left || right) m.Result = new IntPtr(left ? 10 : 11);
        }

        private Button MakeButton(string text, bool primary)
        {
            var button = new JournalButton { Text = text, FlatStyle = FlatStyle.Flat, Size = new Size(94, 34),
                Fill = primary ? accent : surface, ForeColor = primary ? actionText : ink,
                Cursor = Cursors.Hand, Margin = new Padding(0, 0, 8, 6), UseVisualStyleBackColor = false };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        private void Render()
        {
            cards.SuspendLayout();
            try
            {
                while (cards.Controls.Count > 0) { var child = cards.Controls[0]; cards.Controls.Remove(child); child.Dispose(); }
                cards.RowStyles.Clear();
                cards.RowCount = 0;
                reportText = "";
                export.Enabled = false;
                status.Text = "";
                if (from.Value.Date > to.Value.Date) { status.Text = "Khoảng ngày không hợp lệ."; return; }
                var sessions = load();
                var groups = WorkJournal.GroupProjects(sessions, from.Value.Date, to.Value.Date);
                reportText = WorkJournal.Report(sessions, from.Value.Date, to.Value.Date);
                foreach (var project in groups)
                {
                    int index = cards.RowCount++;
                    cards.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    cards.Controls.Add(ProjectCard(project), 0, index);
                }
                export.Enabled = groups.Count > 0;
            }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { cards.ResumeLayout(true); }
        }

        private Control ProjectCard(WorkProjectReport project)
        {
            var card = new JournalSurface { Dock = DockStyle.Top, Fill = surface, Radius = 18,
                Outline = darkMode ? Color.FromArgb(78, 87, 101) : Color.FromArgb(180, 195, 216), Elevated = true,
                Margin = new Padding(0, 0, 0, 18), Height = 230 };
            var caption = new Label { Text = "PROJECT NUMBER", AutoSize = true, ForeColor = muted,
                Font = captionFont, BackColor = surface };
            var code = new TextBox { Text = project.Number, ForeColor = accent, ReadOnly = true, BorderStyle = BorderStyle.None,
                ShortcutsEnabled = true, Font = codeFont, BackColor = surface, AccessibleName = "Project number", Cursor = Cursors.IBeam };
            var name = new TextBox { Text = project.Name, ForeColor = ink, ReadOnly = true, BorderStyle = BorderStyle.None,
                ShortcutsEnabled = true, Multiline = true, WordWrap = true, Font = nameFont, BackColor = surface,
                AccessibleName = "Tên dự án", Cursor = Cursors.IBeam };
            var daysCaption = new Label { Text = "NGÀY / GIỜ ĐÓNG", AutoSize = true, ForeColor = muted,
                Font = captionFont, BackColor = surface };
            card.Controls.AddRange(new Control[] { caption, code, name, daysCaption });
            var previous = MakeButton("‹", false);
            var next = MakeButton("›", false);
            ((JournalButton)previous).Fill = rowColor;
            ((JournalButton)next).Fill = rowColor;
            previous.AccessibleName = "Các ngày trước";
            next.AccessibleName = "Các ngày tiếp theo";
            var pageLabel = new Label { ForeColor = muted, BackColor = surface, TextAlign = ContentAlignment.MiddleCenter };
            card.Controls.AddRange(new Control[] { previous, pageLabel, next });
            var tiles = new List<JournalSurface>();
            foreach (var day in project.Days)
            {
                var tile = new JournalSurface { Fill = rowColor, Radius = 12, Height = 74,
                    Outline = darkMode ? Color.FromArgb(69, 83, 102) : Color.FromArgb(202, 216, 237),
                    AccessibleName = day.Day.ToString("dd/MM/yyyy") };
                tile.Controls.Add(new Label { Text = day.Day.ToString("dd/MM/yyyy"), Location = new Point(14, 9),
                    AutoSize = true, Font = dateFont, ForeColor = muted, BackColor = rowColor });
                tile.Controls.Add(new Label { Text = day.Closed.ToString("HH:mm"), Location = new Point(10, 29),
                    AutoSize = true, Font = timeFont, ForeColor = ink, BackColor = rowColor });
                tiles.Add(tile);
                card.Controls.Add(tile);
            }
            int firstVisible = 0, pageSize = 10;
            Action arrange = () =>
            {
                int width = Math.Max(240, card.ClientSize.Width - 48);
                caption.Location = new Point(24, 23);
                code.SetBounds(20, 42, 156, 46);
                int nameWidth = Math.Max(80, width - 172);
                int nameHeight = Math.Max(40, TextRenderer.MeasureText(project.Name, name.Font,
                    new Size(nameWidth, int.MaxValue), TextFormatFlags.WordBreak).Height);
                name.SetBounds(196, 30, nameWidth, nameHeight);
                int top = Math.Max(100, 42 + nameHeight);
                daysCaption.Location = new Point(24, top);
                previous.SetBounds(card.ClientSize.Width - 136, top - 5, 30, 26);
                pageLabel.SetBounds(card.ClientSize.Width - 104, top - 5, 44, 26);
                next.SetBounds(card.ClientSize.Width - 58, top - 5, 30, 26);
                top += 32;
                int columns = Math.Max(1, Math.Min(6, (width + 10) / 154));
                pageSize = columns * 2;
                firstVisible = Math.Min((firstVisible / pageSize) * pageSize, ((tiles.Count - 1) / pageSize) * pageSize);
                int count = Math.Min(pageSize, tiles.Count - firstVisible);
                bool paged = tiles.Count > pageSize;
                previous.Visible = pageLabel.Visible = next.Visible = paged;
                previous.Enabled = firstVisible > 0;
                next.Enabled = firstVisible + pageSize < tiles.Count;
                pageLabel.Text = (firstVisible / pageSize + 1) + " / " + ((tiles.Count + pageSize - 1) / pageSize);
                int tileWidth = (width - (columns - 1) * 10) / columns;
                for (int i = 0; i < tiles.Count; i++)
                {
                    bool visible = i >= firstVisible && i < firstVisible + count;
                    tiles[i].Visible = visible;
                    if (visible) { int j = i - firstVisible; tiles[i].SetBounds(24 + (j % columns) * (tileWidth + 10), top + (j / columns) * 84, tileWidth, 74); }
                }
                int height = top + ((count + columns - 1) / columns) * 84 + 22;
                if (card.Height != height) card.Height = height;
            };
            previous.Click += delegate { firstVisible = Math.Max(0, firstVisible - pageSize); arrange(); };
            next.Click += delegate { firstVisible = Math.Min(tiles.Count - 1, firstVisible + pageSize); arrange(); };
            card.SizeChanged += delegate { arrange(); };
            arrange();
            return card;
        }

        private void Export()
        {
            Render();
            if (!export.Enabled) return;
            using (var dialog = new SaveFileDialog { Filter = "Text UTF-8 (*.txt)|*.txt", DefaultExt = "txt", AddExtension = true,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                FileName = "TTSK_" + from.Value.ToString("yyyyMMdd") + "_" + to.Value.ToString("yyyyMMdd") + ".txt" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { File.WriteAllText(dialog.FileName, reportText, new UTF8Encoding(true)); status.Text = "Đã xuất TXT."; }
                catch (Exception ex) { status.Text = "Không xuất được tệp: " + ex.Message; }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && dateTimer != null) dateTimer.Dispose();
            base.Dispose(disposing);
            if (disposing)
            {
                headingFont.Dispose(); captionFont.Dispose(); codeFont.Dispose();
                nameFont.Dispose(); dateFont.Dispose(); timeFont.Dispose();
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int dark = darkMode ? 1 : 0;
            try { DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        private sealed class JournalDatePicker : Control
        {
            private readonly Color fill, ink, muted, accent, actionText, row;
            private readonly Font dayFont = new Font("Segoe UI", 23F, FontStyle.Bold);
            private readonly Font monthFont = new Font("Segoe UI", 10F);
            private readonly Font labelFont = new Font("Segoe UI", 8F, FontStyle.Bold);
            private DateTime value = DateTime.Today;
            private JournalCalendarPopup popup;
            internal event EventHandler ValueChanged;
            internal DateTime Value
            {
                get { return value; }
                set { if (this.value == value.Date) return; this.value = value.Date; Invalidate(); ValueChanged?.Invoke(this, EventArgs.Empty); }
            }
            internal JournalDatePicker(Color fill, Color ink, Color muted, Color accent, Color actionText, Color row)
            {
                this.fill = fill; this.ink = ink; this.muted = muted; this.accent = accent; this.actionText = actionText; this.row = row;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                Cursor = Cursors.Hand;
                TabStop = true;
                AccessibleRole = AccessibleRole.ComboBox;
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(Parent is JournalSurface ? ((JournalSurface)Parent).Fill : Parent == null ? fill : Parent.BackColor);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 9))
                {
                    using (var brush = new SolidBrush(fill)) e.Graphics.FillPath(brush, path);
                    if (Focused) using (var pen = new Pen(accent)) e.Graphics.DrawPath(pen, path);
                }
                TextRenderer.DrawText(e.Graphics, (AccessibleName ?? "Ngày").ToUpperInvariant(), labelFont, new Rectangle(8, 3, Width - 16, 17), muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(e.Graphics, value.ToString("dd"), dayFont, new Rectangle(4, 16, 54, 40), ink,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(e.Graphics, value.ToString("MM / yyyy"), monthFont, new Rectangle(57, 24, 78, 28), muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                int x = Width - 10, y = 39;
                using (var pen = new Pen(muted, 1.5F))
                    e.Graphics.DrawLines(pen, new[] { new Point(x - 4, y - 2), new Point(x, y + 2), new Point(x + 4, y - 2) });
            }
            protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
            protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
            protected override void OnClick(EventArgs e) { base.OnClick(e); Focus(); OpenCalendar(); }
            protected override bool IsInputKey(Keys keyData)
            {
                var key = keyData & Keys.KeyCode;
                return key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down || base.IsInputKey(keyData);
            }
            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space || (e.Alt && e.KeyCode == Keys.Down)) OpenCalendar();
                else if (e.KeyCode == Keys.Left) Value = value.AddDays(-1);
                else if (e.KeyCode == Keys.Right) Value = value.AddDays(1);
                else if (e.KeyCode == Keys.Up) Value = value.AddDays(-7);
                else if (e.KeyCode == Keys.Down) Value = value.AddDays(7);
                else if (e.KeyCode == Keys.PageUp) Value = value.AddMonths(-1);
                else if (e.KeyCode == Keys.PageDown) Value = value.AddMonths(1);
                else { base.OnKeyDown(e); return; }
                e.Handled = e.SuppressKeyPress = true;
            }
            private void OpenCalendar()
            {
                if (popup != null) popup.Dispose();
                popup = new JournalCalendarPopup { BackColor = fill, ForeColor = ink, Padding = new Padding(1), DropShadowEnabled = false };
                var calendar = new Panel { Size = new Size(294, 306), BackColor = fill, Padding = new Padding(12) };
                var previous = CalendarButton("‹", new Rectangle(12, 10, 36, 34));
                var next = CalendarButton("›", new Rectangle(246, 10, 36, 34));
                var title = new Label { Bounds = new Rectangle(50, 10, 194, 34), ForeColor = ink, TextAlign = ContentAlignment.MiddleCenter };
                calendar.Controls.AddRange(new Control[] { previous, title, next });
                string[] weekdays = { "T2", "T3", "T4", "T5", "T6", "T7", "CN" };
                for (int i = 0; i < 7; i++) calendar.Controls.Add(new Label { Text = weekdays[i],
                    Bounds = new Rectangle(14 + i * 38, 50, 36, 26), ForeColor = muted, TextAlign = ContentAlignment.MiddleCenter });
                var days = new List<JournalButton>();
                for (int i = 0; i < 42; i++)
                {
                    var button = CalendarButton("", new Rectangle(14 + (i % 7) * 38, 80 + (i / 7) * 36, 34, 32));
                    button.Click += delegate { Value = (DateTime)button.Tag; popup.Close(); Focus(); };
                    days.Add(button);
                    calendar.Controls.Add(button);
                }
                DateTime month = new DateTime(value.Year, value.Month, 1);
                Action renderMonth = () =>
                {
                    title.Text = month.ToString("MM / yyyy");
                    DateTime start = month.AddDays(-(((int)month.DayOfWeek + 6) % 7));
                    for (int i = 0; i < days.Count; i++)
                    {
                        DateTime date = start.AddDays(i);
                        var button = days[i];
                        button.Tag = date;
                        button.Text = date.Day.ToString();
                        button.AccessibleName = date.ToString("dd/MM/yyyy");
                        button.Fill = date == value ? accent : date == DateTime.Today ? row : fill;
                        button.ForeColor = date == value ? actionText : date.Month == month.Month ? ink : muted;
                        button.Invalidate();
                    }
                };
                previous.Click += delegate { month = month.AddMonths(-1); renderMonth(); };
                next.Click += delegate { month = month.AddMonths(1); renderMonth(); };
                renderMonth();
                var host = new ToolStripControlHost(calendar) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false, Size = calendar.Size };
                popup.Items.Add(host);
                popup.Show(this, new Point(0, Height + 6));
            }
            private JournalButton CalendarButton(string text, Rectangle bounds)
            {
                return new JournalButton { Text = text, Bounds = bounds, Fill = fill, ForeColor = ink, FlatStyle = FlatStyle.Flat,
                    Font = Font, Cursor = Cursors.Hand, Margin = Padding.Empty };
            }
            protected override void Dispose(bool disposing)
            {
                if (disposing && popup != null) popup.Dispose();
                base.Dispose(disposing);
                if (disposing) { dayFont.Dispose(); monthFont.Dispose(); labelFont.Dispose(); }
            }
        }

        private sealed class JournalCalendarPopup : ToolStripDropDown
        {
            protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(BackColor); }
            protected override void OnPaint(PaintEventArgs e)
            {
                using (var pen = new Pen(ControlPaint.Dark(BackColor, .07F)))
                    e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }

        private sealed class JournalScrollArea : Panel
        {
            private readonly Panel holder = new Panel();
            private readonly JournalScrollThumb rail = new JournalScrollThumb();
            private Control content;
            private int offset;
            private bool arranging;
            internal JournalScrollArea()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
                Controls.Add(holder);
                Controls.Add(rail);
                rail.MoveTo += value => { offset = value; Arrange(); };
                MouseWheel += ScrollWheel;
            }
            internal void SetContent(Control control)
            {
                content = control;
                holder.Controls.Add(control);
                control.SizeChanged += delegate { Arrange(); };
                BindWheel(control);
                Arrange();
            }
            private void BindWheel(Control control)
            {
                control.MouseWheel += ScrollWheel;
                control.ControlAdded += (s, e) => BindWheel(e.Control);
                foreach (Control child in control.Controls) BindWheel(child);
            }
            private void ScrollWheel(object sender, MouseEventArgs e)
            {
                offset -= e.Delta / 120 * 72;
                Arrange();
                var handled = e as HandledMouseEventArgs;
                if (handled != null) handled.Handled = true;
            }
            protected override void OnResize(EventArgs e) { base.OnResize(e); Arrange(); }
            private void Arrange()
            {
                if (arranging || content == null) return;
                arranging = true;
                try
                {
                    holder.Width = Math.Max(1, ClientSize.Width - 14);
                    holder.Height = content.Height;
                    int maximum = Math.Max(0, content.Height - ClientSize.Height);
                    offset = Math.Max(0, Math.Min(offset, maximum));
                    holder.Location = new Point(0, -offset);
                    rail.SetBounds(ClientSize.Width - 9, 0, 8, ClientSize.Height);
                    rail.ForeColor = ForeColor;
                    rail.Maximum = maximum;
                    rail.Page = ClientSize.Height;
                    rail.Value = offset;
                    rail.Visible = maximum > 0;
                    rail.Invalidate();
                }
                finally { arranging = false; }
            }
        }

        private sealed class JournalScrollThumb : Control
        {
            internal int Maximum, Page, Value;
            internal event Action<int> MoveTo;
            private int dragStart, dragValue;
            internal JournalScrollThumb()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
                Cursor = Cursors.Hand;
                TabStop = true;
                AccessibleRole = AccessibleRole.ScrollBar;
            }
            private int ThumbHeight { get { return Math.Min(Height, Math.Max(32, (int)((long)Height * Page / Math.Max(1, Maximum + Page)))); } }
            private int ThumbTop { get { return Maximum == 0 ? 0 : (int)((long)Value * (Height - ThumbHeight) / Maximum); } }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = Rounded(new Rectangle(1, ThumbTop, Math.Max(1, Width - 3), ThumbHeight), 3))
                using (var brush = new SolidBrush(Color.FromArgb(100, ForeColor))) e.Graphics.FillPath(brush, path);
            }
            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left) return;
                if (e.Y < ThumbTop || e.Y > ThumbTop + ThumbHeight)
                    MoveTo?.Invoke((int)((long)Math.Max(0, e.Y - ThumbHeight / 2) * Maximum / Math.Max(1, Height - ThumbHeight)));
                dragStart = e.Y;
                dragValue = Value;
                Capture = true;
            }
            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (Capture) MoveTo?.Invoke(dragValue + (int)((long)(e.Y - dragStart) * Maximum / Math.Max(1, Height - ThumbHeight)));
            }
            protected override void OnMouseUp(MouseEventArgs e) { Capture = false; base.OnMouseUp(e); }
            protected override bool IsInputKey(Keys keyData)
            {
                var key = keyData & Keys.KeyCode;
                return key == Keys.Up || key == Keys.Down || base.IsInputKey(keyData);
            }
            protected override void OnKeyDown(KeyEventArgs e)
            {
                int delta = e.KeyCode == Keys.Down ? 72 : e.KeyCode == Keys.Up ? -72 : e.KeyCode == Keys.PageDown ? Page : e.KeyCode == Keys.PageUp ? -Page : 0;
                if (delta != 0) { MoveTo?.Invoke(Value + delta); e.Handled = true; }
                else base.OnKeyDown(e);
            }
        }

        private static GraphicsPath Rounded(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            int d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            if (d <= 0) return path;
            path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private sealed class JournalSurface : Panel
        {
            internal Color Fill;
            internal Color Outline = Color.Empty;
            internal bool Elevated;
            internal int Radius;
            internal JournalSurface()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
                BackColor = Color.Transparent;
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                int inset = Elevated ? 3 : 0;
                if (Elevated)
                    using (var shadow = Rounded(new Rectangle(2, 4, Width - 4, Height - 5), Radius))
                    using (var brush = new SolidBrush(Color.FromArgb(45, Color.Black))) e.Graphics.FillPath(brush, shadow);
                using (var path = Rounded(new Rectangle(0, 0, Width - 1 - inset, Height - 1 - inset), Radius))
                {
                    using (var brush = new SolidBrush(Fill)) e.Graphics.FillPath(brush, path);
                    if (!Outline.IsEmpty) using (var pen = new Pen(Outline)) e.Graphics.DrawPath(pen, path);
                }
                base.OnPaint(e);
            }
        }

        private sealed class JournalButton : Button
        {
            internal Color Fill;
            private bool hover;
            internal JournalButton()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                FlatAppearance.BorderSize = 0;
            }
            private Color CanvasColor()
            {
                for (Control control = Parent; control != null; control = control.Parent)
                {
                    var surface = control as JournalSurface;
                    Color color = surface == null ? control.BackColor : surface.Fill;
                    if (color.A == 255) return color;
                }
                return SystemColors.Control;
            }
            protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(CanvasColor()); }
            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(CanvasColor());
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Color fill = hover && Enabled ? ControlPaint.Light(Fill, .1f) : Fill;
                using (var path = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 9))
                using (var brush = new SolidBrush(fill)) e.Graphics.FillPath(brush, path);
                Color foreground = Enabled ? ForeColor : SystemColors.GrayText;
                if (Text == "‹" || Text == "›")
                {
                    float x = (Width - 1) / 2F, y = (Height - 1) / 2F;
                    float direction = Text == "›" ? 1F : -1F;
                    using (var pen = new Pen(foreground, 1.6F) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                        e.Graphics.DrawLines(pen, new[] { new PointF(x - direction * 2, y - 4),
                            new PointF(x + direction * 2, y), new PointF(x - direction * 2, y + 4) });
                }
                else TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, foreground,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                if (Focused)
                    using (var path = Rounded(new Rectangle(2, 2, Width - 5, Height - 5), 7))
                    using (var pen = new Pen(ControlPaint.Light(Fill, .35F))) e.Graphics.DrawPath(pen, path);
            }
        }

        private sealed class WindowButton : Button
        {
            internal int Kind;
            internal Color HoverColor;
            private bool hover;
            internal WindowButton()
            {
                Margin = Padding.Empty;
                TabStop = false;
                FlatStyle = FlatStyle.Flat;
                Cursor = Cursors.Hand;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }
            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(hover ? Kind == 2 ? Color.FromArgb(190, 55, 55) : HoverColor : BackColor);
                int x = Width / 2, y = Height / 2;
                using (var pen = new Pen(hover && Kind == 2 ? Color.White : ForeColor, 1.2F))
                {
                    if (Kind == 0) e.Graphics.DrawLine(pen, x - 5, y + 3, x + 5, y + 3);
                    else if (Kind == 1) e.Graphics.DrawRectangle(pen, x - 5, y - 5, 10, 10);
                    else { e.Graphics.DrawLine(pen, x - 4, y - 4, x + 4, y + 4); e.Graphics.DrawLine(pen, x + 4, y - 4, x - 4, y + 4); }
                    if (Focused) e.Graphics.DrawRectangle(pen, 4, 4, Width - 9, Height - 9);
                }
            }
        }
    }
}
