using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TTSK_AutoDim_Plates.Updater
{
    /// <summary>
    /// Hộp thoại thông báo cập nhật phiên bản mới, hiển thị ghi chú phát hành, tiến độ tải và cho phép người dùng lựa chọn cập nhật.
    /// </summary>
    public sealed class UpdateDialog : Form
    {
        private readonly GitHubReleaseInfo _releaseInfo;
        private readonly UpdateVersion _currentVersion;
        private readonly bool _darkMode;
        private readonly Func<bool> _canApplyCheck;
        private readonly Func<GitHubReleaseInfo, IProgress<UpdateDownloadProgress>, CancellationToken, Task<string>> _downloadHandler;
        private readonly Func<string, GitHubReleaseInfo, Task> _applyHandler;

        private Label lblHeaderTitle;
        private Label lblVersionInfo;
        private TextBox txtChangelog;
        private ProgressBar progressBar;
        private Label lblStatus;
        private Button btnUpdate;
        private Button btnLater;
        private Button btnOpenReleasePage;
        private CancellationTokenSource _cts;
        private bool _isDownloading;
        private bool _isApplying;
        public bool StartAutomatically { get; set; }
        private bool _customDarkFrame;

        // Theme the native non-client area as well as the WinForms content.
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyNativeWindowTheme();
        }

        private void ApplyNativeWindowTheme()
        {
            try
            {
                int dark = _darkMode ? 1 : 0;
                // Windows 10 1809 used attribute 19; newer Windows uses 20.
                if (DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)) != 0)
                    DwmSetWindowAttribute(Handle, 19, ref dark, sizeof(int));

                // Windows 11: explicitly color border, caption and caption text.
                // Older Windows safely returns an unsupported-attribute HRESULT.
                int border = _darkMode ? ColorTranslator.ToWin32(Color.FromArgb(18, 22, 29)) : -1;
                int caption = _darkMode ? ColorTranslator.ToWin32(Color.FromArgb(24, 28, 36)) : -1;
                int text = _darkMode ? ColorTranslator.ToWin32(Color.FromArgb(240, 244, 250)) : -1;
                int borderResult = DwmSetWindowAttribute(Handle, 34, ref border, sizeof(int));
                DwmSetWindowAttribute(Handle, 35, ref caption, sizeof(int));
                DwmSetWindowAttribute(Handle, 36, ref text, sizeof(int));
                if (_darkMode && borderResult != 0 && !_customDarkFrame) InstallDarkFrame();
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        private void InstallDarkFrame()
        {
            _customDarkFrame = true;
            // Older Windows cannot color native borders. Use a dark draggable caption instead.
            foreach (Control control in Controls) control.Top += 36;
            ClientSize = new Size(ClientSize.Width, ClientSize.Height + 36);
            FormBorderStyle = FormBorderStyle.None;
            var caption = new Panel { BackColor = Color.FromArgb(12, 15, 20), Dock = DockStyle.Top, Height = 36 };
            var title = new Label { Text = "TTSK  •  Cập nhật phần mềm", ForeColor = Color.FromArgb(190, 200, 213),
                Location = new Point(14, 9), AutoSize = true };
            var close = new Button { Text = "×", Dock = DockStyle.Right, Width = 42, FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White, BackColor = caption.BackColor, TabStop = false };
            close.FlatAppearance.BorderSize = 0;
            close.FlatAppearance.MouseOverBackColor = Color.FromArgb(170, 40, 45);
            close.Click += (s, e) => Close();
            MouseEventHandler drag = (s, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); } };
            caption.MouseDown += drag; title.MouseDown += drag;
            caption.Controls.Add(title); caption.Controls.Add(close); Controls.Add(caption); caption.BringToFront();
            Paint += (s, e) => { using (var pen = new Pen(Color.Black)) e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width-1, ClientSize.Height-1); };
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplyNativeWindowTheme();
            if (StartAutomatically) await StartDownloadAndApplyAsync();
        }

        public UpdateDialog(
            GitHubReleaseInfo releaseInfo,
            UpdateVersion currentVersion,
            bool darkMode,
            Func<bool> canApplyCheck,
            Func<GitHubReleaseInfo, IProgress<UpdateDownloadProgress>, CancellationToken, Task<string>> downloadHandler,
            Func<string, GitHubReleaseInfo, Task> applyHandler)
        {
            _releaseInfo = releaseInfo;
            _currentVersion = currentVersion;
            _darkMode = darkMode;
            _canApplyCheck = canApplyCheck;
            _downloadHandler = downloadHandler;
            _applyHandler = applyHandler;

            InitializeUi();
            // Keep a consistent truly dark frame independently of Windows accent/theme settings.
            if (_darkMode && !_customDarkFrame) InstallDarkFrame();
        }

        private void InitializeUi()
        {
            Text = "Cập nhật TTSK Dim Plates";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(600, 510);
            Font = new Font("Segoe UI", 9F, FontStyle.Regular);

            Color bgColor = _darkMode ? Color.FromArgb(24, 28, 36) : Color.FromArgb(248, 250, 252);
            Color fgColor = _darkMode ? Color.FromArgb(240, 244, 250) : Color.FromArgb(15, 23, 42);
            Color accentColor = _darkMode ? Color.FromArgb(201, 122, 64) : Color.FromArgb(30, 58, 138);
            Color boxBgColor = _darkMode ? Color.FromArgb(32, 38, 48) : Color.White;
            Color boxBorderColor = _darkMode ? Color.FromArgb(60, 70, 85) : Color.FromArgb(220, 226, 235);

            BackColor = bgColor;
            ForeColor = fgColor;

            // Header Title
            lblHeaderTitle = new Label
            {
                Text = "Sẵn sàng cho bản mới",
                Font = new Font("Segoe UI", 23F, FontStyle.Bold),
                ForeColor = accentColor,
                Location = new Point(28, 42),
                AutoSize = true
            };
            Controls.Add(lblHeaderTitle);
            Controls.Add(new Label { Text = "TTSK  /  SOFTWARE UPDATE", ForeColor = accentColor,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold), Location = new Point(30, 20), AutoSize = true });
            Controls.Add(new Label { Text = "Một lần bấm. Phần còn lại để TTSK lo.", ForeColor = fgColor,
                Font = new Font("Segoe UI", 10F), Location = new Point(30, 91), AutoSize = true });

            // Version info
            string curVerStr = _currentVersion != null ? _currentVersion.ToDisplayString() : "v1.0.0";
            string newVerStr = _releaseInfo != null && _releaseInfo.Version != null ? _releaseInfo.Version.ToDisplayString() : _releaseInfo?.TagName;
            string sizeStr = _releaseInfo != null && _releaseInfo.ZipSizeBytes > 0 ? string.Format(" (~{0:F1} MB)", _releaseInfo.ZipSizeBytes / (1024.0 * 1024.0)) : "";

            lblVersionInfo = new Label
            {
                Text = string.Format("{0}     →     {1}    {2}", curVerStr, newVerStr, sizeStr),
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                ForeColor = fgColor,
                Location = new Point(28, 133),
                Size = new Size(544, 62),
                BackColor = boxBgColor,
                TextAlign = ContentAlignment.MiddleCenter
            };
            Controls.Add(lblVersionInfo);

            // Changelog Box
            Label lblNotesTitle = new Label
            {
                Text = "Có gì trong lần cập nhật này?",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = fgColor,
                Location = new Point(28, 217),
                AutoSize = true
            };
            Controls.Add(lblNotesTitle);

            txtChangelog = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = boxBgColor,
                ForeColor = fgColor,
                Location = new Point(42, 260),
                Size = new Size(516, 105),
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 10F),
                TabStop = false,
                Text = FormatReleaseNotes(_releaseInfo?.Body)
            };
            var notesSurface = new Panel { BackColor = boxBgColor, Location = new Point(28, 246), Size = new Size(544, 134) };
            Controls.Add(notesSurface);
            txtChangelog.Location = new Point(14, 14);
            notesSurface.Controls.Add(txtChangelog);

            // Progress bar
            progressBar = new ProgressBar
            {
                Location = new Point(28, 395),
                Size = new Size(544, 5),
                Style = ProgressBarStyle.Blocks,
                Visible = false
            };
            Controls.Add(progressBar);

            // Status message
            lblStatus = new Label
            {
                Text = "✓  Giữ nguyên cấu hình cá nhân của bạn",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = _darkMode ? Color.FromArgb(160, 176, 198) : Color.FromArgb(100, 116, 139),
                Location = new Point(28, 410),
                Size = new Size(544, 22)
            };
            Controls.Add(lblStatus);

            // Buttons
            btnUpdate = new Button
            {
                Text = "↓   Cập nhật ngay",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                BackColor = accentColor,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(350, 448),
                Size = new Size(222, 42),
                Cursor = Cursors.Hand
            };
            btnUpdate.FlatAppearance.BorderSize = 0;
            btnUpdate.Click += async (s, e) => await StartDownloadAndApplyAsync();
            Controls.Add(btnUpdate);

            btnOpenReleasePage = new Button
            {
                Text = "Chi tiết bản phát hành ↗",
                Font = new Font("Segoe UI", 9F),
                BackColor = boxBgColor,
                ForeColor = fgColor,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(28, 448),
                Size = new Size(184, 42),
                Cursor = Cursors.Hand
            };
            btnOpenReleasePage.FlatAppearance.BorderSize = 0;
            btnOpenReleasePage.Click += (s, e) =>
            {
                string url = "https://github.com/NguyenLePhuu/TTSK-Dim-Plates/releases/latest";
                try
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                catch { }
            };
            Controls.Add(btnOpenReleasePage);

            btnLater = new Button
            {
                Text = "Để sau",
                Font = new Font("Segoe UI", 9F),
                BackColor = boxBgColor,
                ForeColor = fgColor,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(228, 448),
                Size = new Size(106, 42),
                Cursor = Cursors.Hand
            };
            btnLater.FlatAppearance.BorderSize = 0;
            btnLater.Click += (s, e) => Close();
            Controls.Add(btnLater);
            ActiveControl = btnUpdate;
            AcceptButton = btnUpdate;

            FormClosing += (s, e) =>
            {
                if (_isApplying && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; return; }
                if (_isDownloading && _cts != null)
                {
                    _cts.Cancel();
                }
            };
        }

        public static string FormatReleaseNotes(string body)
        {
            string value = body ?? "";
            value = System.Text.RegularExpressions.Regex.Replace(value, @"<!--[\s\S]*?-->", "");
            value = System.Text.RegularExpressions.Regex.Replace(value, @"(?im)^.*Full Changelog.*$", "");
            value = System.Text.RegularExpressions.Regex.Replace(value, @"\[([^\]]+)\]\([^\)]+\)", "$1");
            value = System.Text.RegularExpressions.Regex.Replace(value, @"(?m)^\s{0,3}#{1,6}\s+", "");
            value = value.Replace("**", "").Replace("`", "");
            value = System.Text.RegularExpressions.Regex.Replace(value, @"(?m)^\s*[-*]\s+", "•  ").Trim();
            return value.Length == 0 ? "Chưa có ghi chú chi tiết cho phiên bản này.\r\n\r\nXem thêm thông tin trên trang bản phát hành." : value.Substring(0, Math.Min(12000, value.Length));
        }

        private async Task StartDownloadAndApplyAsync()
        {
            if (_isDownloading) return;

            // Kiểm tra busy gate trước khi tải
            if (_canApplyCheck != null && !_canApplyCheck())
            {
                MessageBox.Show(
                    "Các tác vụ Dim hoặc xuất bản vẽ đang chạy trong ứng dụng.\r\nHãy chờ tác vụ hoàn tất trước khi cập nhật.",
                    "TTSK Dim Plates",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return;
            }

            _isDownloading = true;
            btnUpdate.Enabled = false;
            btnOpenReleasePage.Enabled = false;
            btnLater.Text = "Hủy bỏ";
            progressBar.Visible = true;
            progressBar.Value = 0;

            _cts = new CancellationTokenSource();
            var progress = new Progress<UpdateDownloadProgress>(report =>
            {
                if (IsDisposed) return;
                progressBar.Value = Math.Max(0, Math.Min(100, report.ProgressPercentage));
                lblStatus.Text = report.StatusMessage ?? "Đang tải dữ liệu...";
            });

            string stagingDir = null;
            try
            {
                lblStatus.Text = "Đang kết nối tải bản cập nhật...";
                stagingDir = await _downloadHandler(_releaseInfo, progress, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed)
                {
                    lblStatus.Text = "Đã hủy bỏ tải cập nhật.";
                    btnUpdate.Enabled = true;
                    btnOpenReleasePage.Enabled = true;
                    btnLater.Text = "Để sau";
                    progressBar.Visible = false;
                    _isDownloading = false;
                }
                return;
            }
            catch (Exception ex)
            {
                if (!IsDisposed)
                {
                    MessageBox.Show(
                        string.Format("Tải bản cập nhật thất bại: {0}", ex.Message),
                        "Lỗi tải cập nhật",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                    lblStatus.Text = "Tải thất bại.";
                    btnUpdate.Enabled = true;
                    btnOpenReleasePage.Enabled = true;
                    btnLater.Text = "Để sau";
                    progressBar.Visible = false;
                    _isDownloading = false;
                }
                return;
            }

            if (IsDisposed || _cts.IsCancellationRequested) return;
            // Tải xong, kiểm tra busy gate một lần nữa trước khi bàn giao sang worker
            if (_canApplyCheck != null && !_canApplyCheck())
            {
                MessageBox.Show(
                    "Bản cập nhật đã tải xong và xác thực an toàn.\r\nTuy nhiên hiện tại ứng dụng đang bận tác vụ. Hãy hoàn tất tác vụ rồi bấm Cập nhật lại.",
                    "TTSK Dim Plates",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                btnUpdate.Enabled = true;
                btnOpenReleasePage.Enabled = true;
                btnLater.Text = "Để sau";
                _isDownloading = false;
                return;
            }

            lblStatus.Text = "Đang khởi động tiến trình cập nhật và đóng ứng dụng...";
            _isApplying = true;
            try { await _applyHandler(stagingDir, _releaseInfo); }
            catch (Exception ex) { if (!IsDisposed) MessageBox.Show(this, ex.Message, "Update failed"); }
            finally { _isApplying = false; }
            if (!IsDisposed) Close();
        }
    }
}
