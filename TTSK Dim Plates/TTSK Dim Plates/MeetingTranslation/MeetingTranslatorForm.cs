using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TTSK_AutoDim_Plates.MeetingTranslation
{
    internal sealed class MeetingApiPickerForm : Form
    {
        private readonly ApiChoiceButton _googleButton, _nvidiaButton, _model;
        private readonly MeetingWindowPicker.MeetingDropDown _modelMenu;
        private readonly MeetingWindowPicker.MeetingDropDown _keyMenu;
        private readonly ApiChoiceButton _savedKeys;
        private readonly Button _addKey, _deleteKey, _saveKey;
        private List<MeetingApiKeyProfile> _profiles = new List<MeetingApiKeyProfile>();
        public bool KeyRingChanged { get; private set; }
        private readonly List<string> _models = new List<string>();
        private string _selectedModel;
        private readonly bool _dark;
        private readonly Color _muted, _accent, _surface, _border;
        private readonly TextBox _key;
        private readonly Label _status;
        private readonly Button _check, _apply;
        private readonly GoogleMeetingSettings _savedGoogle;
        private readonly NvidiaMeetingSettings _savedNvidia;
        private readonly Dictionary<int, string> _keys = new Dictionary<int, string>();
        private CancellationTokenSource _validation;
        private int _previousProvider;
        private bool _busy;
        private readonly bool _liveMode;
        private string _verifiedNvidiaKey;
        private string _checkedGoogleKey;
        public GoogleMeetingSettings Google { get; private set; }
        public NvidiaMeetingSettings Nvidia { get; private set; }

        public MeetingApiPickerForm(MeetingTranslationProvider initial, GoogleMeetingSettings google, NvidiaMeetingSettings nvidia, bool dark, bool liveMode = false)
        {
            _liveMode = liveMode;
            SuspendLayout();
            _savedGoogle = google; _savedNvidia = nvidia; _dark = dark;
            _verifiedNvidiaKey = nvidia.ApiKey;
            _keys[0] = google.ApiKey; _keys[1] = nvidia.ApiKey;
            Text = "Chọn model API"; StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(600, 510); Font = new Font("Segoe UI", 9.5F);
            BackColor = dark ? Color.FromArgb(19, 21, 26) : Color.FromArgb(248, 250, 253);
            ForeColor = dark ? Color.FromArgb(236, 240, 248) : Color.FromArgb(29, 39, 57);
            _surface = dark ? Color.FromArgb(28, 31, 37) : Color.White;
            _border = dark ? Color.FromArgb(73, 56, 43) : Color.FromArgb(219, 225, 235);
            _muted = dark ? Color.FromArgb(154, 160, 172) : Color.FromArgb(104, 117, 139);
            _accent = dark ? Color.FromArgb(222, 150, 89) : Color.FromArgb(40, 85, 183);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 10, Margin = Padding.Empty };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int height in new[] { 64, 64, 64, 24, 44, 18, 24, 50, 66, 44 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            var heading = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            heading.Controls.Add(new Label { Text = "API cho phiên dịch", Font = new Font("Segoe UI", 18F, FontStyle.Bold), AutoSize = true, Location = new Point(0, 0) });
            heading.Controls.Add(new Label { Text = "Đổi ngay khi cần. Giữ nguyên các bản dịch đã có.", ForeColor = _muted, AutoSize = true, Location = new Point(1, 36) });
            layout.Controls.Add(heading, 0, 0);
            var providers = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            providers.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); providers.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            _googleButton = Choice("Google Gemini", "Chọn model theo API key", "G", _accent);
            _nvidiaButton = Choice("NVIDIA Riva", "Model cố định", "N", dark ? _accent : Color.FromArgb(40, 126, 79));
            _googleButton.Margin = new Padding(0, 0, 6, 0); _nvidiaButton.Margin = new Padding(6, 0, 0, 0);
            providers.Controls.Add(_googleButton, 0, 0); providers.Controls.Add(_nvidiaButton, 1, 0); layout.Controls.Add(providers, 0, 1);
            var keyRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0, 10, 0, 8) };
            keyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); keyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105)); keyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            _savedKeys = Choice("Key đang dùng", "Chọn từ danh sách đã lưu", "", _accent); _savedKeys.DropDown = true;
            _savedKeys.Margin = new Padding(0, 0, 8, 0); _savedKeys.AccessibleName = "Danh sách API key đã lưu";
            _addKey = ActionButton("+ Thêm key", false); _addKey.Dock = DockStyle.Fill; _addKey.Margin = new Padding(0, 0, 8, 0);
            _deleteKey = ActionButton("Xóa key", false); _deleteKey.Dock = DockStyle.Fill;
            keyRow.Controls.Add(_savedKeys, 0, 0); keyRow.Controls.Add(_addKey, 1, 0); keyRow.Controls.Add(_deleteKey, 2, 0); layout.Controls.Add(keyRow, 0, 2);
            layout.Controls.Add(FieldLabel("API KEY"), 0, 3);
            var keyFrame = new RoundPanel(9, _surface, _border) { Dock = DockStyle.Fill, Padding = new Padding(13, 10, 8, 8), Margin = Padding.Empty };
            _key = new TextBox { UseSystemPasswordChar = true, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, AccessibleName = "API key", BackColor = _surface, ForeColor = ForeColor };
            var show = ActionButton("Hiện", false); show.Dock = DockStyle.Right; show.Width = 58; show.TabStop = true;
            show.Click += delegate { _key.UseSystemPasswordChar = !_key.UseSystemPasswordChar; show.Text = _key.UseSystemPasswordChar ? "Hiện" : "Ẩn"; };
            keyFrame.Controls.Add(_key); keyFrame.Controls.Add(show); layout.Controls.Add(keyFrame, 0, 4);
            layout.Controls.Add(FieldLabel("MODEL DỊCH"), 0, 6);
            var modelRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            modelRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); modelRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 144));
            _model = Choice("", "", "", _accent); _model.Margin = new Padding(0, 0, 10, 0); _model.AccessibleRole = AccessibleRole.ComboBox;
            _check = ActionButton("Kiểm tra model", false); _check.Dock = DockStyle.Fill;
            modelRow.Controls.Add(_model, 0, 0); modelRow.Controls.Add(_check, 1, 0); layout.Controls.Add(modelRow, 0, 7);
            _status = new Label { Dock = DockStyle.Fill, ForeColor = _muted, Padding = new Padding(1, 12, 0, 0), Margin = Padding.Empty };
            layout.Controls.Add(_status, 0, 8);
            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 168));
            var cancel = ActionButton("Hủy", false); cancel.DialogResult = DialogResult.Cancel; cancel.Dock = DockStyle.Fill; cancel.Margin = new Padding(0, 0, 10, 0);
            _apply = ActionButton("Áp dụng ngay", true); _apply.Dock = DockStyle.Fill;
            _saveKey = ActionButton("Lưu dự phòng", false); _saveKey.Dock = DockStyle.Fill; _saveKey.Margin = new Padding(0, 0, 28, 0);
            actions.Controls.Add(_saveKey, 0, 0);
            actions.Controls.Add(cancel, 1, 0); actions.Controls.Add(_apply, 2, 0); layout.Controls.Add(actions, 0, 9);
            Controls.Add(layout); CancelButton = cancel; AcceptButton = _apply;
            _modelMenu = new MeetingWindowPicker.MeetingDropDown(_border, dark) { BackColor = _surface, ForeColor = ForeColor, Font = Font,
                ShowImageMargin = false, ShowCheckMargin = false, Padding = new Padding(6), Renderer = new ApiModelRenderer(_accent, _muted, dark) };
            _keyMenu = new MeetingWindowPicker.MeetingDropDown(_border, dark) { BackColor = _surface, ForeColor = ForeColor, Font = Font,
                ShowImageMargin = false, ShowCheckMargin = false, Padding = new Padding(6), Renderer = new ApiModelRenderer(_accent, _muted, dark) };
            try { _profiles = MeetingApiKeyRing.Load().ToList(); } catch (System.IO.IOException) { }
            _savedKeys.Click += delegate { ShowSavedKeys(); };
            _addKey.Click += delegate { if (_busy) return; _key.Text = string.Empty; _key.Focus(); _status.Text = "Nhập key mới, kiểm tra rồi Lưu dự phòng. Key đang dùng được giữ nguyên."; };
            _deleteKey.Click += delegate { DeleteSavedKey(); };
            _saveKey.Click += async delegate { await ValidateSelectionAsync(true, true); };
            _model.Click += delegate { ShowModels(); };
            _previousProvider = initial == MeetingTranslationProvider.Google ? 0 : 1;
            _key.Text = _keys[_previousProvider];
            _googleButton.Click += delegate { SelectProvider(0); }; _nvidiaButton.Click += delegate { SelectProvider(1); };
            _key.TextChanged += delegate { ReloadModels(); };
            _check.Click += async delegate { await ValidateSelectionAsync(false); };
            _apply.Click += async delegate { await ValidateSelectionAsync(true); };
            FormClosing += delegate { _validation?.Cancel(); };
            ReloadModels();
            // Chỉ scale sau khi dựng đủ control; tránh font đổi sớm làm chốt nhầm DPI thiết kế.
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(true);
        }

        private Label FieldLabel(string text) => new Label { Text = text, ForeColor = _muted, Font = new Font(Font.FontFamily, 8F, FontStyle.Bold), AutoSize = true, Margin = Padding.Empty };

        private Button ActionButton(string text, bool primary)
        {
            var button = new MeetingButton { Text = text, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Margin = Padding.Empty,
                Font = new Font(Font, primary ? FontStyle.Bold : FontStyle.Regular),
                BackColor = primary ? _accent : _surface, ForeColor = primary ? (_dark ? Color.FromArgb(24, 26, 31) : Color.White) : ForeColor };
            button.FlatAppearance.BorderColor = primary ? button.BackColor : _border;
            return button;
        }

        private ApiChoiceButton Choice(string text, string detail, string glyph, Color accent) => new ApiChoiceButton
        {
            Text = text, Detail = detail, Glyph = glyph, Accent = accent, Muted = _muted, Outline = _border, Dark = _dark,
            BackColor = _surface, ForeColor = ForeColor, Font = Font, Dock = DockStyle.Fill, Margin = Padding.Empty,
            FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, AccessibleName = text
        };

        private void SelectProvider(int index)
        {
            if (_liveMode || _busy || _previousProvider == index) return;
            _modelMenu.Close(); _keys[_previousProvider] = _key.Text; _previousProvider = index;
            _key.Text = _keys[index]; ReloadModels();
        }

        private MeetingApiKeyProfile SelectedKey() => _profiles.FirstOrDefault(p => p.Provider == (_previousProvider == 0 ? MeetingTranslationProvider.Google : MeetingTranslationProvider.Nvidia) && p.ApiKey == _key.Text.Trim());

        private void ShowSavedKeys()
        {
            if (_busy) return;
            if (_keyMenu.Visible) { _keyMenu.Close(); return; }
            while (_keyMenu.Items.Count > 0) { var old = _keyMenu.Items[0]; _keyMenu.Items.RemoveAt(0); old.Dispose(); }
            _keyMenu.MinimumSize = new Size(_savedKeys.Width, 0); _keyMenu.MaximumSize = new Size(_savedKeys.Width, 0);
            foreach (var profile in _profiles.Where(p => p.Provider == (_previousProvider == 0 ? MeetingTranslationProvider.Google : MeetingTranslationProvider.Nvidia)))
            {
                var saved = profile;
                var row = new ToolStripMenuItem(saved.Name) { Checked = _key.Text.Trim() == saved.ApiKey, AutoSize = false,
                    Size = new Size(_savedKeys.Width - _keyMenu.Padding.Horizontal, (int)(50 * DeviceDpi / 96F)) };
                row.Click += delegate
                {
                    _key.UseSystemPasswordChar = true; _key.Text = saved.ApiKey;
                    if (saved.Provider == MeetingTranslationProvider.Google && _models.Contains(saved.Model)) { _selectedModel = saved.Model; _model.Text = saved.Model; }
                    if (saved.Provider == MeetingTranslationProvider.Nvidia) _verifiedNvidiaKey = saved.ApiKey;
                    _model.Invalidate();
                };
                _keyMenu.Items.Add(row);
            }
            if (_keyMenu.Items.Count == 0) _keyMenu.Items.Add(new ToolStripMenuItem("Chưa có key đã lưu") { Enabled = false });
            _keyMenu.Show(_savedKeys, new Point(0, _savedKeys.Height + 4));
        }

        private void DeleteSavedKey()
        {
            var selected = SelectedKey(); if (_busy || selected == null) return;
            if (MessageBox.Show(this, "Xóa " + selected.Name + " khỏi danh sách dự phòng? Phiên đang dịch vẫn giữ key hiện tại đến khi đổi API.",
                "Xóa key", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                MeetingApiKeyRing.Remove(selected.Id); KeyRingChanged = true;
                _profiles = MeetingApiKeyRing.Load().ToList(); _key.Text = string.Empty; ReloadModels();
                _status.Text = "Đã xóa key được chọn. Các key khác và lịch sử dịch được giữ nguyên.";
            }
            catch { _status.Text = "Chưa xóa được key. Danh sách trên máy chưa được xác nhận thay đổi."; }
        }

        private void ShowModels()
        {
            if (_busy || _previousProvider != 0 || _models.Count == 0) return;
            if (_modelMenu.Visible) { _modelMenu.Close(); return; }
            while (_modelMenu.Items.Count > 0) { var old = _modelMenu.Items[0]; _modelMenu.Items.RemoveAt(0); old.Dispose(); }
            int width = _model.Width;
            _modelMenu.MinimumSize = new Size(width, 0); _modelMenu.MaximumSize = new Size(width, 0);
            foreach (string model in _models)
            {
                string choice = model;
                var row = new ToolStripMenuItem(model) { AutoSize = false, Size = new Size(width - _modelMenu.Padding.Horizontal, (int)(54 * DeviceDpi / 96F)),
                    Checked = model == _selectedModel, ToolTipText = model, AccessibleName = model };
                row.Click += delegate { _selectedModel = choice; _model.Text = choice; _model.AccessibleName = choice; _model.Invalidate(); _model.Focus(); };
                _modelMenu.Items.Add(row);
            }
            _modelMenu.Show(_model, new Point(0, _model.Height + 5));
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int dark = _dark ? 1 : 0, round = 2;
                DwmSetWindowAttribute(Handle, 20, ref dark, 4); DwmSetWindowAttribute(Handle, 33, ref round, 4);
            }
            catch { }
        }
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        protected override void Dispose(bool disposing) { if (disposing) { _modelMenu?.Dispose(); _keyMenu?.Dispose(); } base.Dispose(disposing); }

        // Thẻ provider và ô model dùng cùng khoảng cách, typography, focus bàn phím.
        private sealed class ApiChoiceButton : Button
        {
            public string Detail, Glyph;
            public bool Selected, DropDown, Dark;
            public Color Accent, Muted, Outline;
            private bool _hover;
            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; g.Clear(Parent.BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
                float s = DeviceDpi / 96F;
                Color soft = Dark ? Color.FromArgb(48, 32, 20) : Color.FromArgb(237, 244, 255);
                using (var path = MeetingVisuals.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), (int)(10 * s)))
                using (var fill = new SolidBrush(Selected || (Enabled && _hover) ? soft : BackColor))
                using (var pen = new Pen(Selected || Focused ? Accent : Outline, Selected ? 1.5F : 1F))
                { g.FillPath(fill, path); g.DrawPath(pen, path); }
                int x = (int)(14 * s), cy = Height / 2;
                if (!string.IsNullOrEmpty(Glyph))
                {
                    var box = new Rectangle(x, cy - (int)(16 * s), (int)(32 * s), (int)(32 * s));
                    using (var path = MeetingVisuals.Rounded(box, (int)(8 * s)))
                    using (var brush = new SolidBrush(Dark ? Color.FromArgb(58, 45, 35) : Color.FromArgb(229, 237, 250))) g.FillPath(brush, path);
                    using (var font = new Font(Font.FontFamily, 13F, FontStyle.Bold))
                        TextRenderer.DrawText(g, Glyph, font, box, Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    x = box.Right + (int)(11 * s);
                }
                int width = Width - x - (int)(30 * s);
                var flags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
                using (var bold = new Font(Font, FontStyle.Bold))
                    TextRenderer.DrawText(g, Text, bold, new Rectangle(x, cy - (int)(21 * s), width, (int)(24 * s)), ForeColor, flags);
                using (var small = new Font(Font.FontFamily, 8.5F))
                    TextRenderer.DrawText(g, Detail, small, new Rectangle(x, cy + (int)(3 * s), width, (int)(19 * s)), Muted, flags);
                using (var pen = new Pen(Accent, 1.7F * s))
                {
                    float right = Width - 20 * s;
                    if (DropDown) g.DrawLines(pen, new[] { new PointF(right - 4 * s, cy - 2 * s), new PointF(right, cy + 2 * s), new PointF(right + 4 * s, cy - 2 * s) });
                    else if (Selected) g.DrawLines(pen, new[] { new PointF(right - 4 * s, cy), new PointF(right - s, cy + 3 * s), new PointF(right + 5 * s, cy - 4 * s) });
                }
            }
            protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) == Keys.Down || base.IsInputKey(keyData);
            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (DropDown && (e.KeyCode == Keys.Down || e.KeyCode == Keys.F4)) { PerformClick(); e.Handled = true; }
                base.OnKeyDown(e);
            }
        }

        private sealed class ApiModelRenderer : ToolStripProfessionalRenderer
        {
            private readonly Color _accent, _muted, _hover;
            public ApiModelRenderer(Color accent, Color muted, bool dark)
            { _accent = accent; _muted = muted; _hover = dark ? Color.FromArgb(48, 32, 20) : Color.FromArgb(237, 244, 255); RoundedEdges = false; }
            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { e.Graphics.Clear(e.ToolStrip.BackColor); }
            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }
            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                if (!e.Item.Selected && !((ToolStripMenuItem)e.Item).Checked) return;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = MeetingVisuals.Rounded(new Rectangle(1, 1, e.Item.Width - 3, e.Item.Height - 3), 8))
                using (var brush = new SolidBrush(_hover)) e.Graphics.FillPath(brush, path);
            }
            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                float s = e.Graphics.DpiX / 96F; int x = (int)(12 * s), cy = e.Item.Height / 2;
                int width = Math.Min(e.Item.Width, e.ToolStrip.Width - e.Item.Bounds.Left) - x - (int)(32 * s);
                var flags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
                using (var bold = new Font(e.TextFont, FontStyle.Bold))
                    TextRenderer.DrawText(e.Graphics, e.Text, bold, new Rectangle(x, cy - (int)(22 * s), width, (int)(24 * s)), e.ToolStrip.ForeColor, flags);
                bool selected = ((ToolStripMenuItem)e.Item).Checked;
                TextRenderer.DrawText(e.Graphics, selected ? "Đang chọn" : "Đã xác minh theo key", e.TextFont,
                    new Rectangle(x, cy + (int)(3 * s), width, (int)(20 * s)), _muted, flags);
                if (selected) using (var pen = new Pen(_accent, 1.7F * s))
                    e.Graphics.DrawLines(pen, new[] { new PointF(e.Item.Width - 24 * s, cy), new PointF(e.Item.Width - 20 * s, cy + 3 * s), new PointF(e.Item.Width - 14 * s, cy - 4 * s) });
            }
        }

        private void ReloadModels()
        {
            if (_liveMode)
            {
                _googleButton.Text = "Google Live"; _googleButton.Detail = "Âm thanh Nhật → Việt";
                _googleButton.Selected = true; _nvidiaButton.Enabled = false;
                _selectedModel = GoogleLiveTranslationSession.Model;
                _model.Text = _selectedModel; _model.Detail = "Model dịch âm thanh trực tiếp";
                _model.Enabled = _model.DropDown = false; _model.Invalidate();
                _savedKeys.Text = SelectedKey()?.Name ?? "Key Google Live";
                _savedKeys.Detail = "Dùng được key Google đã lưu"; _savedKeys.Invalidate();
                _deleteKey.Enabled = false; _saveKey.Text = "Lưu key Live";
                _check.Text = "Kiểm tra key";
                _status.Text = "Kiểm tra chỉ mở kết nối. Âm thanh chỉ được gửi khi bấm Bắt đầu trên cửa sổ dịch.";
                return;
            }
            string previous = _selectedModel;
            _models.Clear(); _selectedModel = null;
            bool google = _previousProvider == 0;
            _googleButton.Selected = google; _nvidiaButton.Selected = !google;
            _googleButton.Invalidate(); _nvidiaButton.Invalidate();
            var savedKey = SelectedKey();
            _savedKeys.Text = savedKey?.Name ?? (string.IsNullOrWhiteSpace(_key.Text) ? "Key mới" : "Key cho phiên này");
            int keyCount = _profiles.Count(p => p.Provider == (google ? MeetingTranslationProvider.Google : MeetingTranslationProvider.Nvidia));
            _savedKeys.Detail = keyCount + " key đã lưu · chọn để đổi"; _savedKeys.Invalidate();
            _deleteKey.Enabled = !_busy && savedKey != null;
            _model.DropDown = google; _model.Enabled = google; _check.Text = google ? "Kiểm tra model" : "Kiểm tra key";
            if (google)
            {
                var models = GoogleJapaneseVietnameseTranslator.GetSavedVerifiedModels(_key.Text.Trim()).ToList();
                if (savedKey != null && _key.Text.Trim() != _checkedGoogleKey) models = models.Concat(savedKey.Models ?? new List<string>()).Where(GoogleMeetingSettings.IsModelName).Distinct().ToList();
                if (_key.Text.Trim() != _checkedGoogleKey && _key.Text.Trim() == _savedGoogle.ApiKey && _savedGoogle.IsReady && !models.Contains(_savedGoogle.Model)) models.Add(_savedGoogle.Model);
                models = GoogleMeetingSettings.OrderVerifiedModels(models).ToList();
                _models.AddRange(models);
                string selected = models.Contains(previous) ? previous : models.Contains(_savedGoogle.Model) ? _savedGoogle.Model : models.FirstOrDefault();
                _selectedModel = selected;
                _model.Text = selected ?? "Chưa có model";
                _model.Detail = models.Count > 0 ? models.Count + " lựa chọn theo key này" : "Kiểm tra key để tải danh sách";
                _status.Text = models.Count == 0 ? "Chưa có model đã xác minh cho key này. Bấm Kiểm tra model (có gọi API)."
                    : "Thử hết model đã xác minh của key hiện tại rồi mới đổi key dự phòng. Giữ nguyên bản dịch đã có.";
            }
            else
            {
                _model.Text = "NVIDIA Riva"; _model.Detail = "Model cố định · Nhật → Việt";
                _status.Text = "Riva dùng model cố định. Khi API lỗi, tự chuyển sang Google nếu đã lưu key và model đã xác minh.";
            }
            _model.Invalidate();
            _model.AccessibleName = _model.Text;
        }

        private async Task ValidateLiveAsync(bool apply, bool saveOnly)
        {
            string key = _key.Text.Trim();
            _busy = true;
            _googleButton.Enabled = _key.Enabled = _check.Enabled = _apply.Enabled = false;
            _savedKeys.Enabled = _addKey.Enabled = _saveKey.Enabled = false;
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25)))
            {
                _validation = cts;
                try
                {
                    _status.Text = "Đang kiểm tra kết nối Google Live…";
                    await GoogleLiveTranslationSession.ValidateAsync(key, cts.Token);
                    if (IsDisposed || cts.IsCancellationRequested) return;
                    if (saveOnly) NvidiaApiKeyStore.SaveTarget(GoogleLiveTranscriptRows.CredentialTarget, key);
                    _status.Text = saveOnly ? "Đã lưu key Live. Key/model Gemini và Riva được giữ nguyên." : "Kết nối thành công. Chưa gửi âm thanh.";
                    if (apply && !saveOnly)
                    {
                        Google = new GoogleMeetingSettings { ApiKey = key, Model = GoogleLiveTranslationSession.Model };
                        DialogResult = DialogResult.OK; Close();
                    }
                }
                catch (Exception ex) { if (!IsDisposed) _status.Text = GoogleLiveTranslationSession.SafeError(ex, key); }
                finally
                {
                    _validation = null; _busy = false;
                    if (!IsDisposed) _googleButton.Enabled = _key.Enabled = _check.Enabled = _apply.Enabled = _savedKeys.Enabled = _addKey.Enabled = _saveKey.Enabled = true;
                }
            }
        }

        private async Task ValidateSelectionAsync(bool apply, bool saveOnly = false)
        {
            if (_busy) return;
            if (_liveMode) { await ValidateLiveAsync(apply, saveOnly); return; }
            string key = _key.Text.Trim(); bool google = _previousProvider == 0;
            var expected = google ? MeetingTranslationProvider.Google : MeetingTranslationProvider.Nvidia;
            var detected = MeetingProviderSettings.DetectKey(key);
            if (key.Length == 0 || (detected.HasValue && detected.Value != expected))
            { _status.Text = "Nhập API key đúng với nhà cung cấp đang chọn."; return; }
            string model = _selectedModel;
            if (google && apply && model == null) { _status.Text = "Bấm Kiểm tra model trước khi áp dụng key này."; return; }
            _busy = true; _googleButton.Enabled = _nvidiaButton.Enabled = _key.Enabled = _model.Enabled = _check.Enabled = _apply.Enabled = false;
            _savedKeys.Enabled = _addKey.Enabled = _deleteKey.Enabled = _saveKey.Enabled = false;
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120)))
            {
                _validation = cts;
                try
                {
                    if (!apply)
                    {
                        _status.Text = google ? "Đang tải và dịch thử các model… Phiên hiện tại vẫn tiếp tục." : "Đang kiểm tra key NVIDIA…";
                        if (google)
                        {
                            await GoogleJapaneseVietnameseTranslator.GetVerifiedModelsAsync(key, cts.Token, true);
                            _checkedGoogleKey = key;
                        }
                        else { await NvidiaApiKeyValidator.ValidateAsync(key, cts.Token); _verifiedNvidiaKey = key; }
                        if (IsDisposed || cts.IsCancellationRequested) return;
                        ReloadModels();
                        if (!google) _status.Text = "Key NVIDIA đã kết nối thành công. Bấm Áp dụng ngay để chuyển.";
                    }
                    else
                    {
                        // Google đã có danh sách theo key; đổi model không quét/dịch thử lần nữa.
                        if (!google && key != _verifiedNvidiaKey)
                        {
                            _status.Text = "Đang xác minh key NVIDIA mới…";
                            await NvidiaApiKeyValidator.ValidateAsync(key, cts.Token);
                            _verifiedNvidiaKey = key;
                        }
                        if (IsDisposed || cts.IsCancellationRequested) return;
                        if (saveOnly)
                        {
                            MeetingApiKeyRing.SaveVerified(new MeetingApiKeyProfile { Provider = expected, ApiKey = key, Model = google ? model : null, Models = _models.ToList() });
                            KeyRingChanged = true; _profiles = MeetingApiKeyRing.Load().ToList(); ReloadModels();
                            _status.Text = "Đã lưu key dự phòng. Chưa đổi key đang dịch; chọn Áp dụng ngay nếu muốn đổi.";
                            return;
                        }
                        if (google) Google = new GoogleMeetingSettings { ApiKey = key, Model = model };
                        else Nvidia = new NvidiaMeetingSettings { ApiKey = key, TranslationPreferences = _savedNvidia.TranslationPreferences };
                        DialogResult = DialogResult.OK; Close();
                    }
                }
                catch (Exception ex)
                {
                    if (!IsDisposed) _status.Text = ex is OperationCanceledException ? "Đã hủy hoặc quá thời gian kiểm tra. Có thể thử lại."
                        : ex is System.IO.IOException ? "Chưa lưu/đọc được kho key. Các key đã có không bị ghi đè."
                        : "Không kiểm tra được API. Kiểm tra key, quota và mạng rồi thử lại.";
                }
                finally
                {
                    _validation = null; _busy = false;
                    if (!IsDisposed)
                    {
                        _googleButton.Enabled = _nvidiaButton.Enabled = _key.Enabled = _check.Enabled = _apply.Enabled = true;
                        _savedKeys.Enabled = _addKey.Enabled = _saveKey.Enabled = true; _deleteKey.Enabled = SelectedKey() != null;
                        _model.Enabled = _previousProvider == 0;
                    }
                }
            }
        }
    }

    public sealed class MeetingTranslatorForm : Form
    {
        private readonly bool _darkMode;
        private bool _closing;
        private bool _layingOutHistory;
        private readonly Dictionary<string, ColorPair> _speakerColors = new Dictionary<string, ColorPair>(StringComparer.OrdinalIgnoreCase);
        private readonly List<MeetingHistoryRow> _historyRows = new List<MeetingHistoryRow>();
        private readonly Dictionary<Tuple<Guid, long>, MeetingHistoryRow> _historyRowsBySequence = new Dictionary<Tuple<Guid, long>, MeetingHistoryRow>();
        private readonly ToolTip _latencyTip = new ToolTip();
        private readonly MeetingReadingFonts _readingFonts = new MeetingReadingFonts();
        private TableLayoutPanel _bodyLayout, _liveGrid;
        private RowStyle _liveRow;
        private Label _historyTitle;
        private float _readingScale = 1F;
        private bool _updatingReadingLayout;
        private bool _subtitleReadingMode;
        private readonly float[] _normalRows = new float[4];
        private readonly float[] _normalColumns = new float[6];
        private Padding _normalBodyPadding, _normalHeaderPadding;

        private TeamsDesktopCaptionCoordinator _coordinator;
        private MeetingSubtitleForm _subtitleOverlay;
        private long _teamsEpoch;
        private Guid _latestSessionId = Guid.Empty;
        private long _latestGeneration;
        private int _latestRevision;
        private bool _liveHasTranslation;
        private string _latestTranslatedJapanese = string.Empty;
        private long _receivingSequence;
        private string _receivingJapanese;
        private bool _translationErrorVisible;
        private string _translationErrorText;
        private bool _isRunning;
        private bool _googleLiveMode, _liveDeviceManual;
        private string _liveKey;
        private MeetingWindowPicker _cboLiveDevices;
        private Label _sourceTitle, _sourceDescription, _sourceTextTitle;
        private CancellationTokenSource _audioCancellation;
        private Task _audioRun;
        private long _audioEpoch;
        private GoogleLiveTranscriptRows _audioRows;
        private readonly Queue<Tuple<string, bool>> _audioText = new Queue<Tuple<string, bool>>();
        private readonly object _audioGate = new object();
        private int _audioTextLength;
        private System.Windows.Forms.Timer _audioTimer;
        private DateTimeOffset _lastSound;
        private string _audioStatus;
        private bool _isBusy;
        private GoogleMeetingSettings _selectedGoogle;
        private bool _explicitGoogleModel; // Chỉ model người dùng chọn trong hộp API của phiên này.
        private NvidiaMeetingSettings _selectedNvidia;
        private bool _showJapanese = true;
        private bool _captionShowJapanese = true, _liveShowJapanese = true;
        private string _lastLiveSource = string.Empty;
        private Panel _liveSeparator;
        private Button _btnLatestTranslation;
        private bool _userScrolledUp;

        // Visual Palette
        private Color _formBack;
        private Color _cardBack;
        private Color _borderColor;
        private Color _textColor;
        private Color _mutedTextColor;
        private Color _accentColor;
        private Color _accentSoftColor;
        private Color _goldAccent;
        private Color _dangerColor;
        private Color _successColor;

        // UI Controls
        private MeetingWindowPicker _cboTeamsWindows;
        private Button _btnRefreshTeams;
        private Button _btnStart;
        private Button _btnStop;
        private Button _btnProvider;
        private ToolStripDropDownMenu _providerMenu;
        private MeetingTranslationProvider _provider = MeetingProviderSettings.Load();
        private Button _btnClearSession;
        private Button _btnExportText;
        private readonly List<MeetingExportEntry> _exportEntries = new List<MeetingExportEntry>();
        private Button _btnRetryPending;
        private Button _btnSubtitleOverlay;
        private CheckBox _chkShowJapanese;
        private Label _lblStatus;
        private Label _lblLatency;
        private Label _lblError;
        private MeetingSelectableText _lblLiveJapanese;
        private MeetingSelectableText _lblLiveVietnamese;
        private MeetingHistoryPanel _pnlHistory;
        private Panel _pnlErrorBar;
        private RowStyle _errorRow;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        public MeetingTranslatorForm(bool darkMode)
        {
            _darkMode = darkMode;

            BuildPalette();
            InitializeComponent();
            RefreshTeamsWindows();
            MeetingApiKeyRing.Changed += OnKeyRingChanged;
        }

        private void OnKeyRingChanged() { SafeInvoke(RefreshFallbackKeys); }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int useDark = _darkMode ? 1 : 0;
                DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
            }
            catch { }
        }

        private void BuildPalette()
        {
            if (_darkMode)
            {
                _formBack = Color.FromArgb(19, 21, 26);
                _cardBack = Color.FromArgb(28, 31, 37);
                _borderColor = Color.FromArgb(73, 56, 43);
                _textColor = Color.FromArgb(242, 244, 248);
                _mutedTextColor = Color.FromArgb(154, 160, 172);
                _accentColor = Color.FromArgb(222, 150, 89);
                _accentSoftColor = Color.FromArgb(48, 32, 20);
                _goldAccent = Color.FromArgb(212, 175, 55);
                _dangerColor = Color.FromArgb(248, 113, 113);
                _successColor = Color.FromArgb(74, 222, 128);
            }
            else
            {
                _formBack = Color.FromArgb(242, 243, 241);
                _cardBack = Color.FromArgb(255, 255, 255);
                _borderColor = Color.FromArgb(205, 212, 223);
                _textColor = Color.FromArgb(37, 44, 56);
                _mutedTextColor = Color.FromArgb(117, 110, 98);
                _accentColor = Color.FromArgb(30, 58, 138);
                _accentSoftColor = Color.FromArgb(252, 243, 235);
                _goldAccent = Color.FromArgb(197, 163, 93);
                _dangerColor = Color.FromArgb(220, 38, 38);
                _successColor = Color.FromArgb(52, 116, 81);
            }
        }

        private void InitializeComponent()
        {
            Text = "TTSK · Dịch họp Nhật → Việt";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.None;
            ShowIcon = false;
            ClientSize = new Size(1040, 780);
            MinimumSize = new Size(880, 620);
            Padding = new Padding(1);
            BackColor = _formBack;
            ForeColor = _textColor;
            Font = new Font("Segoe UI", 9.5F);
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);

            var frame = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            frame.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            frame.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            frame.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(frame);
            frame.Controls.Add(CreateTitleBar(), 0, 0);

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 20, 24, 16),
                Margin = Padding.Empty, ColumnCount = 1, RowCount = 6 };
            _bodyLayout = body;
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
            _liveRow = new RowStyle(SizeType.Absolute, 120);
            body.RowStyles.Add(_liveRow);
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _errorRow = new RowStyle(SizeType.Absolute, 0);
            body.RowStyles.Add(_errorRow);
            frame.Controls.Add(body, 0, 1);

            var heading = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            heading.Controls.Add(new Label { Text = "Dịch họp Nhật → Việt", Font = new Font("Segoe UI", 23F, FontStyle.Bold),
                ForeColor = _textColor, AutoSize = true, Location = new Point(0, 0) });
            _sourceDescription = new Label { Text = "Phụ đề Teams · Không thu âm · Dịch Nhật → Việt", Font = new Font("Segoe UI", 9F),
                ForeColor = _mutedTextColor, AutoSize = true, Location = new Point(2, 43) };
            heading.Controls.Add(_sourceDescription);
            _btnProvider = CreateButton(string.Empty, false, 154, 36);
            ((MeetingButton)_btnProvider).ProviderStyle = true;
            UpdateProviderButton();
            _btnProvider.AccessibleName = "Chọn dịch bằng NVIDIA, Google Gemini hoặc Google Live";
            _btnProvider.Click += delegate { ShowProviderMenu(); };
            heading.Controls.Add(_btnProvider);
            _lblLatency = new Label { Font = new Font("Segoe UI", 8.5F), ForeColor = _mutedTextColor, AutoSize = true };
            heading.Controls.Add(_lblLatency);
            heading.SizeChanged += delegate { _btnProvider.Location = new Point(heading.Width - _btnProvider.Width - 2, 4); _lblLatency.Location = new Point(heading.Width - _lblLatency.Width - 2, 40); };
            _lblLatency.TextChanged += delegate { _lblLatency.Left = heading.Width - _lblLatency.Width - 2; };
            _lblLatency.SizeChanged += delegate { _lblLatency.Left = heading.Width - _lblLatency.Width - 2; };
            body.Controls.Add(heading, 0, 0);

            var commands = new RoundPanel(12, _cardBack, _borderColor) { Dock = DockStyle.Fill, Margin = new Padding(0, 5, 0, 10), Padding = new Padding(14, 10, 14, 10) };
            var commandRows = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
            commandRows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            commandRows.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            commandRows.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            commandRows.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var sourceHeader = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            _sourceTitle = new Label { Text = "CUỘC HỌP TEAMS", Font = new Font("Segoe UI", 8F, FontStyle.Bold), ForeColor = _mutedTextColor, Dock = DockStyle.Fill, Margin = Padding.Empty };
            sourceHeader.Controls.Add(_sourceTitle);
            commandRows.Controls.Add(sourceHeader, 0, 0);
            var tools = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
            tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122));
            tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
            tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 98));
            tools.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _cboTeamsWindows = new MeetingWindowPicker(_darkMode) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 0) };
            _btnRefreshTeams = CreateButton("Tìm lại Teams", false, 112, 36);
            _btnStart = CreateButton("▶  Bắt đầu", true, 116, 36);
            _btnStop = CreateButton("■  Dừng", false, 90, 36);
            _btnStop.Enabled = false;
            _btnRefreshTeams.Click += delegate { if (_googleLiveMode) LoadLiveDevices(); else RefreshTeamsWindows(); };
            _btnStart.Click += async delegate { await StartTranslationAsync(); };
            _btnStop.Click += async delegate { await StopTranslationAsync(); };
            var sourcePicker = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 0) };
            _cboTeamsWindows.Margin = Padding.Empty;
            _cboLiveDevices = new MeetingWindowPicker(_darkMode, true) { Dock = DockStyle.Fill, Visible = false, Margin = Padding.Empty };
            _cboLiveDevices.SelectionChangeCommitted += delegate { _liveDeviceManual = true; };
            sourcePicker.Controls.Add(_cboTeamsWindows); sourcePicker.Controls.Add(_cboLiveDevices);
            tools.Controls.Add(sourcePicker, 0, 0);
            Button[] buttons = { _btnRefreshTeams, _btnStart, _btnStop };
            for (int i = 0; i < buttons.Length; i++) { buttons[i].Dock = DockStyle.Fill; buttons[i].Margin = new Padding(0, 0, i == 2 ? 0 : 10, 0); tools.Controls.Add(buttons[i], i + 1, 0); }
            commandRows.Controls.Add(tools, 0, 1);
            _lblStatus = new Label { Text = "Sẵn sàng", Font = new Font("Segoe UI", 8.5F), ForeColor = _mutedTextColor, Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.BottomLeft, Margin = Padding.Empty };
            var statusRow = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            _btnLatestTranslation = CreateButton("↓ Về mới nhất", false, 122, 20);
            _btnLatestTranslation.Dock = DockStyle.Right; _btnLatestTranslation.Visible = false;
            _btnLatestTranslation.Font = new Font("Segoe UI", 8F);
            _btnLatestTranslation.Click += delegate
            {
                _userScrolledUp = false; _pnlHistory.ScrollToBottom(); UpdateLatestTranslationButton();
            };
            statusRow.Controls.Add(_lblStatus); statusRow.Controls.Add(_btnLatestTranslation);
            commandRows.Controls.Add(statusRow, 0, 2);
            commands.Controls.Add(commandRows);
            body.Controls.Add(commands, 0, 1);

            var live = new RoundPanel(12, _cardBack, _borderColor) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 4), Padding = new Padding(16, 12, 16, 12) };
            var liveGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Margin = Padding.Empty };
            _liveGrid = liveGrid;
            liveGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
            liveGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28));
            liveGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57));
            liveGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            liveGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _sourceTextTitle = new Label { Text = "CÂU ĐANG DỊCH", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 8F, FontStyle.Bold), ForeColor = _mutedTextColor, Margin = Padding.Empty };
            liveGrid.Controls.Add(_sourceTextTitle, 0, 0);
            liveGrid.Controls.Add(new Label { Text = "BẢN DỊCH TIẾNG VIỆT", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 8F, FontStyle.Bold), ForeColor = _accentColor, Margin = Padding.Empty }, 2, 0);
            var separator = new Panel { Dock = DockStyle.Fill, BackColor = _borderColor, Margin = new Padding(13, 1, 14, 1) };
            _liveSeparator = separator;
            liveGrid.Controls.Add(separator, 1, 0); liveGrid.SetRowSpan(separator, 2);
            _lblLiveJapanese = new MeetingSelectableText { BackColor = _cardBack, Text = "Chờ phụ đề…", Dock = DockStyle.Fill, Font = new Font("Yu Gothic UI", 11.5F), ForeColor = _mutedTextColor, Margin = Padding.Empty };
            _lblLiveVietnamese = new MeetingSelectableText { BackColor = _cardBack, Text = "Bản dịch sẽ xuất hiện tại đây", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 14F, FontStyle.Bold), ForeColor = _textColor, Margin = Padding.Empty };
            liveGrid.Controls.Add(_lblLiveJapanese, 0, 1); liveGrid.Controls.Add(_lblLiveVietnamese, 2, 1);
            live.Controls.Add(liveGrid); body.Controls.Add(live, 0, 2);
            _lblLiveJapanese.TextChanged += delegate { UpdateReadingLayout(); };
            _lblLiveVietnamese.TextChanged += delegate { UpdateReadingLayout(); };

            var historyHeader = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1, Margin = Padding.Empty, Padding = new Padding(0, 10, 0, 8) };
            historyHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            historyHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
            historyHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 134));
            historyHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124));
            historyHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            historyHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142));
            historyHeader.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _historyTitle = new Label { Text = "Bản dịch trong phiên", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 11.5F, FontStyle.Bold), ForeColor = _textColor, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty };
            historyHeader.Controls.Add(_historyTitle, 0, 0);
            _chkShowJapanese = new CheckBox { Text = "Hiện câu Nhật", Checked = true, Dock = DockStyle.Fill, ForeColor = _mutedTextColor, Font = new Font("Segoe UI", 9F), Margin = Padding.Empty };
            _chkShowJapanese.CheckedChanged += delegate
            {
                _showJapanese = _chkShowJapanese.Checked;
                if (_googleLiveMode) _liveShowJapanese = _showJapanese; else _captionShowJapanese = _showJapanese;
                UpdateJapaneseVisibility();
            };
            historyHeader.Controls.Add(_chkShowJapanese, 1, 0);
            _btnSubtitleOverlay = CreateButton("Cửa sổ phụ đề", false, 124, 34);
            _btnClearSession = CreateButton("Xóa phiên", false, 90, 34);
            _btnRetryPending = CreateButton("Chọn model API", false, 132, 34);
            _btnRetryPending.Dock = DockStyle.Fill; _btnRetryPending.Margin = new Padding(10, 0, 0, 0);
            _btnRetryPending.AccessibleName = "Chọn model API hoặc đổi key trong phiên dịch";
            _btnRetryPending.Click += async delegate { await ChooseMeetingApiAsync(_provider); };
            historyHeader.Controls.Add(_btnRetryPending, 5, 0);
            _btnExportText = CreateButton("Xuất file text", false, 114, 34);
            _btnExportText.Dock = DockStyle.Fill; _btnExportText.Margin = new Padding(0, 0, 10, 0);
            _btnExportText.Click += delegate { ExportSessionText(); };
            historyHeader.Controls.Add(_btnExportText, 3, 0);
            _btnSubtitleOverlay.Dock = _btnClearSession.Dock = DockStyle.Fill;
            _btnSubtitleOverlay.Margin = new Padding(0, 0, 10, 0); _btnClearSession.Margin = Padding.Empty;
            _btnSubtitleOverlay.Click += delegate { ToggleSubtitleOverlay(); };
            _btnClearSession.Click += delegate { ClearSession(); };
            historyHeader.Controls.Add(_btnSubtitleOverlay, 2, 0); historyHeader.Controls.Add(_btnClearSession, 4, 0);
            body.Controls.Add(historyHeader, 0, 3);

            _pnlHistory = new MeetingHistoryPanel(_darkMode) { Dock = DockStyle.Fill, BackColor = _formBack, Margin = Padding.Empty };
            _pnlHistory.UserScrolled += delegate { CheckUserScroll(); };
            _pnlHistory.SizeChanged += delegate { RelayoutHistoryCards(); };
            body.Controls.Add(_pnlHistory, 0, 4);
            _pnlErrorBar = new Panel { Dock = DockStyle.Fill, BackColor = _darkMode ? Color.FromArgb(57, 31, 32) : Color.FromArgb(254, 239, 238), Padding = new Padding(12, 6, 12, 6), Margin = new Padding(0, 6, 0, 0), Visible = false };
            _lblError = new Label { Dock = DockStyle.Fill, ForeColor = _dangerColor, Font = new Font("Segoe UI", 9F), AutoEllipsis = true };
            _pnlErrorBar.Controls.Add(_lblError); body.Controls.Add(_pnlErrorBar, 0, 5);
            _audioTimer = new System.Windows.Forms.Timer { Interval = 100 };
            _audioTimer.Tick += delegate { DrainLiveText(); _audioRows?.Tick(DateTimeOffset.UtcNow); };
        }

        private void SetGoogleLiveMode(bool enabled)
        {
            _googleLiveMode = enabled;
            _cboTeamsWindows.Visible = !enabled; _cboLiveDevices.Visible = enabled;
            _sourceTitle.Text = enabled ? "LOA / TAI NGHE" : "CUỘC HỌP TEAMS";
            _sourceTextTitle.Text = enabled ? "NGUỒN TIẾNG NHẬT MỚI NHẤT" : "CÂU ĐANG DỊCH";
            _sourceDescription.Text = enabled ? "Âm thanh loa/tai nghe → tiếng Việt · Google Live"
                : "Phụ đề Teams · Không thu âm · Dịch Nhật → Việt";
            _btnRefreshTeams.Text = enabled ? "Tìm thiết bị" : "Tìm lại Teams";
            _receivingSequence = 0; _receivingJapanese = null; _liveHasTranslation = false;
            _latestSessionId = Guid.Empty; _latestGeneration = 0; _latestRevision = 0;
            _latestTranslatedJapanese = ""; _lblLatency.Text = "";
            _lblLiveJapanese.Text = enabled ? "Chờ âm thanh tiếng Nhật…" : "Chờ phụ đề Teams…";
            _lblLiveVietnamese.Text = "Bản dịch sẽ xuất hiện tại đây";
            _chkShowJapanese.Text = enabled ? "Xem tiếng Nhật" : "Hiện câu Nhật";
            _latencyTip.SetToolTip(_chkShowJapanese, enabled ? "Hiện tiếng Nhật nhỏ bên dưới bản dịch trong cùng thẻ, và ô nguồn ở phía trên." : "Hiện câu Nhật trong lịch sử dịch.");
            _chkShowJapanese.Checked = enabled ? _liveShowJapanese : _captionShowJapanese;
            _historyTitle.Text = enabled ? "Bản dịch tiếng Việt" : "Bản dịch trong phiên";
            UpdateJapaneseVisibility(); UpdateLatestTranslationButton();
            ClearError(); UpdateProviderButton(); SetLiveControls();
            UpdateStatus(enabled ? "Chọn đầu ra đang phát tiếng. Bắt đầu sẽ gửi âm thanh từ đầu ra này tới Google. Không lưu file âm thanh."
                : "Đã trở về phụ đề Teams. Bấm Bắt đầu để tiếp tục; lịch sử và hàng đợi được giữ nguyên.");
        }

        private void SetLiveControls()
        {
            if (_closing || IsDisposed) return;
            _btnStart.Enabled = !_isRunning && !_isBusy;
            _btnStart.Text = !_googleLiveMode && _coordinator?.IsPaused == true ? "▶ Tiếp tục" : "▶ Bắt đầu";
            _btnStop.Enabled = _isRunning && !_isBusy;
            _btnProvider.Enabled = _btnRetryPending.Enabled = !_isBusy;
            _btnRefreshTeams.Enabled = _cboTeamsWindows.Enabled = _cboLiveDevices.Enabled = !_isRunning && !_isBusy;
        }

        private string LiveKey()
        {
            if (_liveKey != null) return _liveKey;
            string own = NvidiaApiKeyStore.LoadTarget(GoogleLiveTranscriptRows.CredentialTarget);
            if (!string.IsNullOrWhiteSpace(own)) return own;
            string google = (_selectedGoogle ?? GoogleApiKeyStore.Load()).ApiKey;
            if (!string.IsNullOrWhiteSpace(google)) return google;
            return MeetingApiKeyRing.Load().FirstOrDefault(p => p.Provider == MeetingTranslationProvider.Google)?.ApiKey ?? "";
        }

        private async Task ChooseLiveApiAsync()
        {
            try
            {
                using (var picker = new MeetingApiPickerForm(MeetingTranslationProvider.Google,
                    new GoogleMeetingSettings { ApiKey = LiveKey(), Model = GoogleLiveTranslationSession.Model },
                    _selectedNvidia ?? NvidiaMeetingSettings.FromSecureStore(), _darkMode, true))
                {
                    if (picker.ShowDialog(this) != DialogResult.OK || _closing) return;
                    bool restart = _isRunning;
                    await StopGoogleLiveAsync();
                    if (_closing) return;
                    _liveKey = picker.Google.ApiKey;
                    try { NvidiaApiKeyStore.SaveTarget(GoogleLiveTranscriptRows.CredentialTarget, _liveKey); }
                    catch { ShowError("Key Live đã dùng cho phiên này; chưa lưu được trên máy."); }
                    if (restart) await StartGoogleLiveAsync();
                }
            }
            catch { if (!_closing) ShowError("Chưa đọc được kho key. Hãy thử mở lại Chọn model API."); }
        }

        private bool LoadLiveDevices()
        {
            try
            {
                string previous = _cboLiveDevices.SelectedAudioDevice?.Id;
                var devices = WindowsLoopback.Devices();
                _cboLiveDevices.AudioDevices.Clear();
                _cboLiveDevices.AudioDevices.AddRange(devices);
                var selected = _liveDeviceManual ? devices.FirstOrDefault(d => d.Id == previous) : WindowsLoopback.RecommendDevice(devices);
                _cboLiveDevices.SelectedIndex = _cboLiveDevices.AudioDevices.IndexOf(selected);
                if (selected == null) { ShowError("Không tìm thấy đầu ra đã chọn. Kết nối hoặc chọn lại loa/tai nghe."); return false; }
                ClearError();
                UpdateStatus("Chọn loa/tai nghe đang phát cuộc họp, rồi bấm Bắt đầu.");
                return true;
            }
            catch (Exception ex) { ShowError("Không đọc được thiết bị âm thanh: " + ex.GetBaseException().Message); return false; }
        }

        private void CreateLiveRows()
        {
            _audioRows = new GoogleLiveTranscriptRows();
            _audioRows.Changed += line =>
            {
                if (_closing || !_googleLiveMode) return;
                if (!string.IsNullOrEmpty(line.Japanese))
                {
                    _lastLiveSource = line.Japanese;
                    if (_showJapanese) _lblLiveJapanese.Text = line.Japanese;
                }
                if (!GoogleLiveTranscriptRows.IsSource(line))
                {
                    _lblLiveVietnamese.Text = line.Vietnamese; _liveHasTranslation = true;
                    // Google không cung cấp mã ghép câu; ô phụ đề không ghép tùy tiện nguồn đang nhận.
                    _latestTranslatedJapanese = "";
                    if (_subtitleOverlay != null && !_subtitleOverlay.IsDisposed)
                        _subtitleOverlay.UpdateSubtitle(line.Vietnamese, "", "Google Live");
                }
                UpsertHistoryCard(line);
            };
        }

        private void DrainLiveText()
        {
            List<Tuple<string, bool>> pending;
            lock (_audioGate) { pending = _audioText.ToList(); _audioText.Clear(); _audioTextLength = 0; }
            if (_closing || !_googleLiveMode || _audioRows == null) return;
            var now = DateTimeOffset.UtcNow;
            foreach (var text in pending) _audioRows.Append(text.Item1, text.Item2, now);
        }

        private async Task StartGoogleLiveAsync()
        {
            if (_audioRun != null || _isBusy || _closing || !LoadLiveDevices()) return;
            string key;
            try { key = LiveKey(); }
            catch { ShowError("Chưa đọc được Google key. Bấm Chọn model API để kiểm tra."); return; }
            if (string.IsNullOrWhiteSpace(key)) { ShowError("Bấm Chọn model API để nhập Google key cho Live."); return; }
            var device = _cboLiveDevices.SelectedAudioDevice;
            CreateLiveRows();
            _audioCancellation = new CancellationTokenSource();
            long epoch = ++_audioEpoch;
            _isRunning = true; SetLiveControls(); ClearError();
            _lastSound = DateTimeOffset.UtcNow; _audioStatus = "Đang kết nối Google Live…";
            UpdateStatus(_audioStatus); _lblLatency.Text = "Chờ tiếng…";
            var session = new GoogleLiveTranslationSession();
            session.Status += text => SafeInvoke(() => { if (epoch == _audioEpoch) { _audioStatus = text; UpdateStatus(text); } });
            session.Level += level => SafeInvoke(() =>
            {
                if (epoch != _audioEpoch) return;
                if (level > .003F) _lastSound = DateTimeOffset.UtcNow;
                double quiet = (DateTimeOffset.UtcNow - _lastSound).TotalSeconds;
                _lblLatency.Text = level > .003F ? "Có tiếng · " + (level * 100).ToString("0.0") + "%" : "Chờ tiếng " + quiet.ToString("0") + "s";
                UpdateStatus(quiet >= 8 ? "Chưa có tiếng từ đầu ra đã chọn. Dừng để chọn lại loa/tai nghe." : _audioStatus);
            });
            session.Transcript += (text, vi) =>
            {
                lock (_audioGate)
                {
                    if (epoch != _audioEpoch) return;
                    if (_audioTextLength + text.Length > 100000) throw new InvalidOperationException("Giao diện không theo kịp phụ đề. Đã dừng để giữ chữ hiện có.");
                    _audioText.Enqueue(Tuple.Create(text, vi)); _audioTextLength += text.Length;
                }
            };
            _audioTimer.Start();
            // Giữ gợi ý nguồn Nhật mặc định của luồng cũ; không cần công tắc trên giao diện.
            _audioRun = RunGoogleLiveAsync(session, key, device.Id, _audioCancellation.Token, true);
            await _audioRun;
        }

        private async Task RunGoogleLiveAsync(GoogleLiveTranslationSession session, string key, string device, CancellationToken token, bool japanese)
        {
            await Task.Yield(); // Gán task vòng đời trước khi có lỗi đồng bộ hoặc yêu cầu Dừng.
            try { await session.RunAsync(key, device, token, japanese); }
            catch (Exception ex) { if (!token.IsCancellationRequested && !_closing) ShowError(GoogleLiveTranslationSession.SafeError(ex, key)); }
            finally
            {
                DrainLiveText(); if (!_closing) _audioRows?.Finish(DateTimeOffset.UtcNow);
                EndLiveRun();
            }
        }

        private void EndLiveRun()
        {
            lock (_audioGate) { ++_audioEpoch; _audioText.Clear(); _audioTextLength = 0; }
            _audioTimer.Stop(); _audioCancellation.Dispose(); _audioCancellation = null; _audioRun = null;
            _isRunning = false;
            if (!_closing)
            {
                _lblLatency.Text = "Đã dừng"; SetLiveControls();
                UpdateStatus("Đã dừng thu và gửi âm thanh. Lịch sử được giữ nguyên; Bắt đầu sẽ mở phiên Live mới.");
            }
        }

        private async Task StopGoogleLiveAsync()
        {
            var run = _audioRun; if (run == null) return;
            _isBusy = true; SetLiveControls();
            _audioCancellation?.Cancel();
            try { await run; }
            finally { _isBusy = false; SetLiveControls(); }
        }

        private void CheckUserScroll()
        {
            _userScrolledUp = !_pnlHistory.IsAtBottom;
            UpdateLatestTranslationButton();
        }

        private void UpdateLatestTranslationButton()
        {
            if (_btnLatestTranslation != null) _btnLatestTranslation.Visible = _googleLiveMode && _userScrolledUp && !_subtitleReadingMode;
        }

        private void RefreshTeamsWindows()
        {
            try
            {
                IntPtr previousHandle = IntPtr.Zero;
                if (_cboTeamsWindows.SelectedItem is TeamsWindow prev)
                {
                    previousHandle = prev.Handle;
                }

                _cboTeamsWindows.Items.Clear();
                var list = TeamsDesktopCaptionReader.GetWindows();
                int selectIndex = -1;

                for (int i = 0; i < list.Count; i++)
                {
                    _cboTeamsWindows.Items.Add(list[i]);
                    if (list[i].Handle == previousHandle)
                        selectIndex = i;
                }

                if (selectIndex >= 0)
                    _cboTeamsWindows.SelectedIndex = selectIndex;
                else if (_cboTeamsWindows.Items.Count > 0)
                    _cboTeamsWindows.SelectedIndex = 0;

                if (list.Count == 0)
                {
                    UpdateStatus("Không tìm thấy cửa sổ Teams. Hãy mở Teams, bật Live Captions và nhấn 'Tìm lại Teams'.");
                }
                else
                {
                    UpdateStatus("Đã tìm thấy " + list.Count + " cửa sổ Teams. Chọn cửa sổ và nhấn Bắt đầu.");
                }
            }
            catch (Exception ex)
            {
                ShowError("Lỗi quét cửa sổ Teams: " + ex.Message);
            }
        }

        private void ShowProviderMenu()
        {
            if (_isBusy || _closing || IsDisposed || Disposing) return;
            // Menu thuộc vòng đời form: Closed vẫn nằm trong xử lý click của WinForms.
            // Không Dispose ở Closed, nếu không HandleClick tiếp tục dùng menu đã hủy.
            if (_providerMenu == null)
            {
                _providerMenu = CreateProviderMenu();
                _providerMenu.Closed += delegate
                {
                    if (_btnProvider.IsDisposed) return;
                    ((MeetingButton)_btnProvider).MenuExpanded = false;
                    _btnProvider.Invalidate();
                };
            }
            if (_providerMenu.Visible) { _providerMenu.Close(); return; }
            // Bề rộng pixel theo nút đã scale DPI; giữ hai mép popup thẳng với nút.
            _providerMenu.MinimumSize = new Size(_btnProvider.Width, 0);
            _providerMenu.MaximumSize = new Size(_btnProvider.Width, 0);
            foreach (ToolStripMenuItem item in _providerMenu.Items)
                item.Checked = _googleLiveMode ? Equals(item.Tag, "GoogleLive") : item.Tag is MeetingTranslationProvider && (MeetingTranslationProvider)item.Tag == _provider;
            _providerMenu.Show(_btnProvider, new Point(_btnProvider.Width, _btnProvider.Height + 5), ToolStripDropDownDirection.BelowLeft);
            ((MeetingButton)_btnProvider).MenuExpanded = _providerMenu.Visible;
            _btnProvider.Invalidate();
        }

        private void UpdateProviderButton()
        {
            bool google = _googleLiveMode || _provider == MeetingTranslationProvider.Google;
            _btnProvider.Text = _googleLiveMode ? "GOOGLE LIVE" : google ? "GOOGLE AI" : "NVIDIA";
            _btnProvider.BackColor = _darkMode
                ? (google ? Color.FromArgb(27, 43, 59) : Color.FromArgb(27, 46, 39))
                : (google ? Color.FromArgb(233, 243, 255) : Color.FromArgb(234, 246, 239));
            _btnProvider.ForeColor = _darkMode
                ? (google ? Color.FromArgb(133, 195, 252) : Color.FromArgb(134, 209, 166))
                : (google ? Color.FromArgb(34, 103, 173) : Color.FromArgb(39, 113, 76));
            _btnProvider.FlatAppearance.BorderColor = _darkMode
                ? (google ? Color.FromArgb(48, 76, 99) : Color.FromArgb(46, 78, 61))
                : (google ? Color.FromArgb(191, 215, 240) : Color.FromArgb(192, 220, 202));
            _btnProvider.Invalidate();
            var saved = GoogleSettingsForSession();
            _latencyTip.SetToolTip(_btnProvider, _googleLiveMode ? "Google Live · âm thanh loa/tai nghe → tiếng Việt" : google ? "Google AI Studio · " + (saved.IsReady ? saved.Model : "Chưa kiểm tra key/model") : "NVIDIA · Riva; tự dùng model NVIDIA dự phòng khi Riva chậm");
        }

        private ToolStripDropDownMenu CreateProviderMenu()
        {
            var menu = new MeetingWindowPicker.MeetingDropDown(_btnProvider.FlatAppearance.BorderColor, _darkMode) { BackColor = _cardBack, ForeColor = _textColor,
                ShowImageMargin = false, ShowCheckMargin = false, Font = Font, Padding = new Padding(5),
                Renderer = new MeetingProviderMenuRenderer(_darkMode) };
            menu.LayoutCompleted += delegate
            {
                // WinForms có thể đo hàng rộng hơn popup; căn lại để dấu chọn không bị cắt.
                foreach (ToolStripItem row in menu.Items)
                {
                    int width = Math.Max(1, menu.ClientSize.Width - row.Bounds.Left - menu.Padding.Right);
                    if (row.Width != width) row.Size = new Size(width, row.Height);
                }
            };
            foreach (var provider in new[] { MeetingTranslationProvider.Nvidia, MeetingTranslationProvider.Google })
            {
                var selected = provider;
                var item = new ToolStripMenuItem(provider == MeetingTranslationProvider.Google ? "Google Gemini" : "NVIDIA")
                { Checked = _provider == provider, Tag = provider, BackColor = _cardBack, ForeColor = _textColor,
                    AutoSize = false, Size = new Size(_btnProvider.Width - menu.Padding.Horizontal, (int)(36 * DeviceDpi / 96F)) };
                item.Click += async delegate
                {
                    await QuickSwitchProviderAsync(selected);
                };
                menu.Items.Add(item);
            }
            var live = new ToolStripMenuItem("Google Live") { Tag = "GoogleLive", BackColor = _cardBack, ForeColor = _textColor,
                AutoSize = false, Size = new Size(_btnProvider.Width - menu.Padding.Horizontal, (int)(36 * DeviceDpi / 96F)) };
            live.Click += async delegate
            {
                if (_isBusy || _closing || _googleLiveMode) return;
                if (_isRunning) await StopTranslationAsync();
                if (_closing) return;
                SetGoogleLiveMode(true); LoadLiveDevices();
            };
            menu.Items.Add(live);
            return menu;
        }

        private async Task ChooseMeetingApiAsync(MeetingTranslationProvider initialProvider)
        {
            if (_isBusy || _closing) return;
            if (_googleLiveMode) { await ChooseLiveApiAsync(); return; }
            using (var picker = new MeetingApiPickerForm(initialProvider, _selectedGoogle ?? GoogleMeetingSettings.FromSecureStore(),
                _selectedNvidia ?? NvidiaMeetingSettings.FromSecureStore(), _darkMode))
            {
                var result = picker.ShowDialog(this);
                if (picker.KeyRingChanged) RefreshFallbackKeys();
                if (result != DialogResult.OK || _closing) return;
                await ApplyMeetingApiAsync(picker.Google, picker.Nvidia, true);
            }
        }

        private async Task QuickSwitchProviderAsync(MeetingTranslationProvider provider)
        {
            if (_isBusy || _closing || (!_googleLiveMode && provider == _provider)) return;
            if (_googleLiveMode)
            {
                await StopGoogleLiveAsync();
                if (_closing) return;
                SetGoogleLiveMode(false);
            }
            // Chọn nhanh chỉ đổi phiên hiện tại, dùng cấu hình đã có; không mở hộp quản lý key.
            var google = provider == MeetingTranslationProvider.Google ? GoogleSettingsForSession() : null;
            var nvidia = provider == MeetingTranslationProvider.Nvidia ? (_selectedNvidia ?? NvidiaMeetingSettings.FromSecureStore()) : null;
            if (!(google?.IsReady ?? nvidia?.IsReady ?? false))
            {
                ShowError("Chưa có cấu hình " + (provider == MeetingTranslationProvider.Google ? "Google Gemini" : "NVIDIA")
                    + ". Bấm Chọn model API để lưu key/model trước khi chuyển nhanh.");
                return;
            }
            await ApplyMeetingApiAsync(google, nvidia, false);
        }

        private GoogleMeetingSettings GoogleSettingsForSession()
        {
            var saved = _selectedGoogle ?? GoogleMeetingSettings.FromSecureStore();
            // Đang chạy giữ model thật của worker; phiên mới/chọn nhanh dùng ưu tiên chung.
            if (_explicitGoogleModel || (_coordinator != null && _provider == MeetingTranslationProvider.Google) || !saved.IsReady) return saved;
            var models = GoogleJapaneseVietnameseTranslator.GetSavedVerifiedModels(saved.ApiKey).ToList();
            try
            {
                var profile = MeetingApiKeyRing.Load().FirstOrDefault(p => p.Provider == MeetingTranslationProvider.Google && p.ApiKey == saved.ApiKey);
                if (profile?.Models != null) models.AddRange(profile.Models);
            }
            catch (System.IO.IOException) { } // Kho tạm bận: vẫn dùng cache/model đã xác minh.
            return GoogleMeetingSettings.PreferVerifiedModel(saved, models);
        }

        private async Task ApplyMeetingApiAsync(GoogleMeetingSettings google, NvidiaMeetingSettings nvidia, bool saveSettings)
        {
            if (_isBusy || _closing) return;
            _isBusy = true;
            _btnProvider.Enabled = _btnRetryPending.Enabled = _btnStart.Enabled = _btnStop.Enabled = false;
            UpdateStatus("Đang chuyển API · giữ nguyên các câu trong phiên…");
            try
            {
                if (_coordinator != null) await _coordinator.SwitchApiAsync(google, nvidia);
                if (_closing) return;
                _provider = google != null ? MeetingTranslationProvider.Google : MeetingTranslationProvider.Nvidia;
                if (google != null) { _selectedGoogle = google; if (saveSettings) _explicitGoogleModel = true; }
                else _selectedNvidia = nvidia;
                UpdateProviderButton(); ClearError();
                _translationErrorVisible = false; _translationErrorText = null;
                UpdateStatus((_coordinator?.IsPaused == true ? "Đã đổi API · bấm Tiếp tục. " : "Đã chọn API · ") +
                    (google?.Model ?? "NVIDIA Riva") + " · các câu chưa dịch được giữ đúng thứ tự.");
                if (!saveSettings) return;
                try
                {
                    if (google != null) GoogleApiKeyStore.Save(google);
                    else NvidiaApiKeyStore.Save(nvidia.ApiKey);
                    MeetingProviderSettings.Save(_provider);
                    RefreshFallbackKeys();
                }
                catch { ShowError("API mới đã dùng cho phiên này; chưa lưu được cấu hình trên máy."); }
            }
            catch (Exception) { if (!_closing) ShowError("Chưa chuyển được API. Các câu trong phiên vẫn được giữ; hãy chọn lại."); }
            finally
            {
                _isBusy = false;
                if (!_closing && !IsDisposed)
                {
                    _btnProvider.Enabled = _btnRetryPending.Enabled = true;
                    _btnStart.Enabled = !_isRunning; _btnStop.Enabled = _isRunning;
                }
            }
        }

        private void RefreshFallbackKeys()
        {
            if (_coordinator == null || _closing) return;
            try
            {
                string googlePrimary = GoogleApiKeyStore.Load().ApiKey, nvidiaPrimary = NvidiaApiKeyStore.Load();
                _coordinator.ConfigureAutomaticKeyFallback(MeetingApiKeyRing.Load().OrderBy(p =>
                    p.ApiKey == (p.Provider == MeetingTranslationProvider.Google ? googlePrimary : nvidiaPrimary) ? 0 : 1));
            }
            catch (System.IO.IOException) { ShowError("Chưa đọc được kho key dự phòng. Phiên vẫn dùng key hiện tại."); }
        }

        private async Task StartTranslationAsync()
        {
            if (_isRunning || _isBusy) return;
            if (_googleLiveMode) { await StartGoogleLiveAsync(); return; }
            if (_coordinator != null && _coordinator.IsPaused)
            {
                var selectedWindow = _cboTeamsWindows.SelectedItem as TeamsWindow;
                if (selectedWindow == null) { ShowError("Chọn cửa sổ Teams để tiếp tục."); return; }
                _coordinator.Resume(selectedWindow.Handle);
                _lblLiveJapanese.Text = _receivingJapanese ?? "Chờ phụ đề…";
                _isRunning = true; _btnStart.Enabled = false; _btnStart.Text = "▶ Bắt đầu";
                _btnStop.Enabled = true; _btnProvider.Enabled = true;
                _btnRefreshTeams.Enabled = false; _cboTeamsWindows.Enabled = false;
                ClearError();
                return;
            }

            NvidiaMeetingSettings settings = _provider == MeetingTranslationProvider.Nvidia ? (_selectedNvidia ?? NvidiaMeetingSettings.FromSecureStore()) : null;
            GoogleMeetingSettings googleSettings = _provider == MeetingTranslationProvider.Google ? GoogleSettingsForSession() : null;
            if (!(settings?.IsReady ?? googleSettings?.IsReady ?? false))
            {
                ShowError("Chưa có " + (_provider == MeetingTranslationProvider.Google ? "Google Gemini" : "NVIDIA") + " API Key. Bấm Chọn model API để nhập key.");
                return;
            }

            if (_cboTeamsWindows.SelectedItem == null)
            {
                ShowError("Chưa chọn cửa sổ cuộc họp Teams. Vui lòng mở Teams và chọn cửa sổ.");
                return;
            }

            TeamsWindow targetWindow = (TeamsWindow)_cboTeamsWindows.SelectedItem;
            _isBusy = true;
            _btnProvider.Enabled = false;
            _btnStart.Enabled = false;
            _btnRefreshTeams.Enabled = false;
            _cboTeamsWindows.Enabled = false;
            ClearError();
            UpdateStatus("Đang khởi tạo bộ đọc phụ đề Teams...");

            try
            {
                long epoch = Interlocked.Increment(ref _teamsEpoch);
                _coordinator = _provider == MeetingTranslationProvider.Google ? new TeamsDesktopCaptionCoordinator(googleSettings) : new TeamsDesktopCaptionCoordinator(settings);
                if (googleSettings != null) { _selectedGoogle = googleSettings; UpdateProviderButton(); }
                RefreshFallbackKeys();
                _coordinator.AutomaticKeyChanged += selected => SafeInvoke(() =>
                {
                    if (epoch != _teamsEpoch) return;
                    _provider = selected.Provider;
                    if (selected.Provider == MeetingTranslationProvider.Google) { _selectedGoogle = new GoogleMeetingSettings { ApiKey = selected.ApiKey, Model = selected.Model }; _explicitGoogleModel = false; }
                    else _selectedNvidia = new NvidiaMeetingSettings { ApiKey = selected.ApiKey, TranslationPreferences = _selectedNvidia?.TranslationPreferences ?? TranslationPreferenceStore.Load() };
                    if (_googleLiveMode) return;
                    UpdateProviderButton(); ClearError();
                    _translationErrorVisible = false; _translationErrorText = null;
                    UpdateStatus("Đã tự chuyển sang " + selected.Name + " · " + (selected.Model ?? "Riva") + " · giữ nguyên bản dịch cũ.");
                });

                _coordinator.StateChanged += state =>
                {
                    SafeInvoke(() => { if (epoch == _teamsEpoch && !_googleLiveMode) UpdateStatus(state); });
                };

                _coordinator.ErrorOccurred += err =>
                {
                    SafeInvoke(() => { if (epoch == _teamsEpoch && !_googleLiveMode) { _translationErrorVisible = true; _translationErrorText = err; ShowError(err); } });
                };

                _coordinator.CaptionProcessing += (sequence, text) =>
                {
                    SafeInvoke(() =>
                    {
                        if (epoch != _teamsEpoch || _googleLiveMode) return;
                        ShowReceivingCaption(sequence, text);
                    });
                };

                _coordinator.TranslationReady += line =>
                {
                    SafeInvoke(() => { if (epoch == _teamsEpoch) { if (_googleLiveMode) UpsertHistoryCard(line); else ProcessTranslationLine(line); } });
                };

                _coordinator.TranslationDraftReady += line =>
                {
                    SafeInvoke(() => { if (epoch == _teamsEpoch) { if (_googleLiveMode) UpsertHistoryCard(line); else ProcessTranslationLine(line); } });
                };

                _coordinator.TranslationFailed += line =>
                {
                    SafeInvoke(() => { if (epoch == _teamsEpoch) { if (_googleLiveMode) UpsertHistoryCard(line); else ProcessTranslationLine(line); } });
                };
                _coordinator.PendingCountChanged += count => SafeInvoke(() =>
                {
                    if (epoch != _teamsEpoch) return;
                    _btnRetryPending.Enabled = !_isBusy && !_closing;
                });

                _coordinator.Start(targetWindow.Handle);
                _isRunning = true;
                _btnStop.Enabled = true;
                UpdateStatus("Đang tìm vùng phụ đề Teams • giữ đủ các câu đang hiển thị...");
            }
            catch (Exception ex)
            {
                ShowError("Không thể bắt đầu: " + ex.Message);
                _isBusy = false;
                await StopTranslationAsync();
            }
            finally
            {
                _isBusy = false;
                if (!_closing) _btnProvider.Enabled = _btnRetryPending.Enabled = true;
            }
        }

        private async Task StopTranslationAsync()
        {
            if (_isBusy) return;
            if (_googleLiveMode) { await StopGoogleLiveAsync(); return; }
            _isBusy = true;
            try
            {
                if (_coordinator != null)
                {
                    _coordinator.Pause();
                }
                await Task.CompletedTask;

                if (_closing || IsDisposed) return;
                _isRunning = false;
                _btnStart.Enabled = true;
                _btnStart.Text = "▶ Tiếp tục";
                _btnStop.Enabled = false;
                _btnRefreshTeams.Enabled = true;
                _cboTeamsWindows.Enabled = true;
                _btnProvider.Enabled = true;
                _btnRetryPending.Enabled = true;
                _lblLiveJapanese.Text = "Đã dừng.";
                if (!_liveHasTranslation) _lblLiveVietnamese.Text = "Đã tạm dừng · bấm Tiếp tục để dịch các câu còn lại.";
                _lblLatency.Text = "Tạm dừng";
                UpdateStatus("Đã tạm dừng nhận câu mới. Hoàn tất câu đang dịch; các câu còn lại được giữ để Tiếp tục.");
            }
            catch (Exception ex)
            {
                ShowError("Lỗi khi dừng: " + ex.Message);
            }
            finally
            {
                _isBusy = false;
            }
        }

        private void ShowReceivingCaption(long sequence, string text)
        {
            _receivingSequence = sequence;
            _receivingJapanese = text;
            _liveHasTranslation = false;
            _lblLiveJapanese.Text = string.IsNullOrEmpty(text) ? "Chờ phụ đề..." : text;
            _lblLiveVietnamese.Text = string.IsNullOrEmpty(text) ? "Chờ câu nói tiếp theo..." : "Đang dịch câu này…";
            _latestTranslatedJapanese = string.Empty;
            _lblLatency.Text = "";
            if (_subtitleOverlay != null && !_subtitleOverlay.IsDisposed)
                _subtitleOverlay.UpdateSubtitle(_lblLiveVietnamese.Text, text, "");
        }

        private void ProcessTranslationLine(MeetingTranslationLine line)
        {
            if (line == null) return;
            if (line.CompletedTicks > 0)
            {
                double display = (Stopwatch.GetTimestamp() - line.CompletedTicks) * 1000.0 / Stopwatch.Frequency;
                line.TotalMilliseconds += display - line.DisplayMilliseconds;
                line.DisplayMilliseconds = display;
            }
            if (line.SessionId == _latestSessionId && line.Sequence == _latestGeneration && line.Revision < _latestRevision) return;
            if (line.TranslationSucceeded && _translationErrorVisible)
            {
                if (_lblError.Text == _translationErrorText) ClearError();
                _translationErrorVisible = false;
            }

            // Update live display
            if ((_receivingJapanese != null && (line.Sequence != _receivingSequence || line.Japanese != _receivingJapanese)) ||
                (line.SessionId == _latestSessionId && line.Sequence < _latestGeneration))
            {
                // Older revision correction, update card only
            }
            else
            {
                bool preserveDraft = _liveHasTranslation && !line.TranslationSucceeded &&
                    line.SessionId == _latestSessionId && line.Sequence == _latestGeneration && line.Revision == _latestRevision;
                _latestSessionId = line.SessionId;
                _latestGeneration = line.Sequence;
                _latestRevision = line.Revision;
                // Ô đang nhận chỉ do bộ đọc cập nhật; kết quả dịch chậm không kéo chữ lùi lại.
                _latestTranslatedJapanese = line.Japanese;
                if (!preserveDraft) _lblLiveVietnamese.Text = line.Vietnamese;
                _liveHasTranslation = line.TranslationSucceeded || preserveDraft;
                _lblLatency.Text = line.DisplayTiming;
                _latencyTip.SetToolTip(_lblLatency, line.TimingSummary);

                if (_subtitleOverlay != null && !_subtitleOverlay.IsDisposed)
                {
                    _subtitleOverlay.UpdateSubtitle(_lblLiveVietnamese.Text, line.Japanese, _lblLatency.Text);
                }
            }

            UpsertHistoryCard(line);
        }

        private void UpsertHistoryCard(MeetingTranslationLine line)
        {
            RememberExportLine(line);
            MeetingHistoryRow target = null;
            if (line.Sequence > 0) _historyRowsBySequence.TryGetValue(Tuple.Create(line.SessionId, line.Sequence), out target);
            if (target != null) target.Update(line);
            else
            {
                var row = new MeetingHistoryRow(line, GetSpeakerColorPair(line.SpeakerName));
                int insertion = _historyRows.FindIndex(item => item.Line.SessionId == line.SessionId && item.Line.Sequence > line.Sequence);
                if (insertion < 0) _historyRows.Add(row); else _historyRows.Insert(insertion, row);
                if (line.Sequence > 0) _historyRowsBySequence[Tuple.Create(line.SessionId, line.Sequence)] = row;
            }
            // Chờ chữ Việt trước khi hiện thẻ Live; nguồn tới trước vẫn giữ cho thẻ/xuất file.
            if (!GoogleLiveTranscriptRows.IsSource(line)) RelayoutHistoryCards();
        }

        private void RelayoutHistoryCards()
        {
            if (_pnlHistory == null || _layingOutHistory || _updatingReadingLayout || IsDisposed) return;
            _layingOutHistory = true;
            try { _pnlHistory.SetHistory(_historyRows, _darkMode, _subtitleReadingMode, _showJapanese && !_subtitleReadingMode, _readingScale, !_userScrolledUp); }
            finally { _layingOutHistory = false; }
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateReadingLayout();
            RelayoutHistoryCards();
        }

        private void UpdateReadingLayout()
        {
            if (_updatingReadingLayout || _historyTitle == null || IsDisposed) return;
            _updatingReadingLayout = true;
            try
            {
                // Tăng theo diện tích đọc thực (đã trừ DPI), chặn ở 180%; không nhân dồn khi resize.
                float dpi = DeviceDpi / 96F;
                float available = Math.Min(ClientSize.Width / (1040F * dpi), ClientSize.Height / (780F * dpi) + 0.25F);
                _readingScale = (float)Math.Round(Math.Max(1, Math.Min(1.8F, available)) * 20) / 20F;
                if (_subtitleReadingMode)
                {
                    // Cỡ chữ phụ đề 14,4–19,2 pt theo diện tích logic; DPI do font tự áp dụng một lần.
                    // Cửa sổ 1920×1080 ở 100% dùng khoảng 18 pt, không phóng vô hạn trên màn hình lớn.
                    float readingArea = Math.Min(ClientSize.Width / (1100F * dpi), ClientSize.Height / (720F * dpi));
                    _readingScale = (float)Math.Round(Math.Max(1.2F, Math.Min(1.6F, readingArea)) * 20) / 20F;
                }
                _readingFonts.Apply(_lblLiveVietnamese, 14F * _readingScale);
                _readingFonts.Apply(_lblLiveJapanese, 11.5F * _readingScale);
                _readingFonts.Apply(_historyTitle, 11.5F * Math.Min(_readingScale, 1.25F));
                int[] columns = _liveGrid.GetColumnWidths();
                int japaneseHeight = _googleLiveMode && !_showJapanese ? 0 : _lblLiveJapanese.GetPreferredSize(new Size(Math.Max(100, columns[0]), 0)).Height;
                int vietnameseHeight = _lblLiveVietnamese.GetPreferredSize(new Size(Math.Max(100, columns[2]), 0)).Height;
                float wanted = Math.Max(120 * dpi * _readingScale, Math.Max(japaneseHeight, vietnameseHeight) + 56 * dpi);
                // Câu dài được thêm chỗ, nhưng lịch sử luôn còn diện tích đọc/cuộn.
                float height = Math.Min(wanted, Math.Max(120 * dpi, ClientSize.Height * 0.30F));
                if (_subtitleReadingMode) height = 0;
                if (Math.Abs(_liveRow.Height - height) > 1) _liveRow.Height = (float)Math.Ceiling(height);
                _latencyTip.SetToolTip(_lblLiveJapanese, _lblLiveJapanese.Text);
                _latencyTip.SetToolTip(_lblLiveVietnamese, _lblLiveVietnamese.Text);
                _bodyLayout.PerformLayout();
            }
            finally { _updatingReadingLayout = false; }
            RelayoutHistoryCards();
        }

        private void UpdateJapaneseVisibility()
        {
            bool sourceVisible = !_googleLiveMode || _showJapanese;
            _liveGrid.SuspendLayout();
            _sourceTextTitle.Visible = _lblLiveJapanese.Visible = _liveSeparator.Visible = sourceVisible;
            _liveGrid.ColumnStyles[0].Width = sourceVisible ? 43 : 0;
            _liveGrid.ColumnStyles[1].Width = sourceVisible ? 28 * DeviceDpi / 96F : 0;
            _liveGrid.ColumnStyles[2].Width = sourceVisible ? 57 : 100;
            if (_googleLiveMode && sourceVisible) _lblLiveJapanese.Text = string.IsNullOrEmpty(_lastLiveSource) ? "Chờ âm thanh tiếng Nhật…" : _lastLiveSource;
            _liveGrid.ResumeLayout(true);
            UpdateReadingLayout();
            if (_subtitleOverlay != null && !_subtitleOverlay.IsDisposed)
            {
                _subtitleOverlay.SetJapaneseVisibility(_showJapanese);
            }
        }

        private void RememberExportLine(MeetingTranslationLine line)
        {
            // Lưu bản chữ độc lập trong RAM cho xuất file, cập nhật đúng revision của từng lượt.
            if (!GoogleLiveTranscriptRows.IsSource(line) && (!line.TranslationSucceeded || string.IsNullOrWhiteSpace(line.Vietnamese))) return;
            var entry = _exportEntries.Find(x => x.Session == line.SessionId && x.Sequence == line.Sequence);
            if (entry != null && entry.Revision > line.Revision) return;
            if (entry == null)
            {
                entry = new MeetingExportEntry { Session = line.SessionId, Sequence = line.Sequence };
                int index = _exportEntries.FindIndex(x => x.Session == line.SessionId && x.Sequence > line.Sequence);
                if (index < 0) _exportEntries.Add(entry); else _exportEntries.Insert(index, entry);
            }
            entry.Revision = line.Revision;
            if (GoogleLiveTranscriptRows.IsLive(line))
            {
                entry.Text = "Thời gian nhận: " + line.RecognizedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz")
                    + "\r\nGoogle Live · Chưa xác định người nói\r\n"
                    + "Tiếng Việt:\r\n" + line.Vietnamese + "\r\nTiếng Nhật trong cùng đoạn:\r\n" + line.Japanese;
                return;
            }
            entry.Text = "Thời gian: " + line.RecognizedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz")
                + "\r\nNgười nói: " + (line.HasSpeaker ? line.SpeakerName : "Không rõ người nói")
                + "\r\nTiếng Nhật:\r\n" + line.Japanese + "\r\nTiếng Việt:\r\n" + line.Vietnamese
                + (line.IsDraft ? "\r\nGhi chú: Bản dịch đang hiệu đính." : line.IsApproximate ? "\r\nGhi chú: Bản dịch gần nghĩa." : "");
        }

        private string BuildSessionText()
        {
            bool hasLive = _historyRows.Any(row => GoogleLiveTranscriptRows.IsLive(row.Line));
            var text = new System.Text.StringBuilder();
            text.AppendLine("TTSK — BẢN DỊCH CUỘC HỌP NHẬT → VIỆT");
            text.AppendLine("Xuất lúc: " + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
            text.AppendLine((hasLive ? "Số mục trong phiên: " : "Số lượt đã dịch: ") + _exportEntries.Count);
            text.AppendLine(hasLive ? "Gồm cả câu Nhật đang ẩn. Google Live gom nguồn/bản dịch theo đoạn nhận; hai ngôn ngữ có thể lệch thời điểm."
                : "Nội dung đã dịch trong phiên đến thời điểm xuất; gồm cả câu Nhật đang ẩn trên giao diện.");
            for (int i = 0; i < _exportEntries.Count; i++)
            {
                text.AppendLine();
                text.AppendLine("──────────────────── " + (hasLive ? "Mục " : "Lượt ") + (i + 1) + " ────────────────────");
                text.AppendLine(_exportEntries[i].Text);
            }
            return text.ToString();
        }

        private void ExportSessionText()
        {
            if (_exportEntries.Count == 0) { ShowError("Chưa có bản dịch để xuất."); return; }
            try
            {
                string path = WriteSessionText(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), DateTime.Now, BuildSessionText());
                UpdateStatus("Đã xuất bản dịch ra Desktop: " + path);
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
            { ShowError("Không thể xuất file: " + ex.Message); }
        }

        private static string WriteSessionText(string desktop, DateTime date, string content)
        {
            string name = date.ToString("dd-MM-yy", System.Globalization.CultureInfo.InvariantCulture);
            for (int number = 1; ; number++)
            {
                string path = System.IO.Path.Combine(desktop, name + (number == 1 ? "" : " (" + number + ")") + ".txt");
                System.IO.FileStream file;
                try
                {
                    // CreateNew giữ nguyên file đã có, kể cả khi hai cửa sổ cùng xuất một lúc.
                    file = new System.IO.FileStream(path, System.IO.FileMode.CreateNew, System.IO.FileAccess.Write, System.IO.FileShare.None);
                }
                catch (System.IO.IOException) when (System.IO.File.Exists(path)) { continue; }
                using (file)
                using (var writer = new System.IO.StreamWriter(file, new System.Text.UTF8Encoding(true))) writer.Write(content);
                return path;
            }
        }

        private sealed class MeetingExportEntry
        {
            public Guid Session;
            public long Sequence;
            public int Revision;
            public string Text;
        }

        private void ClearSession()
        {
            if (_googleLiveMode)
            {
                lock (_audioGate) { _audioText.Clear(); _audioTextLength = 0; }
                _lastLiveSource = string.Empty;
                CreateLiveRows();
            }
            _historyRows.Clear();
            _historyRowsBySequence.Clear();
            _exportEntries.Clear();
            _pnlHistory.ClearHistory();
            _speakerColors.Clear();
            _latestSessionId = Guid.Empty;
            _latestGeneration = 0;
            _latestRevision = 0;
            _latestTranslatedJapanese = string.Empty;
            _receivingSequence = 0;
            _receivingJapanese = null;
            _translationErrorVisible = false;
            _userScrolledUp = false;
            UpdateLatestTranslationButton();
            _lblLiveJapanese.Text = "Chờ phụ đề...";
            _lblLiveVietnamese.Text = "---";
            _lblLatency.Text = "";
            if (_subtitleOverlay != null && !_subtitleOverlay.IsDisposed)
                _subtitleOverlay.UpdateSubtitle("Chờ câu nói tiếng Nhật...", "", "");
            ClearError();
            UpdateStatus("Đã xóa phiên làm việc nội bộ.");
        }

        private void ToggleSubtitleOverlay()
        {
            // Chỉ đổi cách đọc; phiên dịch, hàng đợi và lịch sử vẫn dùng cùng dữ liệu.
            var header = (TableLayoutPanel)_bodyLayout.GetControlFromPosition(0, 3);
            _bodyLayout.SuspendLayout();
            if (!_subtitleReadingMode)
            {
                for (int i = 0; i < 4; i++) _normalRows[i] = _bodyLayout.RowStyles[i].Height;
                for (int i = 0; i < 6; i++) _normalColumns[i] = header.ColumnStyles[i].Width;
                _normalBodyPadding = _bodyLayout.Padding; _normalHeaderPadding = header.Padding;
            }
            _subtitleReadingMode = !_subtitleReadingMode;
            _pnlHistory.ReadingOnly = _subtitleReadingMode;
            for (int i = 0; i < 3; i++)
            {
                _bodyLayout.GetControlFromPosition(0, i).Visible = !_subtitleReadingMode;
                _bodyLayout.RowStyles[i].Height = _subtitleReadingMode ? 0 : _normalRows[i];
            }
            float dpi = DeviceDpi / 96F;
            _bodyLayout.Padding = _subtitleReadingMode ? new Padding((int)(12 * dpi), 0, (int)(12 * dpi), (int)(8 * dpi)) : _normalBodyPadding;
            _bodyLayout.RowStyles[3].Height = _subtitleReadingMode ? 42 * dpi : _normalRows[3];
            header.Padding = _subtitleReadingMode ? new Padding(0, (int)(4 * dpi), 0, (int)(4 * dpi)) : _normalHeaderPadding;
            foreach (int column in new[] { 1, 3, 4, 5 }) header.ColumnStyles[column].Width = _subtitleReadingMode ? 0 : _normalColumns[column];
            _chkShowJapanese.Visible = _btnExportText.Visible = _btnClearSession.Visible = _btnRetryPending.Visible = !_subtitleReadingMode;
            _btnSubtitleOverlay.Text = _subtitleReadingMode ? "Quay lại · Esc" : "Cửa sổ phụ đề";
            _historyTitle.Text = _subtitleReadingMode || _googleLiveMode ? "Bản dịch tiếng Việt" : "Bản dịch trong phiên";
            UpdateLatestTranslationButton();
            _bodyLayout.ResumeLayout(true);
            UpdateReadingLayout();
            _pnlHistory.Focus();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_subtitleReadingMode && keyData == Keys.Escape) { ToggleSubtitleOverlay(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private ColorPair GetSpeakerColorPair(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return new ColorPair(
                    _darkMode ? Color.FromArgb(40, 40, 48) : Color.FromArgb(240, 240, 245),
                    _darkMode ? Color.FromArgb(160, 160, 175) : Color.FromArgb(100, 110, 125)
                );
            }

            if (_speakerColors.TryGetValue(name, out ColorPair existing))
                return existing;

            ColorPair[] curated = new ColorPair[]
            {
                new ColorPair(Color.FromArgb(236, 244, 254), Color.FromArgb(41, 108, 198)), // Blue
                new ColorPair(Color.FromArgb(237, 247, 237), Color.FromArgb(46, 125, 50)),  // Green
                new ColorPair(Color.FromArgb(254, 243, 235), Color.FromArgb(216, 92, 28)),  // Orange
                new ColorPair(Color.FromArgb(247, 238, 249), Color.FromArgb(142, 36, 170)), // Purple
                new ColorPair(Color.FromArgb(238, 248, 245), Color.FromArgb(0, 137, 123)),  // Teal
                new ColorPair(Color.FromArgb(254, 238, 242), Color.FromArgb(194, 24, 91)),  // Rose
                new ColorPair(Color.FromArgb(254, 249, 231), Color.FromArgb(183, 110, 0))   // Amber
            };

            int index = _speakerColors.Count % curated.Length;
            ColorPair picked = curated[index];
            if (_darkMode)
                picked = new ColorPair(Color.FromArgb(28 + picked.Accent.R / 8, 28 + picked.Accent.G / 8, 32 + picked.Accent.B / 8),
                    Color.FromArgb(120 + picked.Accent.R / 2, 120 + picked.Accent.G / 2, 120 + picked.Accent.B / 2));
            _speakerColors[name] = picked;
            return picked;
        }

        private void UpdateStatus(string message)
        {
            _lblStatus.Text = message;
        }

        private void ShowError(string message)
        {
            _lblError.Text = message;
            _errorRow.Height = 44;
            _pnlErrorBar.Visible = true;
        }

        private void ClearError()
        {
            _lblError.Text = "";
            _pnlErrorBar.Visible = false;
            _errorRow.Height = 0;
        }

        private void SafeInvoke(Action action)
        {
            if (IsDisposed || _closing || !IsHandleCreated) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(() => { if (!IsDisposed && !_closing) action(); })); } catch { }
            }
            else
            {
                action();
            }
        }

        protected override async void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel) return;
            if (_closing) { e.Cancel = _coordinator != null || _audioRun != null; return; }
            _closing = true;
            Interlocked.Increment(ref _teamsEpoch);
            if (_subtitleOverlay != null && !_subtitleOverlay.IsDisposed)
            {
                _subtitleOverlay.Close();
                _subtitleOverlay = null;
            }
            if (_coordinator != null || _audioRun != null)
            {
                e.Cancel = true;
                Enabled = false;
                try
                {
                    await StopGoogleLiveAsync();
                    var coord = _coordinator;
                    if (coord != null) await coord.DisposeAsync();
                }
                catch { }
                finally { _coordinator = null; }
                if (!IsDisposed) Close();
            }
        }

        private Button CreateButton(string text, bool isPrimary, int width, int height)
        {
            Button btn = new MeetingButton();
            btn.Text = text;
            btn.Size = new Size(width, height);
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = isPrimary ? 0 : 1;
            btn.Font = new Font("Segoe UI", 9F, isPrimary ? FontStyle.Bold : FontStyle.Regular);
            btn.Cursor = Cursors.Hand;

            if (isPrimary)
            {
                btn.BackColor = _accentColor;
                btn.ForeColor = _darkMode ? Color.FromArgb(24, 26, 31) : Color.White;
                btn.FlatAppearance.BorderColor = _accentColor;
            }
            else
            {
                btn.BackColor = _darkMode ? Color.FromArgb(36, 36, 44) : Color.White;
                btn.ForeColor = _textColor;
                btn.FlatAppearance.BorderColor = _borderColor;
            }
            return btn;
        }

        private Control CreateTitleBar()
        {
            var bar = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = _cardBack };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 132, Margin = Padding.Empty, WrapContents = false };
            for (int kind = 0; kind < 3; kind++)
            {
                int action = kind;
                var button = new MeetingWindowButton { Kind = kind, Size = new Size(44, 38), ForeColor = _textColor,
                    BackColor = _cardBack, HoverColor = _accentSoftColor, AccessibleName = kind == 0 ? "Thu nhỏ" : kind == 1 ? "Phóng to / khôi phục" : "Đóng" };
                button.Click += delegate { if (action == 0) WindowState = FormWindowState.Minimized; else if (action == 1) ToggleMaximize(); else Close(); };
                buttons.Controls.Add(button);
            }
            var title = new Label { Text = Text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(16, 0, 0, 0), ForeColor = _mutedTextColor, AutoEllipsis = true, Font = new Font("Segoe UI", 9F) };
            MouseEventHandler drag = (sender, e) => { if (e.Button == MouseButtons.Left && e.Clicks == 1) { ReleaseCapture(); SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); } };
            title.MouseDown += drag; bar.MouseDown += drag;
            title.DoubleClick += delegate { ToggleMaximize(); }; bar.DoubleClick += delegate { ToggleMaximize(); };
            bar.Controls.Add(title); bar.Controls.Add(buttons);
            return bar;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) MeetingApiKeyRing.Changed -= OnKeyRingChanged;
            if (disposing) { _audioCancellation?.Cancel(); _audioTimer?.Dispose(); _providerMenu?.Dispose(); _latencyTip.Dispose(); _readingFonts.Dispose(); }
            base.Dispose(disposing);
        }

        private Rectangle _meetingRestoreBounds;
        private void ToggleMaximize()
        {
            if (WindowState == FormWindowState.Maximized)
            {
                WindowState = FormWindowState.Normal;
                if (!_meetingRestoreBounds.IsEmpty) Bounds = _meetingRestoreBounds;
            }
            else
            {
                _meetingRestoreBounds = Bounds;
                MaximizedBounds = Screen.FromHandle(Handle).WorkingArea;
                WindowState = FormWindowState.Maximized;
            }
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct WindowMinMaxInfo
        {
            public Point Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
        }
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(_borderColor)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
        protected override void WndProc(ref Message message)
        {
            // Route native caption double-click through the same monitor-aware action.
            if (message.Msg == 0xA3 && message.WParam == (IntPtr)2)
            { ToggleMaximize(); message.Result = IntPtr.Zero; return; }
            base.WndProc(ref message);
            if (message.Msg == 0x24 && message.LParam != IntPtr.Zero)
            {
                var info = (WindowMinMaxInfo)Marshal.PtrToStructure(message.LParam, typeof(WindowMinMaxInfo));
                var screen = Screen.FromHandle(Handle);
                Rectangle work = screen.WorkingArea, monitor = screen.Bounds;
                info.MaxPosition = new Point(work.Left - monitor.Left, work.Top - monitor.Top);
                info.MaxSize = new Point(work.Width, work.Height);
                info.MinTrackSize = new Point(Math.Min(MinimumSize.Width, work.Width), Math.Min(MinimumSize.Height, work.Height));
                Marshal.StructureToPtr(info, message.LParam, false);
                return;
            }
            if (message.Msg != 0x84 || WindowState == FormWindowState.Maximized) return;
            long point = message.LParam.ToInt64();
            Point p = PointToClient(new Point((short)(point & 0xffff), (short)((point >> 16) & 0xffff)));
            const int edge = 6;
            bool left = p.X < edge, right = p.X >= ClientSize.Width - edge, top = p.Y < edge, bottom = p.Y >= ClientSize.Height - edge;
            if (top) message.Result = (IntPtr)(left ? 13 : right ? 14 : 12);
            else if (bottom) message.Result = (IntPtr)(left ? 16 : right ? 17 : 15);
            else if (left || right) message.Result = (IntPtr)(left ? 10 : 11);
        }
    }

    /// <summary>
    /// Custom history card matching NihongoFlow design:
    /// Pastel speaker badge (Initials avatar + name on same row, no "TEAMS" label),
    /// Vertical gold bar, large Vietnamese text, smaller Japanese text below.
    /// </summary>
    internal sealed class MeetingSelectableText : TextBox
    {
        public MeetingSelectableText()
        {
            Multiline = true; ReadOnly = true; BorderStyle = BorderStyle.None;
            WordWrap = true; ScrollBars = ScrollBars.None; AutoSize = false;
            ShortcutsEnabled = true; HideSelection = false;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int width = Math.Max(1, proposedSize.Width > 0 ? proposedSize.Width : Width);
            return new Size(width, MeasureHeight(Text, Font, width));
        }
        internal static int MeasureHeight(string text, Font font, int width)
        {
            var measured = TextRenderer.MeasureText(string.IsNullOrEmpty(text) ? " " : text, font,
                new Size(Math.Max(1, width - 4), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix);
            return measured.Height + 4;
        }
    }

    internal sealed class MeetingHistoryMetrics : IDisposable
    {
        private Font _vietnamese, _japanese, _timing;
        private float _scale;
        private readonly Label _timingLabel = new Label { AutoSize = true };
        public int Measure(MeetingTranslationLine line, int width, float scale, int dpi, bool compact, bool japanese)
        {
            if (_vietnamese == null || _scale != scale)
            {
                _vietnamese?.Dispose(); _japanese?.Dispose(); _timing?.Dispose();
                _vietnamese = new Font("Segoe UI", 12F * scale, FontStyle.Bold);
                _japanese = new Font("Yu Gothic UI", 9.5F * scale);
                _timing = new Font("Segoe UI", 7.8F * Math.Min(scale, 1.2F));
                _timingLabel.Font = _timing; _scale = scale;
            }
            float pixels = scale * dpi / 96F;
            int inset = (int)Math.Round(12 * pixels);
            int textLeft = 2 * inset + 4 + (line.HasSpeaker ? inset + (int)(130 * Math.Min(scale, 1.35F) * dpi / 96F) : 0);
            _timingLabel.Text = MeetingHistoryRow.ReadingTime(line);
            int timingWidth = compact ? 0 : Math.Max((int)(98 * pixels), _timingLabel.PreferredWidth + inset);
            int textWidth = Math.Max(120, width - textLeft - timingWidth - inset);
            int bottom = (int)Math.Round(8 * pixels) + (GoogleLiveTranscriptRows.IsSource(line) ? 0 : MeetingSelectableText.MeasureHeight(line.Vietnamese, _vietnamese, textWidth));
            if (japanese) bottom += (GoogleLiveTranscriptRows.IsSource(line) ? 0 : (int)Math.Round(4 * pixels)) + MeetingSelectableText.MeasureHeight(line.Japanese, _japanese, textWidth);
            return Math.Max((int)Math.Round((compact ? 54 : 64) * pixels), bottom + inset);
        }
        public void Dispose() { _timingLabel.Dispose(); _vietnamese?.Dispose(); _japanese?.Dispose(); _timing?.Dispose(); }
    }

    // Lịch sử chỉ giữ dữ liệu và kích thước; control native tồn tại trong vùng đang đọc.
    internal sealed class MeetingHistoryRow
    {
        internal static string ReadingTime(MeetingTranslationLine line) => GoogleLiveTranscriptRows.IsLive(line)
            ? line.RecognizedAt.ToLocalTime().ToString("HH:mm:ss") : line.DisplayTiming;
        public MeetingTranslationLine Line { get; private set; }
        public readonly ColorPair Colors;
        public int Top, Height, Version;
        private int _width;
        private int _dpi;
        private float _scale;
        private bool _compact, _japanese, _dirty = true;

        public MeetingHistoryRow(MeetingTranslationLine line, ColorPair colors) { Colors = colors; Update(line); }
        public void Update(MeetingTranslationLine line)
        {
            if (Line != null && line.Revision < Line.Revision) return;
            bool preserve = Line != null && line.Revision == Line.Revision && Line.TranslationSucceeded && !line.TranslationSucceeded;
            var next = line.Snapshot();
            if (preserve) { next.Vietnamese = Line.Vietnamese; next.TranslationSucceeded = true; next.IsDraft = Line.IsDraft; }
            Line = next; Version++; _dirty = true;
        }
        public HistoryCardControl CreateCard(int width, bool dark, bool compact, bool japanese, float scale)
        {
            var card = new HistoryCardControl(Line, Colors, dark, japanese);
            card.Width = width; card.SetCompactReading(compact, japanese); card.SetReadingScale(scale);
            return card;
        }
        public void Measure(int width, bool compact, bool japanese, float scale, MeetingHistoryMetrics metrics, int dpi)
        {
            if (!_dirty && width == _width && scale == _scale && compact == _compact && japanese == _japanese && dpi == _dpi) return;
            Height = metrics.Measure(Line, width, scale, dpi, compact, japanese);
            _width = width; _scale = scale; _compact = compact; _japanese = japanese; _dpi = dpi; _dirty = false;
        }
    }

    internal sealed class HistoryCardControl : Panel
    {
        private readonly List<Font> _baseFonts = new List<Font>();
        private Font CreateOwnedFont(string family, float size, FontStyle style)
        {
            var font = new Font(family, size, style); _baseFonts.Add(font); return font;
        }
        public Guid SessionId { get; }
        public long LiveGeneration { get; }
        public int Revision { get; private set; }
        public bool IsPending { get; private set; }
        private bool _hasTranslation;
        public bool HasTranslation => _hasTranslation;
        private bool _compactReading;

        private readonly bool _darkMode;
        private readonly ColorPair _speakerColors;
        private MeetingSelectableText _lblVietnamese;
        private MeetingSelectableText _lblJapanese;
        private Label _lblTiming;
        private Panel _speakerBadge;
        private Panel _goldBar;
        private readonly ToolTip _speakerTip = new ToolTip();
        private bool _showJapanese;
        private float _readingScale = 1F;
        private readonly MeetingReadingFonts _readingFonts = new MeetingReadingFonts();
        private Label _speakerName, _avatar;
        private bool _recalculating;
        private bool _sourceOnly;

        public HistoryCardControl(MeetingTranslationLine line, ColorPair speakerColors, bool darkMode, bool showJapanese)
        {
            SessionId = line.SessionId;
            LiveGeneration = line.LiveGeneration;
            Revision = line.Revision;
            IsPending = line.IsPending; _hasTranslation = line.TranslationSucceeded;
            _speakerColors = speakerColors;
            _darkMode = darkMode;
            _showJapanese = showJapanese;
            Width = 800;

            DoubleBuffered = true;
            BackColor = darkMode ? Color.FromArgb(28, 31, 37) : Color.White;
            Padding = new Padding(12, 10, 12, 10);
            Margin = new Padding(0, 0, 0, 8);

            _sourceOnly = GoogleLiveTranscriptRows.IsSource(line);
            BuildUi(line, showJapanese);
        }

        private void BuildUi(MeetingTranslationLine line, bool showJapanese)
        {
            Controls.Clear();

            // 1. Speaker Badge (Initials avatar + name on same row)
            bool hasSpeaker = !string.IsNullOrWhiteSpace(line.SpeakerName);
            int contentLeft = 14;

            if (hasSpeaker)
            {
                _speakerBadge = new RoundPanel(8, _speakerColors.Background, _speakerColors.Background);
                _speakerBadge.Location = new Point(12, 10);
                _speakerBadge.Size = new Size(130, 42);
                _speakerBadge.BackColor = _speakerColors.Background;
                _speakerBadge.Padding = new Padding(4);

                // Avatar circle
                Label avatar = new MeetingAvatar();
                _avatar = avatar;
                avatar.Text = ExtractInitials(line.SpeakerName);
                avatar.Font = CreateOwnedFont("Segoe UI", 9F, FontStyle.Bold);
                avatar.ForeColor = _darkMode ? Color.FromArgb(24, 24, 30) : Color.White;
                avatar.BackColor = _speakerColors.Accent;
                avatar.Size = new Size(28, 28);
                avatar.Location = new Point(6, 7);
                avatar.TextAlign = ContentAlignment.MiddleCenter;
                _speakerBadge.Controls.Add(avatar);

                // Speaker Name
                Label lblName = new Label();
                _speakerName = lblName;
                lblName.Text = line.SpeakerName;
                lblName.Font = CreateOwnedFont("Segoe UI", 8.5F, FontStyle.Bold);
                lblName.ForeColor = _speakerColors.Accent;
                lblName.Location = new Point(38, 5);
                lblName.Size = new Size(88, 32);
                lblName.AutoEllipsis = true;
                lblName.TextAlign = ContentAlignment.MiddleLeft;
                _speakerBadge.Controls.Add(lblName);

                ToolTip tip = _speakerTip;
                tip.SetToolTip(lblName, line.SpeakerName);
                tip.SetToolTip(_speakerBadge, line.SpeakerName);

                Controls.Add(_speakerBadge);
                contentLeft = _speakerBadge.Right + 12;
            }

            // 2. Gold Vertical Line
            Panel goldBar = new Panel();
            _goldBar = goldBar;
            goldBar.Location = new Point(contentLeft, 10);
            goldBar.Size = new Size(4, 42);
            goldBar.BackColor = Color.FromArgb(197, 163, 93);
            Controls.Add(goldBar);

            contentLeft = goldBar.Right + 12;

            // 3. Timing Label
            _lblTiming = new Label();
            _lblTiming.Text = MeetingHistoryRow.ReadingTime(line);
            _speakerTip.SetToolTip(_lblTiming, GoogleLiveTranscriptRows.Detail(line));
            _lblTiming.Font = CreateOwnedFont("Segoe UI", 7.8F, FontStyle.Regular);
            _lblTiming.ForeColor = _darkMode ? Color.FromArgb(140, 140, 155) : Color.FromArgb(150, 145, 135);
            _lblTiming.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _lblTiming.AutoSize = true;
            _lblTiming.Location = new Point(Width - 90, 10);
            Controls.Add(_lblTiming);

            // 4. Vietnamese text (Large, bold)
            _lblVietnamese = new MeetingSelectableText { BackColor = BackColor };
            _lblVietnamese.Text = line.Vietnamese;
            _lblVietnamese.Visible = !_sourceOnly;
            _lblVietnamese.Font = CreateOwnedFont("Segoe UI", 12F, FontStyle.Bold);
            _lblVietnamese.ForeColor = _darkMode ? Color.FromArgb(240, 240, 245) : Color.FromArgb(37, 44, 56);
            _lblVietnamese.Location = new Point(contentLeft, 8);
            _lblVietnamese.Size = new Size(Width - contentLeft - 100, 26);
            _lblVietnamese.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _lblVietnamese.AutoSize = false;
            _lblVietnamese.MaximumSize = new Size(Width - contentLeft - 100, 0);
            Controls.Add(_lblVietnamese);
            _speakerTip.SetToolTip(_lblVietnamese, line.IsPending ? line.Warning : string.Empty);

            // 5. Japanese text (Smaller, gray, Yu Gothic UI)
            _lblJapanese = new MeetingSelectableText { BackColor = BackColor };
            _lblJapanese.Text = line.Japanese;
            _lblJapanese.Font = CreateOwnedFont("Yu Gothic UI", 9.5F, FontStyle.Regular);
            _lblJapanese.ForeColor = _darkMode ? Color.FromArgb(160, 165, 175) : Color.FromArgb(115, 123, 134);
            _lblJapanese.Location = new Point(contentLeft, _lblVietnamese.Bottom + 4);
            _lblJapanese.Size = new Size(Width - contentLeft - 100, 20);
            _lblJapanese.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _lblJapanese.AutoSize = false;
            _lblJapanese.MaximumSize = new Size(Width - contentLeft - 100, 0);
            _lblJapanese.Visible = showJapanese;
            Controls.Add(_lblJapanese);

            RecalculateHeight(goldBar);
        }

        public void UpdateLine(MeetingTranslationLine line, bool showJapanese)
        {
            if (line.Revision >= Revision)
            {
                bool preserveDraft = line.Revision == Revision && _hasTranslation && !line.TranslationSucceeded;
                Revision = line.Revision;
                IsPending = line.IsPending;
                _hasTranslation = preserveDraft || line.TranslationSucceeded;
                _showJapanese = showJapanese;
                if (!preserveDraft) _lblVietnamese.Text = line.Vietnamese;
                _lblJapanese.Text = line.Japanese;
                _lblTiming.Text = MeetingHistoryRow.ReadingTime(line);
                _speakerTip.SetToolTip(_lblVietnamese, line.IsPending ? line.Warning : string.Empty);
                _speakerTip.SetToolTip(_lblTiming, GoogleLiveTranscriptRows.Detail(line));
                _lblJapanese.Visible = showJapanese;

                // Adjust positions
                _lblJapanese.Location = new Point(_lblJapanese.Left, _lblVietnamese.Bottom + 4);
                RecalculateHeight(null);
            }
        }

        public void SetJapaneseVisibility(bool show)
        {
            _showJapanese = show;
            _lblJapanese.Visible = show;
            RecalculateHeight(null);
        }

        public void SetCompactReading(bool compact, bool showJapanese)
        {
            if (_compactReading == compact && _showJapanese == showJapanese) return;
            _compactReading = compact;
            _lblTiming.Visible = !compact;
            SetJapaneseVisibility(showJapanese);
        }

        private void RecalculateHeight(Panel goldBar)
        {
            if (_lblJapanese == null || _lblVietnamese == null || _recalculating) return;
            _recalculating = true;
            try
            {
                float dpi = DeviceDpi / 96F;
                float scale = _readingScale * dpi;
                int inset = (int)Math.Round(12 * scale), vertical = (int)Math.Round(10 * scale);
                if (_speakerBadge != null)
                {
                    float badgeScale = Math.Min(_readingScale, 1.35F) * dpi;
                    _speakerBadge.SetBounds(inset, vertical, (int)(130 * badgeScale), (int)(42 * badgeScale));
                    _avatar.SetBounds((int)(6 * badgeScale), (int)(7 * badgeScale), (int)(28 * badgeScale), (int)(28 * badgeScale));
                    _speakerName.SetBounds((int)(38 * badgeScale), (int)(5 * badgeScale), (int)(88 * badgeScale), (int)(32 * badgeScale));
                }
                _goldBar.Left = _speakerBadge == null ? inset : _speakerBadge.Right + inset;
                _goldBar.Top = vertical;
                _lblVietnamese.Left = _lblJapanese.Left = _goldBar.Right + inset;
                _lblVietnamese.Top = (int)Math.Round(8 * scale);
                _lblTiming.Top = vertical;
                int timingWidth = _compactReading ? 0 : Math.Max((int)(98 * scale), _lblTiming.PreferredWidth + inset);
                int textWidth = Math.Max(120, Width - _lblVietnamese.Left - timingWidth - inset);
                _lblVietnamese.MaximumSize = new Size(textWidth, 0);
                _lblJapanese.MaximumSize = new Size(textWidth, 0);
                _lblVietnamese.Size = _lblVietnamese.GetPreferredSize(new Size(textWidth, 0));
                _lblJapanese.Size = _lblJapanese.GetPreferredSize(new Size(textWidth, 0));
                _lblJapanese.Top = _lblVietnamese.Bottom + (int)Math.Round(4 * scale);
                _lblTiming.Left = Math.Max(_lblVietnamese.Left, Width - _lblTiming.PreferredWidth - inset);
                if (_sourceOnly) _lblJapanese.Top = _lblVietnamese.Top;
                int bottom = _showJapanese ? _lblJapanese.Bottom : _lblVietnamese.Bottom;
                int total = Math.Max((int)Math.Round((_compactReading ? 54 : 64) * scale), bottom + inset);
                Height = total;
                if (_compactReading && !_showJapanese)
                    _lblVietnamese.Top = Math.Max((int)Math.Round(8 * scale), (total - _lblVietnamese.Height) / 2);

                goldBar = _goldBar;
                if (goldBar != null)
                {
                    goldBar.Height = Math.Max(42, total - 2 * vertical);
                }
                Invalidate();
            }
            finally { _recalculating = false; }
        }

        public void SetReadingScale(float scale)
        {
            if (Math.Abs(_readingScale - scale) < 0.01F) return;
            _readingScale = scale;
            _readingFonts.Apply(_lblVietnamese, 12F * scale);
            _readingFonts.Apply(_lblJapanese, 9.5F * scale);
            _readingFonts.Apply(_lblTiming, 7.8F * Math.Min(scale, 1.2F));
            if (_speakerName != null)
            {
                _readingFonts.Apply(_speakerName, 8.5F * Math.Min(scale, 1.35F));
                _readingFonts.Apply(_avatar, 9F * Math.Min(scale, 1.35F));
            }
            RecalculateHeight(null);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            RecalculateHeight(null);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _speakerTip.Dispose(); _readingFonts.Dispose(); foreach (var font in _baseFonts) font.Dispose(); _baseFonts.Clear(); }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color border = _darkMode ? Color.FromArgb(61, 61, 66) : Color.FromArgb(220, 223, 230);
            using (var path = MeetingVisuals.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 12))
            using (Pen pen = new Pen(border, 1))
            {
                e.Graphics.DrawPath(pen, path);
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? BackColor : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = MeetingVisuals.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 12))
            using (var brush = new SolidBrush(BackColor)) e.Graphics.FillPath(brush, path);
        }

        private static string ExtractInitials(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "?";
            var parts = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1) return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpperInvariant();
            return (parts[0].Substring(0, 1) + parts[parts.Length - 1].Substring(0, 1)).ToUpperInvariant();
        }
    }

    /// <summary>
    /// Floating subtitle overlay form (top-most, borderless, draggable).
    /// </summary>
    public sealed class MeetingSubtitleForm : Form
    {
        private readonly bool _darkMode;
        private Label _lblStatus;
        private Label _lblVietnamese;
        private Label _lblJapanese;
        private Label _lblTiming;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;

        public MeetingSubtitleForm(bool darkMode, bool showJapanese)
        {
            _darkMode = darkMode;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            Size = new Size(680, 180);
            Location = new Point(Screen.PrimaryScreen.WorkingArea.Width / 2 - 340, Screen.PrimaryScreen.WorkingArea.Height - 260);
            DoubleBuffered = true;

            Color back = darkMode ? Color.FromArgb(24, 24, 28) : Color.FromArgb(247, 243, 234);
            Color text = darkMode ? Color.FromArgb(240, 240, 245) : Color.FromArgb(34, 43, 56);
            BackColor = back;
            ForeColor = text;

            BuildUi(showJapanese);
        }

        private void BuildUi(bool showJapanese)
        {
            // Title Bar
            Panel topBar = new Panel();
            topBar.Width = ClientSize.Width;
            topBar.Dock = DockStyle.Top;
            topBar.Height = 32;
            topBar.Padding = new Padding(12, 6, 8, 4);
            topBar.MouseDown += OnDragMouseDown;

            Label lblTitle = new Label();
            lblTitle.Text = "TTSK • VIETNAMESE SUBTITLE";
            lblTitle.Font = new Font("Segoe UI", 7.8F, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(197, 163, 93);
            lblTitle.AutoSize = true;
            lblTitle.Location = new Point(12, 8);
            lblTitle.MouseDown += OnDragMouseDown;
            topBar.Controls.Add(lblTitle);

            Button btnClose = new Button();
            btnClose.Text = "✕";
            btnClose.Size = new Size(26, 22);
            btnClose.FlatStyle = FlatStyle.Flat;
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
            btnClose.ForeColor = ForeColor;
            btnClose.Cursor = Cursors.Hand;
            btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnClose.Location = new Point(topBar.Width - 34, 5);
            btnClose.Click += delegate { Close(); };
            topBar.Controls.Add(btnClose);

            _lblTiming = new Label();
            _lblTiming.Text = "";
            _lblTiming.Font = new Font("Segoe UI", 8F, FontStyle.Regular);
            _lblTiming.ForeColor = Color.FromArgb(140, 135, 125);
            _lblTiming.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _lblTiming.AutoSize = true;
            _lblTiming.Location = new Point(topBar.Width - 120, 8);
            _lblTiming.MouseDown += OnDragMouseDown;
            topBar.Controls.Add(_lblTiming);

            Controls.Add(topBar);

            // Subtitle text area
            Panel body = new Panel();
            body.Dock = DockStyle.Fill;
            body.Padding = new Padding(16, 8, 16, 12);
            body.MouseDown += OnDragMouseDown;

            _lblVietnamese = new Label();
            _lblVietnamese.Text = "Chờ câu nói tiếng Nhật...";
            _lblVietnamese.Font = new Font("Segoe UI", 16F, FontStyle.Bold);
            _lblVietnamese.ForeColor = ForeColor;
            _lblVietnamese.TextAlign = ContentAlignment.MiddleCenter;
            _lblVietnamese.Dock = DockStyle.Fill;
            _lblVietnamese.AutoEllipsis = true;
            _lblVietnamese.MouseDown += OnDragMouseDown;
            body.Controls.Add(_lblVietnamese);

            _lblJapanese = new Label();
            _lblJapanese.Text = "";
            _lblJapanese.Font = new Font("Yu Gothic UI", 11F, FontStyle.Regular);
            _lblJapanese.ForeColor = Color.FromArgb(135, 140, 150);
            _lblJapanese.TextAlign = ContentAlignment.MiddleCenter;
            _lblJapanese.Dock = DockStyle.Bottom;
            _lblJapanese.Height = 30;
            _lblJapanese.AutoEllipsis = true;
            _lblJapanese.Visible = showJapanese;
            _lblJapanese.MouseDown += OnDragMouseDown;
            body.Controls.Add(_lblJapanese);

            Controls.Add(body);
            body.BringToFront();
            _lblJapanese.ForeColor = _darkMode ? Color.FromArgb(178, 185, 197) : Color.FromArgb(105, 112, 122);
        }

        private void OnDragMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
            }
        }

        public void UpdateSubtitle(string vietnamese, string japanese, string timing)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(() => UpdateSubtitle(vietnamese, japanese, timing))); } catch { }
                return;
            }
            _lblVietnamese.Text = vietnamese;
            _lblJapanese.Text = japanese;
            _lblTiming.Text = timing;
        }

        public void SetJapaneseVisibility(bool show)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(() => SetJapaneseVisibility(show))); } catch { }
                return;
            }
            _lblJapanese.Visible = show;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color border = _darkMode ? Color.FromArgb(80, 80, 95) : Color.FromArgb(205, 187, 150);
            using (Pen pen = new Pen(border, 2))
            {
                e.Graphics.DrawRectangle(pen, 1, 1, Width - 2, Height - 2);
            }
        }
    }

    internal struct ColorPair
    {
        public Color Background { get; }
        public Color Accent { get; }

        public ColorPair(Color background, Color accent)
        {
            Background = background;
            Accent = accent;
        }
    }

    // Mỗi control dùng font riêng; giải phóng font thay thế khi kéo cửa sổ liên tục.
    internal sealed class MeetingReadingFonts : IDisposable
    {
        private readonly Dictionary<Control, Font> _owned = new Dictionary<Control, Font>();
        public void Apply(Control control, float points)
        {
            if (Math.Abs(control.Font.SizeInPoints - points) < 0.01F) return;
            Font next = new Font(control.Font.FontFamily, points, control.Font.Style, GraphicsUnit.Point);
            Font previous;
            _owned.TryGetValue(control, out previous);
            control.Font = next;
            _owned[control] = next;
            if (previous != null) previous.Dispose();
        }
        public void Dispose()
        {
            foreach (Font font in _owned.Values) font.Dispose();
            _owned.Clear();
        }
    }

    internal sealed class MeetingHistoryPanel : Panel
    {
        private readonly List<MeetingHistoryRow> _rows = new List<MeetingHistoryRow>();
        private readonly Dictionary<MeetingHistoryRow, HistoryCardControl> _visible = new Dictionary<MeetingHistoryRow, HistoryCardControl>();
        private readonly Dictionary<MeetingHistoryRow, int> _versions = new Dictionary<MeetingHistoryRow, int>();
        private bool _dark, _compact, _japanese, _updating;
        private float _scale = 1F;
        private int _cardWidth;
        private readonly MeetingHistoryMetrics _metrics = new MeetingHistoryMetrics();
        private readonly Color _thumb, _hoverThumb;
        private int _offset, _extent, _dragY, _dragOffset;
        private bool _hover, _drag;
        public event Action UserScrolled;
        public int MaximumOffset => Math.Max(0, _extent - ClientSize.Height);
        public bool IsAtBottom => _offset >= MaximumOffset - 30;
        public bool ReadingOnly { get; set; }
        public MeetingHistoryPanel(bool dark)
        {
            DoubleBuffered = true; AutoScroll = false; TabStop = true;
            SetStyle(ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
            _thumb = dark ? Color.FromArgb(95, 82, 70) : Color.FromArgb(164, 172, 185);
            _hoverThumb = dark ? Color.FromArgb(160, 122, 86) : Color.FromArgb(112, 131, 160);
            AccessibleName = "Lịch sử bản dịch · dùng con lăn hoặc phím mũi tên để cuộn";
        }
        public void ClearHistory()
        {
            foreach (var card in _visible.Values) { Controls.Remove(card); card.Dispose(); }
            _visible.Clear(); _versions.Clear(); _rows.Clear(); _extent = _offset = 0; Invalidate();
        }
        public void SetHistory(List<MeetingHistoryRow> history, bool dark, bool compact, bool japanese, float scale, bool followEnd)
        {
            var anchor = _rows.Count == 0 ? null : _rows[FirstVisible()];
            double fraction = anchor == null ? 0 : (_offset - anchor.Top) / (double)Math.Max(1, anchor.Height);
            _dark = dark; _compact = compact; _japanese = japanese; _scale = scale;
            _cardWidth = Math.Max(300, ClientSize.Width - 20);
            _rows.Clear(); int top = 4;
            foreach (var row in history)
            {
                if (compact && !row.Line.TranslationSucceeded) continue;
                // Nguồn tới trước được giữ lại; khi có Việt, cùng row/revision sẽ hiện đủ hai phần.
                if (GoogleLiveTranscriptRows.IsSource(row.Line)) continue;
                row.Measure(_cardWidth, compact, japanese && (!GoogleLiveTranscriptRows.IsLive(row.Line) || row.Line.Japanese.Length > 0), scale, _metrics, DeviceDpi);
                row.Top = top; top += row.Height + (int)Math.Round((compact ? 4 : 8) * scale);
                _rows.Add(row);
            }
            _extent = top;
            int offset = followEnd ? MaximumOffset : anchor != null && _rows.Contains(anchor) ? anchor.Top + (int)Math.Round(fraction * anchor.Height) : _offset;
            SetOffset(offset, false);
        }
        private int FirstVisible()
        {
            int lo = 0, hi = _rows.Count;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (_rows[mid].Top + _rows[mid].Height <= _offset) lo = mid + 1; else hi = mid;
            }
            return Math.Min(lo, Math.Max(0, _rows.Count - 1));
        }
        private void UpdateVisibleCards()
        {
            if (_updating) return;
            _updating = true; SuspendLayout();
            try
            {
                var needed = new HashSet<MeetingHistoryRow>();
                for (int i = FirstVisible(); i < _rows.Count && _rows[i].Top < _offset + Height; i++) needed.Add(_rows[i]);
                foreach (var row in new List<MeetingHistoryRow>(_visible.Keys))
                {
                    if (needed.Contains(row)) continue;
                    var card = _visible[row]; Controls.Remove(card); card.Dispose(); _visible.Remove(row); _versions.Remove(row);
                }
                foreach (var row in needed)
                {
                    bool showSource = _japanese && (!GoogleLiveTranscriptRows.IsLive(row.Line) || row.Line.Japanese.Length > 0);
                    HistoryCardControl card;
                    if (!_visible.TryGetValue(row, out card))
                    {
                        card = row.CreateCard(_cardWidth, _dark, _compact, showSource, _scale);
                        card.Location = new Point(0, row.Top - _offset);
                        _visible.Add(row, card); _versions[row] = row.Version; Controls.Add(card);
                    }
                    else
                    {
                        if (_versions[row] != row.Version) { card.UpdateLine(row.Line, showSource); _versions[row] = row.Version; }
                        card.Width = _cardWidth; card.SetCompactReading(_compact, showSource); card.SetReadingScale(_scale);
                        card.Top = row.Top - _offset;
                    }
                }
            }
            finally { ResumeLayout(false); _updating = false; }
        }
        public void ScrollToBottom() { SetOffset(MaximumOffset, false); }
        public void ScrollToOffset(int offset) { SetOffset(offset, false); }
        private Rectangle Thumb
        {
            get
            {
                int h = Math.Min(Height, Math.Max(34, (int)((long)Height * Height / Math.Max(1, _extent))));
                int y = MaximumOffset == 0 ? 0 : (int)((long)_offset * Math.Max(0, Height - h) / MaximumOffset);
                return new Rectangle(Math.Max(0, Width - 10), y, 6, h);
            }
        }
        private void SetOffset(int value, bool user)
        {
            _offset = Math.Max(0, Math.Min(MaximumOffset, value));
            UpdateVisibleCards();
            Invalidate(); if (user) UserScrolled?.Invoke();
        }
        protected override void OnControlAdded(ControlEventArgs e) { base.OnControlAdded(e); WireWheel(e.Control); }
        private void WireWheel(Control control)
        {
            control.MouseWheel += ChildWheel;
            // Không giành focus của ô chữ: giữ thao tác bôi đen và Ctrl+C khi đọc lịch sử.
            if (!(control is TextBoxBase))
                control.MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) Focus(); };
            foreach (Control child in control.Controls) WireWheel(child);
        }
        private void ChildWheel(object sender, MouseEventArgs e)
        {
            var handled = e as HandledMouseEventArgs;
            if (handled != null && handled.Handled) return;
            SetOffset(_offset - e.Delta / 120 * 72, true);
            if (handled != null) handled.Handled = true;
        }
        protected override void OnMouseWheel(MouseEventArgs e) { ChildWheel(this, e); }
        protected override void Dispose(bool disposing)
        {
            if (disposing) _metrics.Dispose();
            base.Dispose(disposing);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); Focus();
            if (e.Button != MouseButtons.Left || MaximumOffset == 0 || e.X < Width - 16) return;
            if (!Thumb.Contains(e.Location)) SetOffset(_offset + (e.Y < Thumb.Top ? -Height : Height), true);
            _drag = true; _dragY = e.Y; _dragOffset = _offset; Capture = true;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_drag) SetOffset(_dragOffset + (int)((long)(e.Y - _dragY) * MaximumOffset / Math.Max(1, Height - Thumb.Height)), true);
            bool hover = MaximumOffset > 0 && Thumb.Contains(e.Location);
            if (hover != _hover) { _hover = hover; Invalidate(); }
            Cursor = _hover || _drag ? Cursors.Hand : Cursors.Default;
        }
        protected override void OnMouseUp(MouseEventArgs e) { _drag = false; Capture = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); SetOffset(_offset, false); }
        protected override bool IsInputKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            return key == Keys.Up || key == Keys.Down || key == Keys.PageUp || key == Keys.PageDown || key == Keys.Home || key == Keys.End || base.IsInputKey(keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            int next = _offset;
            switch (e.KeyCode)
            {
                case Keys.Up: next -= 36; break; case Keys.Down: next += 36; break;
                case Keys.PageUp: next -= Height; break; case Keys.PageDown: next += Height; break;
                case Keys.Home: next = 0; break; case Keys.End: next = MaximumOffset; break;
                default: base.OnKeyDown(e); return;
            }
            SetOffset(next, true); e.Handled = true;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_rows.Count == 0)
            {
                Color muted = BackColor.GetBrightness() < .5f ? Color.FromArgb(154, 163, 175) : Color.FromArgb(114, 123, 132);
                TextRenderer.DrawText(e.Graphics, ReadingOnly ? "Chưa có câu dịch hoàn tất" : "Chưa có bản dịch trong phiên", Font,
                    new Rectangle(12, Math.Max(8, Height / 2 - 28), Math.Max(1, Width - 24), 28), muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(e.Graphics, ReadingOnly ? "Bản dịch mới sẽ tự xuất hiện ở đây · Esc để quay lại." : "Bật Live captions tiếng Nhật trong Teams, rồi nhấn Bắt đầu.", Font,
                    new Rectangle(12, Math.Max(36, Height / 2), Math.Max(1, Width - 24), 28), muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
            if (MaximumOffset == 0) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = MeetingVisuals.Rounded(Thumb, 3))
            using (var brush = new SolidBrush(_hover || _drag ? _hoverThumb : _thumb)) e.Graphics.FillPath(brush, path);
        }
    }

    internal sealed class MeetingWindowButton : Button
    {
        public int Kind;
        public Color HoverColor;
        private bool _hover;
        public MeetingWindowButton() { Margin = Padding.Empty; TabStop = false; FlatStyle = FlatStyle.Flat; Cursor = Cursors.Hand; }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(_hover ? (Kind == 2 ? Color.FromArgb(190, 53, 53) : HoverColor) : BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int x = Width / 2, y = Height / 2;
            using (var pen = new Pen(_hover && Kind == 2 ? Color.White : ForeColor, 1.3f))
            {
                if (Kind == 0) e.Graphics.DrawLine(pen, x - 5, y + 3, x + 5, y + 3);
                else if (Kind == 1) e.Graphics.DrawRectangle(pen, x - 5, y - 5, 10, 10);
                else { e.Graphics.DrawLine(pen, x - 4, y - 4, x + 4, y + 4); e.Graphics.DrawLine(pen, x + 4, y - 4, x - 4, y + 4); }
            }
        }
    }

    internal static class MeetingVisuals
    {
        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var path = new GraphicsPath(); int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) return path;
            path.AddArc(r.Left, r.Top, d, d, 180, 90); path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure(); return path;
        }
    }

    internal sealed class MeetingWindowPicker : Control
    {
        private readonly Color _surface, _border, _muted, _accent, _hoverBack;
        private readonly ToolStripDropDownMenu _menu;
        private readonly bool _audioMode;
        private bool _hover;
        private int _selectedIndex = -1;
        public List<TeamsWindow> Items { get; } = new List<TeamsWindow>();
        public List<LiveAudioDevice> AudioDevices { get; } = new List<LiveAudioDevice>();
        public TeamsWindow SelectedItem => _selectedIndex >= 0 && _selectedIndex < Items.Count ? Items[_selectedIndex] : null;
        public LiveAudioDevice SelectedAudioDevice => _selectedIndex >= 0 && _selectedIndex < AudioDevices.Count ? AudioDevices[_selectedIndex] : null;
        public event EventHandler SelectionChangeCommitted;
        private int ItemCount => _audioMode ? AudioDevices.Count : Items.Count;
        private string ItemTitle(int index) => _audioMode ? AudioDevices[index].Name : Items[index].Title;
        public int SelectedIndex
        {
            get { return _selectedIndex; }
            set { _selectedIndex = value >= 0 && value < ItemCount ? value : -1; Invalidate(); }
        }

        public MeetingWindowPicker(bool dark, bool audioMode = false)
        {
            _audioMode = audioMode;
            // Vẽ lại toàn bộ hộp khi resize để xóa viền/caret ở kích thước cũ.
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
            _surface = dark ? Color.FromArgb(35, 36, 43) : Color.FromArgb(249, 250, 252);
            _border = dark ? Color.FromArgb(72, 73, 85) : Color.FromArgb(216, 218, 225);
            _muted = dark ? Color.FromArgb(168, 174, 188) : Color.FromArgb(112, 119, 133);
            _accent = dark ? Color.FromArgb(232, 150, 74) : Color.FromArgb(30, 58, 138);
            _hoverBack = dark ? Color.FromArgb(58, 45, 35) : Color.FromArgb(237, 243, 252);
            ForeColor = dark ? Color.FromArgb(239, 242, 248) : Color.FromArgb(37, 44, 56);
            BackColor = dark ? Color.FromArgb(28, 31, 37) : Color.White;
            Font = new Font("Segoe UI", 9F);
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.ComboBox;
            AccessibleName = audioMode ? "Loa hoặc tai nghe Google Live" : "Cửa sổ cuộc họp Teams";
            _menu = new MeetingDropDown(_border, dark)
            {
                ShowImageMargin = false, ShowCheckMargin = false,
                BackColor = _surface, ForeColor = ForeColor,
                Padding = new Padding(5), Font = Font,
                Renderer = new MeetingMenuRenderer(_surface, _hoverBack, _accent, ForeColor, _muted, audioMode)
            };
            _menu.Closed += delegate { Invalidate(); };
            _menu.LayoutCompleted += delegate
            {
                // Menu native đo từng dòng theo chữ; kéo cả hàng ra đủ bề ngang hộp.
                foreach (ToolStripItem item in _menu.Items)
                {
                    int width = Math.Max(1, _menu.ClientSize.Width - item.Bounds.Left - _menu.Padding.Right);
                    if (item.Width == width) continue;
                    item.AutoSize = false;
                    item.Size = new Size(width, item.Height);
                }
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Rounded(new Rectangle(0, 0, Width - 1, Height - 1)))
            using (var brush = new SolidBrush(_surface))
            using (var pen = new Pen(Enabled && (_hover || Focused || _menu.Visible) ? _accent : _border))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
            bool hasSelection = SelectedIndex >= 0 && SelectedIndex < ItemCount;
            Color ink = Enabled && hasSelection ? ForeColor : _muted;
            int cy = Height / 2;
            float scale = e.Graphics.DpiX / 96F;
            using (var pen = new Pen(_muted, 1.5f))
            {
                if (_audioMode) DrawSpeaker(e.Graphics, pen, 12 * scale, cy, scale);
                else
                {
                    e.Graphics.DrawRectangle(pen, 12, cy - 5, 11, 10);
                    e.Graphics.DrawLines(pen, new[] { new Point(23, cy - 2), new Point(28, cy - 5), new Point(28, cy + 5), new Point(23, cy + 2) });
                }
                int direction = _menu.Visible ? -1 : 1;
                e.Graphics.DrawLines(pen, new[] { new Point(Width - 23, cy - 2 * direction), new Point(Width - 19, cy + 2 * direction), new Point(Width - 15, cy - 2 * direction) });
            }
            int textLeft = _audioMode ? (int)(39 * scale) : 37;
            string title = hasSelection ? ItemTitle(SelectedIndex) : _audioMode ? "Chọn loa hoặc tai nghe" : "Chọn cuộc họp Teams";
            TextRenderer.DrawText(e.Graphics, title, Font,
                new Rectangle(textLeft, 1, Math.Max(1, Width - textLeft - 35), Height - 2), ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }

        private void ShowMenu()
        {
            if (!Enabled) return;
            if (_menu.Visible) { _menu.Close(); return; }
            BuildMenu();
            _menu.Show(this, new Point(0, Height + Math.Max(5, Height / 6)));
            if (SelectedIndex >= 0 && SelectedIndex < _menu.Items.Count) _menu.Items[SelectedIndex].Select();
            Invalidate();
        }

        private void BuildMenu()
        {
            while (_menu.Items.Count > 0) { var item = _menu.Items[0]; _menu.Items.RemoveAt(0); item.Dispose(); }
            // Pixel theo DPI hiện tại; menu luôn rộng bằng hộp chọn, giới hạn trong màn hình.
            int width = Math.Min(Width, Screen.FromControl(this).WorkingArea.Width);
            int inset = Math.Max(8, Height / 5);
            _menu.Padding = new Padding(inset);
            _menu.MinimumSize = new Size(width, 0);
            _menu.MaximumSize = new Size(width, 0);
            if (ItemCount == 0)
                _menu.Items.Add(new ToolStripMenuItem(_audioMode ? "Chưa có loa hoặc tai nghe" : "Chưa có cuộc họp Teams")
                { Enabled = false, Padding = new Padding(inset, inset * 3, inset, inset * 3) });
            for (int i = 0; i < ItemCount; i++)
            {
                int index = i;
                var item = new ToolStripMenuItem(ItemTitle(i).Replace("&", "&&"))
                {
                    Padding = new Padding(inset, inset * 2, inset, inset * 2),
                    Checked = i == SelectedIndex,
                    Tag = _audioMode ? AudioDevices[i] : null,
                    ToolTipText = ItemTitle(i),
                    AccessibleName = ItemTitle(i)
                };
                item.Click += delegate { SelectedIndex = index; _menu.Close(); Focus(); SelectionChangeCommitted?.Invoke(this, EventArgs.Empty); };
                _menu.Items.Add(item);
            }
            _menu.PerformLayout();
        }

        protected override void OnClick(EventArgs e) { base.OnClick(e); Focus(); ShowMenu(); }
        protected override void OnSizeChanged(EventArgs e)
        {
            // Đóng popup đang neo theo vị trí cũ khi đổi kích thước cửa sổ.
            if (_menu != null && _menu.Visible) _menu.Close();
            base.OnSizeChanged(e);
        }
        protected override bool IsInputKey(Keys keyData)
        { return (keyData & Keys.KeyCode) == Keys.Down || (keyData & Keys.KeyCode) == Keys.Up || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter || e.KeyCode == Keys.Down || e.KeyCode == Keys.Up || (e.KeyCode == Keys.F4 && !e.Alt))
            { ShowMenu(); e.Handled = true; e.SuppressKeyPress = true; }
            base.OnKeyDown(e);
        }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { if (!Enabled) _menu.Close(); Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnVisibleChanged(EventArgs e) { if (!Visible && _menu != null) _menu.Close(); base.OnVisibleChanged(e); }
        protected override void Dispose(bool disposing) { if (disposing) _menu.Dispose(); base.Dispose(disposing); }

        private static void DrawSpeaker(Graphics graphics, Pen pen, float x, float cy, float scale)
        {
            graphics.DrawPolygon(pen, new[] { new PointF(x, cy - 3 * scale), new PointF(x + 4 * scale, cy - 3 * scale),
                new PointF(x + 9 * scale, cy - 7 * scale), new PointF(x + 9 * scale, cy + 7 * scale),
                new PointF(x + 4 * scale, cy + 3 * scale), new PointF(x, cy + 3 * scale) });
            graphics.DrawArc(pen, x + 8 * scale, cy - 5 * scale, 7 * scale, 10 * scale, -65, 130);
            graphics.DrawArc(pen, x + 8 * scale, cy - 8 * scale, 12 * scale, 16 * scale, -65, 130);
        }

        private static GraphicsPath Rounded(Rectangle r)
        {
            var path = new GraphicsPath(); const int d = 14;
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure(); return path;
        }

        internal sealed class MeetingDropDown : ToolStripDropDownMenu
        {
            private readonly Color _outline;
            private readonly bool _dark;
            public MeetingDropDown(Color outline, bool dark) { _outline = outline; _dark = dark; }
            protected override CreateParams CreateParams
            {
                // Khung DWM cho góc bo và bóng mịn; vùng nội dung vẫn do menu vẽ.
                get { var p = base.CreateParams; p.Style |= 0x00C00000 | 0x00040000; return p; }
            }
            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                try
                {
                    int round = 2, dark = _dark ? 1 : 0;
                    int color = _outline.R | (_outline.G << 8) | (_outline.B << 16);
                    DwmSetWindowAttribute(Handle, 33, ref round, 4);
                    DwmSetWindowAttribute(Handle, 20, ref dark, 4);
                    DwmSetWindowAttribute(Handle, 34, ref color, 4);
                }
                catch { } // Windows cũ giữ popup vuông, không cắt Region gây răng cưa.
            }
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x0083) { m.Result = IntPtr.Zero; return; }
                if (m.Msg == 0x0084) { m.Result = (IntPtr)1; return; }
                // Popup đã bỏ non-client; ảnh xem trước cũng không vẽ lại khung caption Windows.
                if (m.Msg == 0x0317) m.LParam = (IntPtr)(m.LParam.ToInt64() & ~2L);
                base.WndProc(ref m);
            }
            [DllImport("dwmapi.dll")]
            private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        }

        private sealed class MeetingMenuRenderer : ToolStripProfessionalRenderer
        {
            private readonly Color _back, _hover, _accent, _text, _muted;
            private readonly bool _audioMode;
            public MeetingMenuRenderer(Color back, Color hover, Color accent, Color text, Color muted, bool audioMode)
            { _back = back; _hover = hover; _accent = accent; _text = text; _muted = muted; _audioMode = audioMode; RoundedEdges = false; }
            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            { using (var brush = new SolidBrush(_back)) e.Graphics.FillRectangle(brush, e.AffectedBounds); }
            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            { } // Chỉ có viền ngoài DWM, không lồng thêm khung bên trong.
            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var item = e.Item as ToolStripMenuItem;
                bool active = e.Item.Enabled && (e.Item.Selected || (item != null && item.Checked));
                if (!active) return;
                // ToolStrip đo tên dài theo nội dung; giới hạn vùng vẽ trong popup thực tế.
                int width = Math.Min(e.Item.Width, e.ToolStrip.ClientSize.Width - e.Item.Bounds.Left);
                using (var path = Rounded(new Rectangle(5, 2, Math.Max(1, width - 11), e.Item.Height - 5)))
                using (var brush = new SolidBrush(_hover))
                { e.Graphics.FillPath(brush, path); }
            }
            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                var item = e.Item as ToolStripMenuItem;
                int width = Math.Min(e.Item.Width, e.ToolStrip.ClientSize.Width - e.Item.Bounds.Left);
                // Kích thước px theo DPI của graphics; hai cấp chữ và icon thay hộp nút disabled.
                float scale = e.Graphics.DpiX / 96F;
                int icon = (int)(32 * scale), left = (int)(16 * scale), gap = (int)(12 * scale);
                int cy = e.Item.Height / 2;
                var glyph = new Rectangle(left, cy - icon / 2, icon, icon);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = MeetingVisuals.Rounded(glyph, (int)(10 * scale)))
                using (var brush = new SolidBrush(_hover)) e.Graphics.FillPath(brush, path);
                using (var pen = new Pen(e.Item.Enabled ? _accent : _muted, 1.4F * scale))
                {
                    float x = glyph.Left + 8 * scale, y = cy - 5 * scale;
                    if (_audioMode) DrawSpeaker(e.Graphics, pen, glyph.Left + 6 * scale, cy, scale);
                    else
                    {
                        e.Graphics.DrawRectangle(pen, x, y, 11 * scale, 10 * scale);
                        e.Graphics.DrawLines(pen, new[] { new PointF(x + 11 * scale, y + 3 * scale),
                            new PointF(x + 16 * scale, y), new PointF(x + 16 * scale, y + 10 * scale), new PointF(x + 11 * scale, y + 7 * scale) });
                    }
                }
                bool selected = item != null && item.Checked;
                int textLeft = glyph.Right + gap;
                int textWidth = Math.Max(1, width - textLeft - (int)(38 * scale));
                var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
                using (var titleFont = new Font(e.TextFont, FontStyle.Bold))
                    TextRenderer.DrawText(e.Graphics, e.Text, titleFont,
                        new Rectangle(textLeft, cy - (int)(21 * scale), textWidth, (int)(23 * scale)), _text, flags);
                string detail = !e.Item.Enabled ? "Mở cuộc họp, rồi chọn “Tìm lại Teams”." : selected ? "Cuộc họp đang chọn" : "Cuộc họp Teams";
                if (_audioMode)
                {
                    var device = e.Item.Tag as LiveAudioDevice;
                    detail = device == null ? "Kết nối thiết bị, rồi chọn “Tìm thiết bị”."
                        : device.TeamsActive ? "Teams đang sử dụng" : device.DefaultCommunications ? "Mặc định cuộc gọi"
                        : device.DefaultMultimedia ? "Mặc định phát âm thanh" : "Đầu ra âm thanh";
                    if (selected) detail += " · Đang chọn";
                }
                TextRenderer.DrawText(e.Graphics, detail, e.TextFont,
                    new Rectangle(textLeft, cy + (int)(2 * scale), textWidth, (int)(21 * scale)), _muted, flags);
                if (selected)
                    using (var pen = new Pen(_accent, 1.8F * scale))
                        e.Graphics.DrawLines(pen, new[] { new PointF(width - 28 * scale, cy),
                            new PointF(width - 24 * scale, cy + 4 * scale), new PointF(width - 17 * scale, cy - 4 * scale) });
            }
        }
    }

    internal sealed class MeetingAvatar : Label
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? BackColor : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(BackColor)) e.Graphics.FillEllipse(brush, 0, 0, Width - 1, Height - 1);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    internal sealed class MeetingButton : Button
    {
        public bool ProviderStyle { get; set; }
        public bool MenuExpanded { get; set; }
        private bool _hover;
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (ProviderStyle) { PaintProvider(e); return; }
            e.Graphics.Clear(Parent == null ? BackColor : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = Enabled && _hover ? ControlPaint.Light(BackColor, .12f) : BackColor;
            using (var path = MeetingVisuals.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 8))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(Focused ? ForeColor : FlatAppearance.BorderColor))
            { e.Graphics.FillPath(brush, path); if (FlatAppearance.BorderSize > 0 || Focused) e.Graphics.DrawPath(pen, path); }
            Color muted = BackColor.GetBrightness() < 0.5f ? Color.FromArgb(155, 163, 175) : Color.FromArgb(113, 118, 126);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? ForeColor : muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        private void PaintProvider(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent == null ? BackColor : Parent.BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = DeviceDpi / 96F;
            Color ink = Enabled ? ForeColor : Color.FromArgb(128, ForeColor);
            Color fill = Enabled && (_hover || MenuExpanded) ? ControlPaint.Light(BackColor, .08F) : BackColor;
            using (var path = MeetingVisuals.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), (int)(10 * s)))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(Focused || MenuExpanded ? ForeColor : FlatAppearance.BorderColor))
            { g.FillPath(brush, path); g.DrawPath(pen, path); }
            float cy = Height / 2F;
            using (var brush = new SolidBrush(ink)) g.FillEllipse(brush, 14 * s, cy - 3 * s, 6 * s, 6 * s);
            using (var titleFont = new Font(Font, FontStyle.Bold))
                TextRenderer.DrawText(g, Text, titleFont, new Rectangle((int)(29 * s), 0, Math.Max(1, Width - (int)(62 * s)), Height),
                    ink, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            using (var pen = new Pen(ink, 1.4F * s))
            {
                float x = Width - 21 * s, direction = MenuExpanded ? -1 : 1;
                g.DrawLines(pen, new[] { new PointF(x - 4 * s, cy - direction * 2 * s),
                    new PointF(x, cy + direction * 2 * s), new PointF(x + 4 * s, cy - direction * 2 * s) });
            }
        }
    }

    internal sealed class MeetingProviderMenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly bool _dark;
        public MeetingProviderMenuRenderer(bool dark) { _dark = dark; RoundedEdges = false; }
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { e.Graphics.Clear(e.ToolStrip.BackColor); }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        { } // Dùng viền DWM như popup chọn cuộc họp Teams.
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var item = e.Item as ToolStripMenuItem;
            if (!e.Item.Selected && !(item?.Checked ?? false)) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = MeetingVisuals.Rounded(new Rectangle(0, 1, e.Item.Width - 1, e.Item.Height - 2), 7))
            using (var brush = new SolidBrush(_dark ? Color.FromArgb(40, 48, 59) : Color.FromArgb(237, 243, 251)))
                e.Graphics.FillPath(brush, path);
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var item = (ToolStripMenuItem)e.Item;
            bool google = Equals(item.Tag, "GoogleLive") || (item.Tag is MeetingTranslationProvider && (MeetingTranslationProvider)item.Tag == MeetingTranslationProvider.Google);
            float s = e.Graphics.DpiX / 96F;
            int left = (int)(10 * s), width = item.Width - (int)(36 * s);
            Color accent = google ? (_dark ? Color.FromArgb(133, 195, 252) : Color.FromArgb(34, 103, 173))
                : (_dark ? Color.FromArgb(134, 209, 166) : Color.FromArgb(39, 113, 76));
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            using (var bold = new Font(e.TextFont, FontStyle.Bold))
                TextRenderer.DrawText(e.Graphics, item.Text, bold,
                    new Rectangle(left, 0, width, item.Height), accent, flags);
            if (item.Checked)
                using (var pen = new Pen(accent, 1.8F * s))
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    float x = item.Width - 15 * s, y = item.Height / 2F;
                    e.Graphics.DrawLines(pen, new[] { new PointF(x - 5 * s, y), new PointF(x - s, y + 4 * s), new PointF(x + 6 * s, y - 4 * s) });
                }
        }
    }

    internal sealed class RoundPanel : Panel
    {
        private readonly int _radius;
        private readonly Color _fill;
        private readonly Color _border;

        public RoundPanel(int radius, Color fill, Color border)
        {
            _radius = radius;
            _fill = fill;
            _border = border;
            BackColor = fill;
            DoubleBuffered = true;
            // Khung bo phải xóa mép cũ trên toàn bộ nền khi kéo rộng/maximize.
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(Parent == null ? _fill : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = CreateRoundRect(ClientRectangle, _radius))
            using (SolidBrush brush = new SolidBrush(_fill))
            using (Pen pen = new Pen(_border, 1))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
        }

        private static GraphicsPath CreateRoundRect(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d - 1, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d - 1, bounds.Bottom - d - 1, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d - 1, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class BadgeLabel : Label
    {
        public BadgeLabel(string text, Color back, Color fore)
        {
            Text = text;
            Font = new Font("Segoe UI", 7.5F, FontStyle.Bold);
            BackColor = back;
            ForeColor = fore;
            Padding = new Padding(6, 2, 6, 2);
            AutoSize = true;
        }
    }
}
