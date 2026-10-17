// Điểm khởi động: ưu tiên updater worker/recovery, sau đó nạp Tekla và mở MainForm.
// LUỒNG: Program.Main → kiểm tra mode → khóa/khôi phục cập nhật → ProgramTeklaStartup.Run → thư viện Tekla → MainForm.
// ĐIỂM SỬA: mode worker ở Program.Main; dò thư viện ở FindTeklaBinPath/ResolveTeklaAssembly.
// THAM SỐ: biến môi trường TeklaBinPath được thử trước các thư mục Tekla 2025 mặc định; các mode phải khớp UpdateWorker.
// TƯƠNG THÍCH: IScript và Class1 được giữ tên kiểu; không chứa thuật toán DIM.
// TRA CỨU: PROJECT_MAP.md ở gốc repo; chỉ mục kiểu/hàm và điểm chỉnh thông số.
// Mục lục:
//   AkitStub.cs
//   Class1.cs
//   Program.cs
//   Program.TeklaStartup.cs

#region AkitStub - Kiểu IScript tương thích điểm gọi macro cũ
namespace Tekla.Technology.Akit
{


    public interface IScript { }
}
#endregion

#region TeklaSessionCoordinator - Chọn phiên trước khi nạp API, không ghi vào model
namespace TTSK_AutoDim_Plates
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Drawing;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Threading;
    using System.Windows.Forms;

    internal sealed class TeklaSessionTarget
    {
        internal int Id;
        internal long Started;
        internal string Title, Session, Bin;
        internal string ModelFolder
        {
            get
            {
                var match = System.Text.RegularExpressions.Regex.Match(Title ?? "", @" - (?<path>(?:[A-Za-z]:\\|\\\\).*)");
                if (!match.Success) return "";
                string value = match.Groups["path"].Value;
                // Tekla tách model bằng hai khoảng trắng; không cắt dấu " - " bên trong tên folder.
                int separator = value.IndexOf("  - ", StringComparison.Ordinal);
                if (separator < 0)
                {
                    var context = System.Text.RegularExpressions.Regex.Match(value, @"\s+-\s+(?:View\s+\d|W\s+\[)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (context.Success) separator = context.Index;
                }
                return (separator < 0 ? value : value.Substring(0, separator)).TrimEnd();
            }
        }
        internal string ProjectName { get { return string.IsNullOrEmpty(ModelFolder) ? Title : Path.GetFileName(ModelFolder.TrimEnd('\\')); } }
        internal string WindowContext
        {
            get
            {
                string folder = ModelFolder;
                if (folder.Length == 0) return "Tekla Structures 2025";
                int end = Title.IndexOf(folder, StringComparison.OrdinalIgnoreCase) + folder.Length;
                string context = Title.Substring(end).Trim().TrimStart('-').Trim();
                return context.Length == 0 ? "Model đang mở" : context;
            }
        }
        public override string ToString() { return Title + "  [PID " + Id + "]"; }
    }

    // Hộp chọn chỉ có thẻ dự án; kích thước là pixel ở 96 DPI, font theo point.
    // Không truy cập API/model và không ghi cấu hình khi xem hoặc hủy hộp chọn.
    internal sealed class TeklaProjectPickerForm : Form
    {
        internal TeklaSessionTarget SelectedTarget { get; private set; }
        private readonly Color ink, muted, accent, border;
        private readonly bool darkMode;
        private bool nativeRoundedFrame;
        internal TeklaProjectPickerForm(List<TeklaSessionTarget> targets, bool dark, TeklaSessionTarget current, int foreground)
        {
            SuspendLayout();
            darkMode = dark;
            Text = "Chọn dự án Tekla";
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(640, 166 + Math.Max(1, Math.Min(4, targets.Count)) * 110);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            BackColor = dark ? Color.FromArgb(19, 21, 26) : Color.FromArgb(243, 245, 249);
            ink = dark ? Color.FromArgb(241, 239, 235) : Color.FromArgb(25, 36, 55);
            muted = dark ? Color.FromArgb(157, 160, 169) : Color.FromArgb(104, 117, 135);
            accent = dark ? Color.FromArgb(222, 150, 89) : Color.FromArgb(37, 79, 169);
            border = dark ? Color.FromArgb(73, 56, 43) : Color.FromArgb(205, 215, 229);
            Font = new Font("Segoe UI", 10F);
            ForeColor = ink;
            Padding = new Padding(1);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
                Padding = new Padding(26, 0, 26, 18), Margin = Padding.Empty };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            Controls.Add(layout);
            var header = new Panel { Size = new Size(586, 116), Dock = DockStyle.Fill, Margin = Padding.Empty };
            var eyebrow = new Label { Text = "TTSK  /  TEKLA STRUCTURES", Location = new Point(0, 18), Size = new Size(430, 22), ForeColor = accent,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            var heading = new Label { Text = "Chọn dự án", Location = new Point(0, 40), Size = new Size(490, 43), ForeColor = ink,
                Font = new Font("Segoe UI", 25F, FontStyle.Bold) };
            var hint = new Label { Text = "Bấm vào dự án để bắt đầu làm việc.", Location = new Point(1, 88), Size = new Size(520, 24), ForeColor = muted };
            var close = new ProjectCardButton(dark) { Text = "Đóng", Compact = true, Location = new Point(535, 15), Size = new Size(48, 38),
                Anchor = AnchorStyles.Top | AnchorStyles.Right, AccessibleName = "Đóng hộp chọn dự án", DialogResult = DialogResult.Cancel };
            close.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            header.Controls.AddRange(new Control[] { eyebrow, heading, hint, close });
            MouseEventHandler drag = delegate(object sender, MouseEventArgs e)
            { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero); } };
            foreach (Control control in new Control[] { header, eyebrow, heading, hint }) control.MouseDown += drag;
            layout.Controls.Add(header, 0, 0);
            var scroll = new ProjectScrollPanel(muted) { Dock = DockStyle.Fill, Margin = Padding.Empty };
            var cards = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 0,
                Margin = Padding.Empty, Padding = new Padding(0, 0, 2, 0) };
            cards.SuspendLayout();
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            ProjectCardButton first = null, preferred = null;
            foreach (var target in targets)
            {
                bool active = current != null && current.Id == target.Id && current.Started == target.Started;
                bool duplicate = targets.Count(t => string.Equals(t.ProjectName, target.ProjectName, StringComparison.OrdinalIgnoreCase)) > 1;
                var card = new ProjectCardButton(dark) { Text = target.ProjectName, Detail = target.WindowContext,
                    Folder = duplicate ? target.ModelFolder + " · Cửa sổ " + target.Id : target.ModelFolder,
                    Badge = active ? "ĐANG DÙNG" : "", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12),
                    AccessibleName = target.ProjectName + ". " + target.WindowContext, Tag = target, TabIndex = cards.RowCount };
                card.Click += delegate { SelectedTarget = target; DialogResult = DialogResult.OK; Close(); };
                card.GotFocus += delegate { scroll.Reveal(card); };
                int row = cards.RowCount++;
                cards.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
                cards.Controls.Add(card, 0, row);
                if (first == null) first = card;
                if (target.Id == foreground || (preferred == null && active)) preferred = card;
            }
            if (targets.Count == 0) cards.Controls.Add(new Label { Text = "Chưa có dự án Tekla sẵn sàng.", AutoSize = true, ForeColor = muted });
            cards.ResumeLayout(false);
            scroll.SetContent(cards);
            layout.Controls.Add(scroll, 0, 1);
            layout.Controls.Add(new Label { Text = "Tekla 2025  ·  " + targets.Count + " dự án đang mở", Dock = DockStyle.Fill,
                ForeColor = muted, Font = new Font("Segoe UI", 9F), Margin = Padding.Empty }, 0, 2);
            Shown += delegate
            {
                // Thẻ 110 px ở 96 DPI; chốt sau autoscale để RowStyle không tích lũy hệ số khi gắn parent.
                cards.SuspendLayout();
                foreach (RowStyle row in cards.RowStyles) row.Height = 110 * DeviceDpi / 96F;
                cards.ResumeLayout(true);
                if (preferred != null || first != null) (preferred ?? first).Focus();
            };
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
            // Chỉ scale sau khi dựng đủ control; tránh đổi font sớm làm mất mốc 96 DPI.
            AutoScaleDimensions = new SizeF(96, 96);
            ResumeLayout(true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!nativeRoundedFrame)
                using (var pen = new Pen(border)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                // DWM cần frame style để bo mép bằng compositor; không cắt Region nhị phân.
                parameters.Style |= 0x00C00000 | 0x00040000; // WS_CAPTION | WS_THICKFRAME
                return parameters;
            }
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Windows 11 vẽ mép bo và viền có antialias, cùng màu theme. Không dùng Region/TransparencyKey.
            // Windows cũ không hỗ trợ attribute 33 thì giữ viền thẳng, không quay lại góc răng cưa.
            int rounded = 2, dark = darkMode ? 1 : 0;
            int color = border.R | (border.G << 8) | (border.B << 16);
            try
            {
                nativeRoundedFrame = DwmSetWindowAttribute(Handle, 33, ref rounded, sizeof(int)) == 0;
                DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
                DwmSetWindowAttribute(Handle, 34, ref color, sizeof(int));
            }
            catch (DllNotFoundException) { nativeRoundedFrame = false; }
            catch (EntryPointNotFoundException) { nativeRoundedFrame = false; }
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0083 && message.WParam != IntPtr.Zero) // WM_NCCALCSIZE: giữ header tự vẽ.
            { message.Result = IntPtr.Zero; return; }
            base.WndProc(ref message);
            // Hộp chọn cố định kích thước; drag header vẫn dùng HTCAPTION từ MouseDown.
            if (message.Msg == 0x0084) message.Result = new IntPtr(1); // WM_NCHITTEST / HTCLIENT
        }
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        private sealed class ProjectScrollPanel : Panel
        {
            private Control content;
            private readonly Color thumbColor;
            private int offset, dragY, dragOffset;
            private int Maximum { get { return content == null ? 0 : Math.Max(0, content.Height - Height); } }
            internal ProjectScrollPanel(Color color)
            {
                thumbColor = color;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }
            internal void SetContent(Control value)
            {
                content = value; Controls.Add(value);
                value.SizeChanged += delegate { Arrange(); };
                Arrange();
            }
            private void Arrange()
            {
                if (content == null) return;
                int width = Math.Max(1, Width - (Maximum > 0 ? 14 : 0));
                if (content.Width != width) content.Width = width;
                offset = Math.Max(0, Math.Min(Maximum, offset)); content.Location = new Point(0, -offset); Invalidate();
            }
            internal void Reveal(Control card)
            {
                if (card.Top < offset) offset = card.Top;
                else if (card.Bottom > offset + Height) offset = card.Bottom - Height;
                Arrange();
            }
            protected override void OnResize(EventArgs e) { base.OnResize(e); Arrange(); }
            protected override void OnMouseWheel(MouseEventArgs e)
            { offset -= e.Delta * Math.Max(1, Font.Height * 3) / 120; Arrange(); base.OnMouseWheel(e); }
            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left || Maximum == 0 || e.X < Width - 14) return;
                dragY = e.Y; dragOffset = offset; Capture = true;
            }
            protected override void OnMouseMove(MouseEventArgs e)
            {
                if (Capture) { offset = dragOffset + (int)((long)(e.Y - dragY) * content.Height / Math.Max(1, Height)); Arrange(); }
                base.OnMouseMove(e);
            }
            protected override void OnMouseUp(MouseEventArgs e) { Capture = false; base.OnMouseUp(e); }
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                if (Maximum == 0) return;
                int h = Math.Max(24, Height * Height / content.Height);
                int y = offset * (Height - h) / Maximum;
                using (var brush = new SolidBrush(thumbColor)) e.Graphics.FillRectangle(brush, Width - 7, y, 3, h);
            }
        }

        private sealed class ProjectCardButton : Button
        {
            internal string Detail = "", Folder = "", Badge = "";
            internal bool Compact;
            private readonly bool dark;
            private bool hover, pressed;
            internal ProjectCardButton(bool dark)
            {
                this.dark = dark;
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                Cursor = Cursors.Hand;
                UseVisualStyleBackColor = false;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
            }
            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
            protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
            protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
            protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
            private Color CanvasColor()
            {
                // TableLayoutPanel có thể trả Color.Transparent; Clear màu đó làm buffer GDI đen.
                // Chỉ dùng nền opaque từ ancestor, rồi vẽ lại toàn bộ nền ở mỗi lần paint/hover/focus.
                for (Control parent = Parent; parent != null; parent = parent.Parent)
                    if (parent.BackColor.A == 255) return parent.BackColor;
                return dark ? Color.FromArgb(19, 21, 26) : Color.FromArgb(243, 245, 249);
            }
            protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(CanvasColor()); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(CanvasColor());
                float scale = DeviceDpi / 96F;
                Func<int, int> px = n => (int)Math.Round(n * scale);
                bool hot = hover || Focused;
                Color accent = dark ? Color.FromArgb(222, 150, 89) : Color.FromArgb(37, 79, 169);
                Color ink = dark ? Color.FromArgb(241, 239, 235) : Color.FromArgb(25, 36, 55);
                Color muted = dark ? Color.FromArgb(157, 166, 179) : Color.FromArgb(104, 117, 135);
                Color fill = dark ? Color.FromArgb(28, 31, 37) : Color.White;
                if (hot) fill = dark ? Color.FromArgb(51, 40, 33) : Color.FromArgb(229, 239, 255);
                if (pressed) fill = dark ? Color.FromArgb(67, 47, 33) : Color.FromArgb(211, 226, 251);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var bounds = new Rectangle(px(1), px(1), Width - px(3), Height - px(3));
                using (var path = Rounded(bounds, px(14)))
                using (var brush = new SolidBrush(fill))
                using (var pen = new Pen(hot ? accent : dark ? Color.FromArgb(63, 58, 54) : Color.FromArgb(216, 225, 238), hot ? px(2) : 1))
                { g.FillPath(brush, path); g.DrawPath(pen, path); }
                if (Compact)
                {
                    int x = Width / 2, y = Height / 2, d = px(5);
                    using (var pen = new Pen(hot ? accent : muted, px(2)))
                    { g.DrawLine(pen, x - d, y - d, x + d, y + d); g.DrawLine(pen, x + d, y - d, x - d, y + d); }
                    return;
                }
                using (var icon = Rounded(new Rectangle(px(18), px(23), px(48), px(48)), px(12)))
                using (var brush = new SolidBrush(dark ? Color.FromArgb(60, 45, 34) : Color.FromArgb(235, 241, 252))) g.FillPath(brush, icon);
                using (var pen = new Pen(accent, px(2)))
                {
                    g.DrawRectangle(pen, px(32), px(35), px(20), px(23));
                    g.DrawLine(pen, px(37), px(42), px(47), px(42));
                    g.DrawLine(pen, px(37), px(48), px(47), px(48));
                    g.DrawLine(pen, px(39), px(58), px(39), px(53));
                }
                var flags = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
                int left = px(82), right = px(48), badgeWidth = Badge.Length == 0 ? 0 : px(86);
                using (var titleFont = new Font("Segoe UI", 18F, FontStyle.Bold))
                using (var detailFont = new Font("Segoe UI", 10F))
                using (var pathFont = new Font("Segoe UI", 8.5F))
                {
                    TextRenderer.DrawText(g, Text, titleFont, new Rectangle(left, px(14), Math.Max(1, Width - left - right - badgeWidth), px(34)), ink, flags);
                    TextRenderer.DrawText(g, Detail, detailFont, new Rectangle(left, px(49), Width - left - right, px(20)), hot ? accent : muted, flags);
                    TextRenderer.DrawText(g, Folder, pathFont, new Rectangle(left, px(73), Width - left - right, px(17)), muted, flags | TextFormatFlags.PathEllipsis);
                }
                if (Badge.Length > 0)
                    using (var font = new Font("Segoe UI", 8F, FontStyle.Bold))
                        TextRenderer.DrawText(g, Badge, font, new Rectangle(Width - right - badgeWidth, px(22), badgeWidth, px(22)), accent, flags | TextFormatFlags.Right);
                int arrowX = Width - px(26), arrowY = Height / 2;
                using (var pen = new Pen(hot ? accent : muted, px(2)))
                    g.DrawLines(pen, new[] { new Point(arrowX - px(3), arrowY - px(5)), new Point(arrowX + px(2), arrowY), new Point(arrowX - px(3), arrowY + px(5)) });
            }
            private static System.Drawing.Drawing2D.GraphicsPath Rounded(Rectangle r, int radius)
            {
                var path = new System.Drawing.Drawing2D.GraphicsPath();
                int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
                path.AddArc(r.Left, r.Top, d, d, 180, 90); path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90); path.CloseFigure();
                return path;
            }
        }
    }

    // Thu ảnh nguồn lớn về đúng pixel hiển thị một lần; cache ở form, không phóng ảnh nhỏ mỗi frame.
    internal static class CompanyLogoRenderer
    {
        internal static Bitmap Load(bool dark)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", dark ? "logo_dark.png" : "logo.png");
            using (var stream = File.OpenRead(path))
            using (var image = Image.FromStream(stream)) return new Bitmap(image);
        }
        internal static Icon CreateIcon(Image source)
        {
            // ICO nhiều kích thước cho title bar/taskbar/DPI; không dùng GetHicon không được giải phóng.
            int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
            var frames = new List<byte[]>();
            foreach (int size in sizes)
            {
                using (var scaled = Render(source, size))
                using (var square = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                using (var graphics = Graphics.FromImage(square))
                using (var frame = new MemoryStream())
                {
                    graphics.Clear(Color.Transparent);
                    graphics.DrawImageUnscaled(scaled, 0, (size - scaled.Height) / 2);
                    square.Save(frame, System.Drawing.Imaging.ImageFormat.Png);
                    frames.Add(frame.ToArray());
                }
            }
            using (var data = new MemoryStream())
            using (var writer = new BinaryWriter(data))
            {
                writer.Write((short)0); writer.Write((short)1); writer.Write((short)sizes.Length);
                int offset = 6 + sizes.Length * 16;
                for (int i = 0; i < sizes.Length; i++)
                {
                    writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                    writer.Write((byte)0); writer.Write((byte)0); writer.Write((short)1); writer.Write((short)32);
                    writer.Write(frames[i].Length); writer.Write(offset); offset += frames[i].Length;
                }
                foreach (byte[] frame in frames) writer.Write(frame);
                writer.Flush(); data.Position = 0;
                using (var icon = new Icon(data)) return (Icon)icon.Clone();
            }
        }
        internal static Bitmap Render(Image source, int width)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (width < 1 || width > 4096) throw new ArgumentOutOfRangeException(nameof(width));
            if (width == source.Width && source is Bitmap) return (Bitmap)((Bitmap)source).Clone();
            int height = Math.Max(1, (int)Math.Round(width * (double)source.Height / source.Width));
            var result = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var graphics = Graphics.FromImage(result))
            using (var attributes = new System.Drawing.Imaging.ImageAttributes())
            {
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                graphics.InterpolationMode = width == source.Width ? System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor
                    : System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                attributes.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);
                graphics.DrawImage(source, new Rectangle(0, 0, width, height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            }
            return result;
        }
    }
    // Logo-only: cửa sổ alpha từng pixel, không nền/viền/form chữ nhật và không thay đổi PNG nguồn.
    internal sealed class CompanyLogoWindow : NativeWindow, IDisposable
    {
        private readonly Bitmap surface;
        private readonly Point position;
        internal CompanyLogoWindow(Image logo, IntPtr owner, int dpi)
        {
            int width = (int)Math.Round(300 * dpi / 96F);
            surface = CompanyLogoRenderer.Render(logo, width);
            int height = surface.Height;
            var screen = Screen.FromHandle(owner).WorkingArea;
            position = new Point(screen.Left + (screen.Width - width) / 2, screen.Top + (screen.Height - height) / 2);
            CreateHandle(new CreateParams { Caption = "TTSK Company Logo", Parent = owner, X = position.X, Y = position.Y,
                Width = width, Height = height, Style = unchecked((int)0x80000000), ExStyle = 0x00080000 | 0x00000080 | 0x08000000 });
        }
        internal void ShowLogo(byte opacity)
        {
            if (Handle == IntPtr.Zero) return;
            IntPtr screen = GetDC(IntPtr.Zero), dc = IntPtr.Zero, bitmap = IntPtr.Zero, previous = IntPtr.Zero;
            try
            {
                dc = CreateCompatibleDC(screen);
                bitmap = surface.GetHbitmap(Color.FromArgb(0));
                previous = SelectObject(dc, bitmap);
                Point destination = position, source = Point.Empty; Size size = surface.Size;
                var blend = new Blend { SourceConstantAlpha = opacity, AlphaFormat = 1 };
                if (!UpdateLayeredWindow(Handle, screen, ref destination, ref size, dc, ref source, 0, ref blend, 2))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                ShowWindow(Handle, 4);
            }
            finally
            {
                if (previous != IntPtr.Zero) SelectObject(dc, previous);
                if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
                if (dc != IntPtr.Zero) DeleteDC(dc);
                if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
            }
        }
        internal void HideLogo() { if (Handle != IntPtr.Zero) ShowWindow(Handle, 0); }
        public void Dispose() { if (Handle != IntPtr.Zero) DestroyHandle(); surface.Dispose(); }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct Blend { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr screen, ref Point destination,
            ref Size size, IntPtr sourceDc, ref Point source, int color, ref Blend blend, int flags);
    }

    // Màn khởi động nhẹ, không nạp Tekla API. Chờ và retry bằng tiến trình con để proxy lỗi không bị tái dùng.
    internal sealed class TeklaStartupForm : Form
    {
        private readonly Func<List<TeklaSessionTarget>> discover;
        private readonly System.Threading.CancellationTokenSource lifetime = new System.Threading.CancellationTokenSource();
        private readonly System.Windows.Forms.Timer animation = new System.Windows.Forms.Timer { Interval = 33 };
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Color accent, muted;
        private Image logo;
        private Icon startupThemeIcon;
        private Bitmap renderedLogo;
        private CompanyLogoWindow logoWindow;
        private bool waitingPresentation;
        internal bool WaitingPresentation { get { return waitingPresentation; } }
        private readonly Font brandFont = new Font("Segoe UI", 29F, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font captionFont = new Font("Segoe UI", 13F, FontStyle.Regular, GraphicsUnit.Pixel);
        private string status = "Đang khởi động…";
        private Process child;
        private bool handedOff;
        private readonly ShortcutManager startupShortcuts;
        private bool entryRequested;
        internal bool OpenWithoutConnection { get; private set; }
        internal bool OpenMeetingTranslation { get; private set; }
        internal bool OpenWorkJournal { get; private set; }
        internal bool DarkMode { get; private set; }
        internal long FirstPaintMilliseconds { get; private set; }
        internal string StatusText { get { return status; } }
        internal int ExitCode { get; private set; }
        internal TeklaStartupForm() : this(TeklaSessionCoordinator.Discover) { }
        internal TeklaStartupForm(Func<List<TeklaSessionTarget>> discover)
        {
            this.discover = discover;
            startupShortcuts = new ShortcutManager(AppDomain.CurrentDomain.BaseDirectory);
            startupShortcuts.Load();
            SuspendLayout();
            bool dark = false;
            try { dark = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "theme.cfg")).Trim().Equals("DARK", StringComparison.OrdinalIgnoreCase); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            DarkMode = dark;
            BackColor = dark ? Color.FromArgb(19, 21, 26) : Color.FromArgb(245, 247, 251);
            ForeColor = dark ? Color.FromArgb(241, 239, 235) : Color.FromArgb(25, 36, 55);
            accent = dark ? Color.FromArgb(222, 150, 89) : Color.FromArgb(37, 79, 169);
            muted = dark ? Color.FromArgb(157, 166, 179) : Color.FromArgb(104, 117, 135);
            Text = "TTSK · Khởi động";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(460, 558);
            Opacity = 0; // Form chờ chỉ hiện sau khi xác định Tekla/model chưa sẵn sàng.
            ShowInTaskbar = false;
            MaximizeBox = MinimizeBox = false;
            Font = new Font("Segoe UI", 10F);
            KeyPreview = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            try
            {
                logo = CompanyLogoRenderer.Load(dark);
                startupThemeIcon = CompanyLogoRenderer.CreateIcon(logo);
                Icon = startupThemeIcon;
            }
            catch (IOException) { }
            catch (ArgumentException) { }
            var close = new Button { Text = "×", FlatStyle = FlatStyle.Flat, ForeColor = muted, BackColor = BackColor,
                Font = new Font("Segoe UI", 16F), Size = new Size(38, 34), Location = new Point(409, 10),
                Cursor = Cursors.Hand, AccessibleName = "Đóng TTSK", Anchor = AnchorStyles.Top | AnchorStyles.Right };
            close.FlatAppearance.BorderSize = 0;
            close.Click += delegate { Close(); };
            // Hai lối vào độc lập; kích thước pixel ở 96 DPI, cùng phong cách thẻ ACTIVE/BATCH.
            var select = new StartupEntryButton(dark, false) { Text = "Vào phần mềm",
                Size = new Size(200, 114), Location = new Point(24, 422), TabIndex = 0,
                AccessibleName = "Vào phần mềm", AccessibleDescription = "Mở công cụ và nhật ký, tiếp tục chờ kết nối Tekla" };
            select.Click += delegate { RequestStartupEntry(null); };
            var meeting = new StartupEntryButton(dark, true) { Text = "Dịch họp",
                Size = new Size(200, 114), Location = new Point(236, 422), TabIndex = 1,
                AccessibleName = "Dịch họp", AccessibleDescription = "Mở dịch họp Teams độc lập, không cần Tekla" };
            meeting.Click += delegate { RequestStartupEntry(ShortcutManager.ActionMeetingTranslation); };
            close.TabIndex = 2;
            Controls.AddRange(new Control[] { close, select, meeting });
            animation.Tick += delegate { Invalidate(); };
            Shown += async delegate
            {
                try
                {
                    if (logo != null)
                    {
                        logoWindow = new CompanyLogoWindow(logo, Handle, DeviceDpi);
                        logoWindow.ShowLogo(255);
                    }
                }
                catch (Exception ex)
                {
                    if (logoWindow != null) { logoWindow.Dispose(); logoWindow = null; }
                    TTSK_AutoDim_Plates.Updater.UpdateSecurity.Log(AppDomain.CurrentDomain.BaseDirectory, "Logo presentation: " + ex);
                    ShowWaitingPresentation();
                }
                await ConnectWhenReadyAsync();
            };
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(true);
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            string actionId;
            // Chỉ hai công cụ độc lập được chạy lúc chờ; giữ phím cấu hình, không mở lệnh bản vẽ.
            if (startupShortcuts.TryFindAction(keyData, out actionId)
                && (actionId == ShortcutManager.ActionMeetingTranslation || actionId == ShortcutManager.ActionWorkJournal))
            {
                RequestStartupEntry(actionId);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        private void RequestStartupEntry(string actionId)
        {
            if (entryRequested || handedOff || IsDisposed) return;
            entryRequested = true;
            OpenMeetingTranslation = actionId == ShortcutManager.ActionMeetingTranslation;
            OpenWorkJournal = actionId == ShortcutManager.ActionWorkJournal;
            OpenWithoutConnection = !OpenMeetingTranslation && !OpenWorkJournal;
            Close(); // Hủy dò/bàn giao đang chờ bằng đường đóng hiện hữu trước khi mở công cụ.
        }
        protected override CreateParams CreateParams
        { get { var p = base.CreateParams; p.Style |= 0x00C00000 | 0x00040000; return p; } }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int round = 2, dark = BackColor.GetBrightness() < .4F ? 1 : 0; DwmSetWindowAttribute(Handle, 33, ref round, 4); DwmSetWindowAttribute(Handle, 20, ref dark, 4); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0083 && m.WParam != IntPtr.Zero) { m.Result = IntPtr.Zero; return; }
            base.WndProc(ref m);
            if (m.Msg == 0x0084) m.Result = new IntPtr(1);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (FirstPaintMilliseconds == 0) FirstPaintMilliseconds = Math.Max(1, clock.ElapsedMilliseconds);
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float scale = ClientSize.Width / 460F;
            // Cache đúng pixel hiển thị, vẽ 1:1; không phóng lại logo trong mỗi frame animation.
            if (logo != null)
            {
                int width = Math.Max(1, (int)Math.Round(280 * scale));
                if (renderedLogo == null || renderedLogo.Width != width)
                {
                    if (renderedLogo != null) renderedLogo.Dispose();
                    renderedLogo = CompanyLogoRenderer.Render(logo, width);
                }
                g.DrawImageUnscaled(renderedLogo, (ClientSize.Width - renderedLogo.Width) / 2, (int)Math.Round(32 * scale));
            }
            g.ScaleTransform(scale, scale);
            using (var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var inkBrush = new SolidBrush(ForeColor))
            using (var mutedBrush = new SolidBrush(muted))
            {
                g.DrawString("TTSK Auto Dimension", brandFont, inkBrush, new RectangleF(10, 280, 440, 43), centered);
                g.DrawString("KẾT NỐI KHÔNG GIAN LÀM VIỆC", captionFont, mutedBrush, new RectangleF(10, 328, 440, 24), centered);
                g.DrawString(status, captionFont, mutedBrush, new RectangleF(12, 385, 436, 28), centered);
            }
            using (var track = new Pen(Color.FromArgb(35, accent), 3)) g.DrawLine(track, 158, 370, 302, 370);
            float x = 158 + (float)((Math.Sin(clock.ElapsedMilliseconds / 430.0) + 1) * 51);
            using (var beam = new Pen(accent, 3) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
                g.DrawLine(beam, x, 370, x + 42, 370);
            g.ResetTransform();
            base.OnPaint(e);
        }
        private void SetStatus(string text) { if (!IsDisposed) { status = text; Invalidate(); } }
        private void ShowWaitingPresentation()
        {
            if (waitingPresentation || IsDisposed) return;
            waitingPresentation = true;
            if (logoWindow != null) logoWindow.HideLogo();
            ShowInTaskbar = true; Opacity = 1; animation.Start(); Activate();
        }
        private async System.Threading.Tasks.Task ConnectWhenReadyAsync()
        {
            TeklaSessionTarget selected = null;
            int failures = 0;
            string lastChoice = null;
            try
            {
                // Shown chạy sau paint đầu; mọi dò process ở nền, luôn chỉ có một scan/request đang chạy.
                await System.Threading.Tasks.Task.Yield();
                while (!lifetime.IsCancellationRequested)
                {
                    var snapshot = await System.Threading.Tasks.Task.Run(() =>
                    {
                        var found = discover();
                        return new { All = found, Ready = found.Where(TeklaSessionCoordinator.IsReady).ToList() };
                    }, lifetime.Token);
                    lifetime.Token.ThrowIfCancellationRequested();
                    var targets = snapshot.All;
                    var ready = snapshot.Ready;
                    if (ready.Count == 0)
                    {
                        ShowWaitingPresentation();
                        SetStatus(targets.Count == 0 ? "Đang chờ Tekla Structures…" : "Đang chờ Tekla mở model…");
                        selected = null;
                    }
                    else
                    {
                        selected = selected == null ? null : ready.Find(t => t.Id == selected.Id && t.Started == selected.Started
                            && string.Equals(t.ModelFolder, selected.ModelFolder, StringComparison.OrdinalIgnoreCase));
                        if (selected == null)
                        {
                            SetStatus("Chọn dự án để bắt đầu.");
                            if (ready.Count > 1 && logoWindow != null) logoWindow.HideLogo();
                            selected = ready.Count == 1 ? ready[0] : TeklaSessionCoordinator.Choose(this, ready);
                            if (selected == null) { Close(); return; }
                            if (!waitingPresentation && logoWindow != null) logoWindow.ShowLogo(255);
                        }
                        string key = selected.Id + ":" + selected.Started + ":" + selected.ModelFolder;
                        if (key != lastChoice) { failures = 0; lastChoice = key; }
                        SetStatus("Đang kết nối dự án…");
                        if (await StartWorkspaceAsync(selected, targets)) return;
                        if (lifetime.IsCancellationRequested) return;
                        ShowWaitingPresentation();
                        SetStatus("Tekla chưa sẵn sàng. Đang chờ…");
                        failures++;
                        // Giãn retry 2–10 giây; mỗi lần là tiến trình mới, không tái dùng proxy bị lỗi.
                        await WaitForRetryAsync(Math.Min(10000, failures * 2000));
                    }
                    await System.Threading.Tasks.Task.Delay(750, lifetime.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                TTSK_AutoDim_Plates.Updater.UpdateSecurity.Log(AppDomain.CurrentDomain.BaseDirectory, "Startup wait: " + ex);
                ShowWaitingPresentation();
                SetStatus("Chưa kết nối được. Mở lại TTSK.");
                ExitCode = 1;
            }
        }
        private async System.Threading.Tasks.Task WaitForRetryAsync(int milliseconds)
        {
            var elapsed = Stopwatch.StartNew();
            while (elapsed.ElapsedMilliseconds < milliseconds)
                await System.Threading.Tasks.Task.Delay(200, lifetime.Token);
        }
        private async System.Threading.Tasks.Task<bool> StartWorkspaceAsync(TeklaSessionTarget target, List<TeklaSessionTarget> targets)
        {
            string token = "Local\\TTSK-start-" + Guid.NewGuid().ToString("N");
            using (var ready = new EventWaitHandle(false, EventResetMode.ManualReset, token + "-ready"))
            using (var accept = new EventWaitHandle(false, EventResetMode.ManualReset, token + "-accept"))
            {
                try
                {
                    var start = TeklaSessionCoordinator.CreateStartInfo(target, token);
                    start.EnvironmentVariables["TTSK_SESSION_FADE"] = "1";
                    start.EnvironmentVariables["TTSK_SESSION_KNOWN"] = string.Join(";", targets.Select(t => t.Id + ":" + t.Started));
                    // Khởi động mới không kế thừa vị trí cũ từ launcher/worker khác.
                    start.EnvironmentVariables.Remove("TTSK_SESSION_X"); start.EnvironmentVariables.Remove("TTSK_SESSION_Y");
                    child = Process.Start(start);
                    var timeout = Stopwatch.StartNew();
                    while (!ready.WaitOne(0))
                    {
                        if (child.HasExited || timeout.ElapsedMilliseconds >= 30000) return false;
                        await System.Threading.Tasks.Task.Delay(50, lifetime.Token);
                    }
                    if (child.HasExited) return false;
                    SetStatus("Sẵn sàng");
                    // Chuyển cảnh 140 ms theo thời gian thực; không thêm thời gian chờ logo bắt buộc.
                    var fade = Stopwatch.StartNew();
                    while (fade.ElapsedMilliseconds < 140)
                    {
                        if (waitingPresentation) Opacity = Math.Max(.04, 1 - fade.ElapsedMilliseconds / 140.0);
                        else if (logoWindow != null) logoWindow.ShowLogo((byte)Math.Max(0, 255 - fade.ElapsedMilliseconds * 255 / 140));
                        await System.Threading.Tasks.Task.Delay(16, lifetime.Token);
                    }
                    lifetime.Token.ThrowIfCancellationRequested();
                    accept.Set();
                    handedOff = true;
                    Close();
                    return true;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    TTSK_AutoDim_Plates.Updater.UpdateSecurity.Log(AppDomain.CurrentDomain.BaseDirectory, "Startup handoff: " + ex);
                    return false;
                }
                finally
                {
                    if (!handedOff) StopChild();
                    if (child != null) { child.Dispose(); child = null; }
                }
            }
        }
        private void StopChild()
        {
            if (child == null) return;
            try { if (!child.HasExited) child.Kill(); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            lifetime.Cancel();
            if (logoWindow != null) logoWindow.HideLogo();
            if (!handedOff) StopChild();
            base.OnFormClosed(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lifetime.Cancel(); animation.Dispose();
                if (!handedOff) StopChild();
                if (logoWindow != null) { logoWindow.Dispose(); logoWindow = null; }
                if (logo != null) { logo.Dispose(); logo = null; }
                if (startupThemeIcon != null) { startupThemeIcon.Dispose(); startupThemeIcon = null; }
                if (renderedLogo != null) { renderedLogo.Dispose(); renderedLogo = null; }
                brandFont.Dispose(); captionFont.Dispose();
            }
            base.Dispose(disposing);
        }
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        private sealed class StartupEntryButton : Button
        {
            private readonly bool dark, meeting;
            private bool hover, pressed;
            internal StartupEntryButton(bool dark, bool meeting)
            {
                this.dark = dark; this.meeting = meeting;
                Cursor = Cursors.Hand;
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }
            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
            protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
            protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { pressed = true; Invalidate(); } base.OnKeyDown(e); }
            protected override void OnKeyUp(KeyEventArgs e) { pressed = false; Invalidate(); base.OnKeyUp(e); }
            protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
            protected override void OnLostFocus(EventArgs e) { pressed = false; Invalidate(); base.OnLostFocus(e); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Parent == null ? BackColor : Parent.BackColor);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                // Toàn bộ hình/chữ co theo DPI của ô; viền nổi nhẹ, không gạch chân hay dấu chọn.
                g.ScaleTransform(Width / 200F, Height / 114F);
                bool active = hover || Focused;
                Color ink = dark ? Color.FromArgb(239, 176, 120) : Color.FromArgb(37, 79, 169);
                Color fill = dark ? (meeting ? Color.FromArgb(22, 25, 31) : Color.FromArgb(43, 33, 27))
                    : (meeting ? Color.White : Color.FromArgb(230, 240, 255));
                if (active) fill = dark ? Color.FromArgb(51, 39, 31) : Color.FromArgb(221, 235, 255);
                if (pressed) fill = dark ? Color.FromArgb(31, 26, 23) : Color.FromArgb(207, 225, 250);
                Color edge = dark ? Color.FromArgb(active ? 195 : 139, active ? 137 : 104, active ? 88 : 77)
                    : Color.FromArgb(active ? 95 : 153, active ? 142 : 176, active ? 211 : 207);
                using (var shadow = Rounded(new RectangleF(1, 5, 198, 108), 14))
                using (var shadowBrush = new SolidBrush(dark ? Color.FromArgb(75, Color.Black) : Color.FromArgb(22, 45, 60, 82)))
                    g.FillPath(shadowBrush, shadow);
                using (var outline = Rounded(new RectangleF(1, 1, 198, 107), 14))
                using (var brush = new SolidBrush(fill))
                using (var pen = new Pen(edge, active ? 1.5F : 1F))
                {
                    g.FillPath(brush, outline); g.DrawPath(pen, outline);
                }
                using (var pen = new Pen(ink, 1.6F) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round })
                {
                    if (meeting)
                    {
                        g.DrawLines(pen, new[] { new PointF(88, 64), new PointF(112, 64), new PointF(112, 81),
                            new PointF(99, 81), new PointF(93, 87), new PointF(93, 81), new PointF(88, 81), new PointF(88, 64) });
                        g.DrawLine(pen, 94, 70, 106, 70); g.DrawLine(pen, 94, 75, 102, 75);
                    }
                    else
                    {
                        g.DrawRectangle(pen, 87, 64, 26, 19);
                        g.DrawLine(pen, 92, 88, 108, 88); g.DrawLine(pen, 100, 83, 100, 88);
                        g.DrawLine(pen, 92, 70, 98, 70); g.DrawLine(pen, 92, 75, 105, 75);
                    }
                    g.DrawLine(pen, 170, 76, 181, 76);
                    g.DrawLines(pen, new[] { new PointF(177, 72), new PointF(181, 76), new PointF(177, 80) });
                }
                // Tiêu đề trên biểu tượng, chừa 12 px hai bên và đủ chiều cao cho dấu tiếng Việt.
                using (var titleFont = new Font("Segoe UI", 17F, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var titleBrush = new SolidBrush(ink))
                using (var format = new StringFormat { Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
                {
                    g.DrawString(Text, titleFont, titleBrush, new RectangleF(12, 14, 176, 34), format);
                }
                g.ResetTransform();
            }
            private static System.Drawing.Drawing2D.GraphicsPath Rounded(RectangleF rect, float radius)
            {
                var path = new System.Drawing.Drawing2D.GraphicsPath();
                float d = radius * 2;
                path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
                path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
                path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
                path.CloseFigure(); return path;
            }
        }
    }

    internal static class TeklaSessionCoordinator
    {
        // Trạng thái chỉ của tiến trình; không lưu lựa chọn/PID vào model hoặc cấu hình lâu dài.
        internal static TeklaSessionTarget Current { get; private set; }
        internal static bool FollowForeground = true;
        internal static string ModelPath { get; private set; }
        private const string Prefix = "TTSK_SESSION_";
        private static readonly object discoveryLock = new object();
        private static readonly Dictionary<string, string> binCache = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> channelSuffixCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        internal static List<TeklaSessionTarget> Discover()
        { return DiscoverProcesses(0); }

        private static List<TeklaSessionTarget> DiscoverProcesses(int requestedId)
        {
            var targets = new List<TeklaSessionTarget>();
            Process[] processes;
            try { processes = requestedId == 0 ? Process.GetProcessesByName("TeklaStructures") : new[] { Process.GetProcessById(requestedId) }; }
            catch (ArgumentException) { return targets; }
            using (var own = Process.GetCurrentProcess())
            foreach (var process in processes)
            using (process)
            {
                try
                {
                    if (process.SessionId != own.SessionId || process.MainWindowHandle == IntPtr.Zero
                        || !string.Equals(process.ProcessName, "TeklaStructures", StringComparison.OrdinalIgnoreCase)) continue;
                    long started = process.StartTime.ToUniversalTime().Ticks;
                    string key = process.Id + ":" + started, bin;
                    lock (discoveryLock)
                    {
                        if (!binCache.TryGetValue(key, out bin))
                        {
                            if (!process.MainModule.FileVersionInfo.FileVersion.StartsWith("2025.", StringComparison.Ordinal)) continue;
                            bin = Path.GetDirectoryName(process.MainModule.FileName);
                            if (binCache.Count > 64) binCache.Clear(); // Cache nhỏ cho metadata bất biến của process.
                            binCache[key] = bin;
                        }
                    }
                    var target = new TeklaSessionTarget { Id = process.Id, Started = started, Title = process.MainWindowTitle, Bin = bin };
                    // Không đọc memory khi Tekla còn ở màn hình khởi động; SESSIONNAME có thể đổi lúc tạo kênh.
                    if (!string.IsNullOrEmpty(target.ModelFolder)) target.Session = ReadSessionName(process.Id);
                    targets.Add(target);
                }
                catch (Exception) { /* Phiên đã đóng hoặc không có quyền đọc: không đoán kênh kết nối. */ }
            }
            return targets.OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        internal static bool Prepare()
        {
            string requested = Environment.GetEnvironmentVariable(Prefix + "PID");
            TeklaSessionTarget selected;
            if (!string.IsNullOrEmpty(requested))
            {
                int id; long started;
                if (!int.TryParse(requested, out id) || !long.TryParse(Environment.GetEnvironmentVariable(Prefix + "START"), out started))
                    throw new InvalidOperationException("Thông tin phiên Tekla không hợp lệ.");
                selected = DiscoverProcesses(id).FirstOrDefault(t => t.Id == id && t.Started == started);
                if (selected == null) throw new InvalidOperationException("Phiên Tekla đã đóng hoặc không thể truy cập. Hãy mở lại TTSK để chọn dự án.");
                FollowForeground = Environment.GetEnvironmentVariable(Prefix + "AUTO") != "0";
            }
            else
            {
                var targets = Discover();
                if (targets.Count == 0) throw new InvalidOperationException("Không tìm thấy phiên Tekla Structures 2025 có thể truy cập. Hãy mở model và chạy TTSK cùng quyền với Tekla.");
                targets = targets.Where(t => !string.IsNullOrEmpty(t.ModelFolder)).ToList();
                if (targets.Count == 0) throw new InvalidOperationException("Tekla đang mở model. Hãy chạy TTSK khi model sẵn sàng.");
                selected = targets.Count == 1 ? targets[0] : Choose(null, targets);
                if (selected == null) return false;
            }
            Current = selected;
            if (string.IsNullOrEmpty(selected.Session)) throw new InvalidOperationException("Tekla đang mở model.");
            // Tekla 2025 chốt SESSIONNAME trong static Channels: phải đặt trước mọi Model/Drawing API.
            Environment.SetEnvironmentVariable("SESSIONNAME", selected.Session);
            Environment.SetEnvironmentVariable("TeklaBinPath", selected.Bin);
            Environment.SetEnvironmentVariable(Prefix + "PID", selected.Id.ToString());
            Environment.SetEnvironmentVariable(Prefix + "START", selected.Started.ToString());
            Environment.SetEnvironmentVariable(Prefix + "AUTO", FollowForeground ? "1" : "0");
            return true;
        }

        internal static bool IsReady(TeklaSessionTarget target)
        {
            if (target == null || string.IsNullOrEmpty(target.ModelFolder) || string.IsNullOrEmpty(target.Session)) return false;
            try
            {
                using (var process = Process.GetProcessById(target.Id))
                {
                    if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != target.Started || !process.Responding) return false;
                    string suffix;
                    lock (discoveryLock)
                    {
                        if (!channelSuffixCache.TryGetValue(target.Bin, out suffix))
                        {
                            var version = FileVersionInfo.GetVersionInfo(Path.Combine(target.Bin, "Tekla.Structures.dll"));
                            suffix = "-" + version.ProductName.Replace(" ", "") + "-{0}:" + version.FileVersion + "$S";
                            channelSuffixCache[target.Bin] = suffix;
                        }
                    }
                    // Chỉ mở event kênh 2025 đã tồn tại, không khởi tạo API/static proxy trong tiến trình chờ.
                    foreach (string api in new[] { "Tekla.Structures.Model", "Tekla.Structures.Drawing" })
                    {
                        EventWaitHandle channel;
                        if (!EventWaitHandle.TryOpenExisting(api + string.Format(suffix, target.Session), out channel)) return false;
                        channel.Dispose();
                    }
                    return true;
                }
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (System.ComponentModel.Win32Exception) { return false; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        internal static TeklaSessionTarget Choose(IWin32Window owner, List<TeklaSessionTarget> targets)
        {
            bool dark = false;
            var host = owner as Control;
            if (host != null) dark = host.BackColor.GetBrightness() < .4F;
            else
            {
                try { dark = string.Equals(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "theme.cfg")).Trim(), "DARK", StringComparison.OrdinalIgnoreCase); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            using (var dialog = new TeklaProjectPickerForm(targets, dark, Current, ForegroundProcessId()))
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK) return null;
                FollowForeground = true;
                Environment.SetEnvironmentVariable(Prefix + "AUTO", "1");
                return dialog.SelectedTarget;
            }
        }

        internal static Process GetBoundProcess()
        {
            var target = Current;
            if (target == null) throw new InvalidOperationException("Chưa chọn phiên Tekla.");
            var process = Process.GetProcessById(target.Id);
            try
            {
                if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != target.Started
                    || !string.Equals(process.ProcessName, "TeklaStructures", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Phiên Tekla đã thay đổi. Hãy chọn lại dự án.");
                return process;
            }
            catch { process.Dispose(); throw; }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal static void VerifyConnection()
        {
            using (var process = GetBoundProcess())
            {
                if (!string.Equals(ReadSessionName(process.Id), Current.Session, StringComparison.Ordinal))
                    throw new InvalidOperationException("Kênh Tekla đã đổi. Hãy chọn lại dự án.");
                var model = new Tekla.Structures.Model.Model();
                var drawing = new Tekla.Structures.Drawing.DrawingHandler();
                if (!model.GetConnectionStatus() || !drawing.GetConnectionStatus())
                    throw new InvalidOperationException("Không kết nối được cả Model và Drawing của phiên đã chọn.");
                var info = model.GetInfo();
                if (info == null || string.IsNullOrWhiteSpace(info.ModelPath))
                    throw new InvalidOperationException("Phiên Tekla chưa mở model.");
                // Xác minh độc lập kênh API với cửa sổ đã chọn; không lấy tên cửa sổ làm đường dẫn để ghi.
                string path = info.ModelPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!MatchesModelTitle(process.MainWindowTitle, path))
                    throw new InvalidOperationException("Model API không khớp cửa sổ Tekla đã chọn; đã chặn lệnh.");
                if (ModelPath != null && !string.Equals(ModelPath, path, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Model trong phiên Tekla đã đổi. Bấm tên dự án để kết nối lại và xóa ngữ cảnh cũ.");
                string inheritedPath = Environment.GetEnvironmentVariable(Prefix + "MODEL");
                if (!string.IsNullOrEmpty(inheritedPath) && !string.Equals(inheritedPath, path, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Model đã đổi từ lúc khởi chạy worker; đã chặn lệnh.");
                ModelPath = path;
                Environment.SetEnvironmentVariable(Prefix + "MODEL", path);
            }
        }

        internal static ProcessStartInfo CreateStartInfo(TeklaSessionTarget target, string handoff)
        {
            var start = new ProcessStartInfo(typeof(TeklaSessionCoordinator).Assembly.Location) { UseShellExecute = false,
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory };
            start.EnvironmentVariables[Prefix + "PID"] = target.Id.ToString();
            start.EnvironmentVariables[Prefix + "START"] = target.Started.ToString();
            start.EnvironmentVariables[Prefix + "AUTO"] = FollowForeground ? "1" : "0";
            // Chốt cả model của lựa chọn: nếu người dùng đổi model cùng PID giữa lúc bàn giao thì từ chối.
            start.EnvironmentVariables[Prefix + "MODEL"] = target.ModelFolder;
            start.EnvironmentVariables[Prefix + "HANDOFF"] = handoff;
            using (var parent = Process.GetCurrentProcess())
            {
                start.EnvironmentVariables[Prefix + "PARENT"] = parent.Id.ToString();
                start.EnvironmentVariables[Prefix + "PARENT_START"] = parent.StartTime.ToUniversalTime().Ticks.ToString();
            }
            return start;
        }

        internal static bool MatchesModelTitle(string title, string path)
        {
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(path)) return false;
            // Dùng chung parser với discovery; chỉ bỏ khoảng trắng thừa, không chấp nhận tên model là prefix của model khác.
            string titlePath = new TeklaSessionTarget { Title = title }.ModelFolder;
            return !string.IsNullOrEmpty(titlePath) && string.Equals(titlePath.Trim().TrimEnd('\\', '/'),
                path.Trim().TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }

        internal static bool SameWorkspace(TeklaSessionTarget target, TeklaSessionTarget bound, string modelPath)
        {
            return target != null && bound != null && target.Id == bound.Id && target.Started == bound.Started
                && string.Equals(target.Session, bound.Session, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(target.ModelFolder) && !string.IsNullOrWhiteSpace(modelPath)
                && string.Equals(target.ModelFolder.Trim().TrimEnd('\\', '/'), modelPath.Trim().TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }

        internal static bool HasProcessExited(TeklaSessionTarget target)
        {
            if (target == null) return false;
            try
            {
                using (var process = Process.GetProcessById(target.Id))
                    return process.HasExited || process.StartTime.ToUniversalTime().Ticks != target.Started
                        || !string.Equals(process.ProcessName, "TeklaStructures", StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException) { return true; }
            catch (Exception) { return false; } // Không đọc được process không đồng nghĩa phiên đã thoát.
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal static bool IsBoundModelClosed()
        {
            try
            {
                using (var process = GetBoundProcess())
                {
                    // Chỉ nhận đóng thật khi cửa sổ Tekla còn phản hồi, hết model trong title và API xác nhận hết model.
                    string title = process.MainWindowTitle;
                    if (!process.Responding || string.IsNullOrWhiteSpace(title)
                        || !title.StartsWith("Tekla Structures", StringComparison.OrdinalIgnoreCase)
                        || !string.IsNullOrWhiteSpace(new TeklaSessionTarget { Title = title }.ModelFolder)) return false;
                    var model = new Tekla.Structures.Model.Model();
                    var info = model.GetInfo();
                    return info != null && string.IsNullOrWhiteSpace(info.ModelPath) && !model.GetConnectionStatus();
                }
            }
            catch (Exception) { return false; } // Timeout/lỗi quyền/title trung gian: chưa có bằng chứng đóng model.
        }

        internal static bool AcceptHandoff()
        {
            string token = Environment.GetEnvironmentVariable(Prefix + "HANDOFF");
            Environment.SetEnvironmentVariable(Prefix + "HANDOFF", null);
            if (string.IsNullOrEmpty(token)) return true;
            using (var ready = EventWaitHandle.OpenExisting(token + "-ready"))
            using (var accept = EventWaitHandle.OpenExisting(token + "-accept"))
            {
                ready.Set();
                if (!accept.WaitOne(30000)) return false;
            }
            int parent; long parentStarted;
            if (int.TryParse(Environment.GetEnvironmentVariable(Prefix + "PARENT"), out parent))
                try
                {
                    using (var p = Process.GetProcessById(parent))
                        if (!long.TryParse(Environment.GetEnvironmentVariable(Prefix + "PARENT_START"), out parentStarted)
                            || p.StartTime.ToUniversalTime().Ticks != parentStarted || !p.WaitForExit(10000)) return false;
                }
                catch (ArgumentException) { }
            return true;
        }

        internal static int ForegroundProcessId()
        {
            uint id; GetWindowThreadProcessId(GetForegroundWindow(), out id); return (int)id;
        }

        // Chỉ đọc SESSIONNAME của tiến trình x64 cùng phiên Windows. Không đọc/ghi file trong model.
        // PEB x64: ProcessParameters +0x20; RTL_USER_PROCESS_PARAMETERS.Environment +0x80.
        // Không có quyền hoặc cấu trúc không hợp lệ thì từ chối, không fallback sang phiên đầu tiên.
        internal static string ReadSessionName(int id)
        {
            IntPtr process = OpenProcess(0x410, false, id);
            if (process == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
            try
            {
                bool wow64;
                if (IntPtr.Size != 8 || !IsWow64Process(process, out wow64) || wow64)
                    throw new InvalidOperationException("Cần Tekla và TTSK x64.");
                var info = new IntPtr[6]; int returned;
                if (NtQueryInformationProcess(process, 0, info, IntPtr.Size * info.Length, out returned) != 0)
                    throw new InvalidOperationException("Không đọc được phiên Tekla.");
                IntPtr parameters = ReadPointer(process, IntPtr.Add(info[1], 0x20));
                IntPtr environment = ReadPointer(process, IntPtr.Add(parameters, 0x80));
                var entry = new StringBuilder();
                // Giới hạn 1 MiB để không quét vô hạn khi tiến trình đóng/đổi environment.
                for (int offset = 0; offset < 1024 * 1024;)
                {
                    long address = environment.ToInt64() + offset;
                    int size = (int)Math.Min(4096 - (address & 4095), 1024 * 1024 - offset);
                    var buffer = new byte[size]; IntPtr read;
                    if (!ReadProcessMemory(process, new IntPtr(address), buffer, size, out read) || read.ToInt64() != size)
                        throw new InvalidOperationException("Không đọc được kênh Tekla.");
                    for (int i = 0; i + 1 < size; i += 2)
                    {
                        char c = (char)(buffer[i] | buffer[i + 1] << 8);
                        if (c != '\0') { entry.Append(c); continue; }
                        if (entry.Length == 0) return "Console";
                        string value = entry.ToString(); entry.Clear();
                        if (value.StartsWith("SESSIONNAME=", StringComparison.OrdinalIgnoreCase))
                            return value.Length == 12 ? "Console" : value.Substring(12);
                    }
                    offset += size;
                }
                throw new InvalidOperationException("Environment Tekla vượt giới hạn đọc.");
            }
            finally { CloseHandle(process); }
        }

        private static IntPtr ReadPointer(IntPtr process, IntPtr address)
        {
            var buffer = new byte[8]; IntPtr read;
            if (!ReadProcessMemory(process, address, buffer, 8, out read) || read.ToInt64() != 8)
                throw new InvalidOperationException("Không đọc được địa chỉ phiên Tekla.");
            return new IntPtr(BitConverter.ToInt64(buffer, 0));
        }
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(int access, bool inherit, int id);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")] private static extern bool IsWow64Process(IntPtr process, out bool wow64);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int size, out IntPtr read);
        [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(IntPtr process, int kind, [Out] IntPtr[] info, int size, out int returned);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint id);
    }
}
#endregion

#region Class1 - Kiểu tương thích cũ, không có thuật toán


namespace TTSK_Dim_Plates
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    internal class Class1 { }
}
#endregion

#region Program - Điểm khởi động ứng dụng


namespace TTSK_AutoDim_Plates
{
    using System;
    using System.IO;
    using System.Windows.Forms;
    using TTSK_AutoDim_Plates.Updater;

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // 1. Kiểm tra chế độ Worker Cập nhật / Phục hồi độc lập trước khi tải bất kỳ thư viện Tekla nào
            if (args != null && args.Length > 0 &&
                (string.Equals(args[0], UpdateWorker.ModeApplyUpdate, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(args[0], UpdateWorker.ModeRecovery, StringComparison.OrdinalIgnoreCase)))
            {
                return UpdateWorker.Run(args);
            }

            string appDir = AppDomain.CurrentDomain.BaseDirectory;

            try
            {
                // Serialize admission with worker READY. Keep a process lease until normal/worker exit.
                using (var admission = new UpdateLock(appDir))
                {
                    if (!admission.TryAcquireUpdateLock(0)) return 1;
                    string journalPath = UpdateJournal.GetJournalPath(appDir);
                    if (File.Exists(journalPath))
                    {
                        var journal = UpdateJournal.LoadFromFile(journalPath);
                        if (journal == null) throw new InvalidDataException("Recovery journal is invalid.");
                        if (journal.State == UpdateTransactionState.Committed || journal.State == UpdateTransactionState.RolledBack)
                            UpdateJournal.DeleteJournal(journalPath);
                        else return UpdateWorker.StartRecoveryFromStaging(appDir);
                    }
                    using (var lease = UpdateWorker.RegisterInstance(appDir))
                    {
                        admission.Release();
                        // Worker xuất ảnh đã có đầu vào độc lập, không yêu cầu phiên Tekla còn mở.
                        bool snapshotWorker = args != null && args.Length > 0 && args[0] == "--snapshot-png-worker";
                        bool apiWorker = args != null && args.Length > 0
                            && (string.Equals(args[0], "--hole-mark-post-dim-worker", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(args[0], "--print-color-worker", StringComparison.OrdinalIgnoreCase));
                        apiWorker = apiWorker || (args != null && args.Length > 0
                            && string.Equals(args[0], "--print-list-worker", StringComparison.OrdinalIgnoreCase));
                        if (!snapshotWorker && !apiWorker)
                        {
                            // Phải cấu hình WinForms trước cả hộp chọn phiên đầu tiên.
                            Application.EnableVisualStyles();
                            Application.SetCompatibleTextRenderingDefault(false);
                        }
                        if (apiWorker && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TTSK_SESSION_PID")))
                            throw new InvalidOperationException("Worker chưa có phiên Tekla được chỉ định.");
                        if (!snapshotWorker && !apiWorker && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TTSK_SESSION_HANDOFF")))
                        {
                            using (var startup = new TeklaStartupForm())
                            {
                                Application.Run(startup);
                                if (startup.OpenMeetingTranslation) return RunMeetingTranslation(startup.DarkMode);
                                if (startup.OpenWorkJournal) return RunWorkJournal(startup.DarkMode);
                                // Vào UI/nhật ký ngay; phiên Tekla sẽ được bàn giao sau khi sẵn sàng.
                                if (startup.OpenWithoutConnection) return ProgramTeklaStartup.Run(args, true);
                                return startup.ExitCode;
                            }
                        }
                        if (!snapshotWorker && !TeklaSessionCoordinator.Prepare()) return 0;
                        return ProgramTeklaStartup.Run(args);
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateSecurity.Log(appDir, "Startup gate: " + ex);
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TTSK_SESSION_HANDOFF"))) return 1;
                MessageBox.Show("Không thể mở TTSK an toàn: " + ex.Message, "TTSK Dim Plates");
                return 1;
            }
        }
        // Dịch họp có vòng đời riêng; không nạp MainForm hoặc dò thư viện/kết nối Tekla.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static int RunMeetingTranslation(bool darkMode)
        {
            using (var meeting = new MeetingTranslation.MeetingTranslatorForm(darkMode))
            {
                meeting.StartPosition = FormStartPosition.CenterScreen;
                Application.Run(meeting);
            }
            return 0;
        }
        // Nhật ký đã lưu mở độc lập từ màn chờ; không cần MainForm hay truy cập model Tekla.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static int RunWorkJournal(bool darkMode)
        {
            using (var journal = new WorkJournalReportForm(() =>
            {
                int unreadable;
                var sessions = WorkJournal.Read(WorkJournal.Folder, out unreadable);
                if (unreadable > 0) throw new IOException("Không đọc được " + unreadable + " tệp nhật ký.");
                return sessions;
            }, darkMode))
            {
                journal.StartPosition = FormStartPosition.CenterScreen;
                Application.Run(journal);
            }
            return 0;
        }
    }
}
#endregion

#region Program.TeklaStartup - Tìm Tekla và nạp assembly lúc khởi động


namespace TTSK_AutoDim_Plates
{
    using System;
    using System.IO;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Windows.Forms;

    /// <summary>
    /// Chứa toàn bộ logic khởi động Tekla và dispatch các worker Tekla hiện hữu.
    /// Được cách ly hoàn toàn qua MethodImplOptions.NoInlining để đảm bảo JIT không tải trước các dependency Tekla khi chạy ở chế độ updater.
    /// </summary>
    internal static class ProgramTeklaStartup
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Run(string[] args)
        {
            return Run(args, false);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Run(string[] args, bool openWithoutConnection)
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

            if (!openWithoutConnection) TeklaSessionCoordinator.VerifyConnection();

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

            // Worker chỉ dành cho danh sách cộng dồn; nhánh --print-color-worker cũ giữ nguyên.
            if (args != null && args.Length >= 3 && string.Equals(args[0], "--print-list-worker", StringComparison.OrdinalIgnoreCase))
                return DrawingPdfPrinter.ExecuteListColorPrintWorker(args[1], args[2]);

            using (var form = new MainForm())
            {
                if (!TeklaSessionCoordinator.AcceptHandoff()) return 1;
                if (Environment.GetEnvironmentVariable("TTSK_SESSION_FADE") == "1")
                {
                    Environment.SetEnvironmentVariable("TTSK_SESSION_FADE", null);
                    form.Opacity = .04;
                    var transition = new System.Windows.Forms.Timer { Interval = 16 };
                    var elapsed = new System.Diagnostics.Stopwatch();
                    form.Shown += delegate { elapsed.Start(); transition.Start(); };
                    transition.Tick += delegate
                    {
                        if (form.IsDisposed) { transition.Dispose(); return; }
                        form.Opacity = Math.Min(1, .04 + elapsed.ElapsedMilliseconds / 180.0);
                        if (form.Opacity >= 1) { transition.Stop(); transition.Dispose(); }
                    };
                    form.Disposed += delegate { transition.Dispose(); };
                }
                Application.Run(form);
            }
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
#endregion
