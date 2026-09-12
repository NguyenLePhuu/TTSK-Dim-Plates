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
        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
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
        }

        private void InitializeUi()
        {
            Text = "Cập nhật TTSK Dim Plates";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(540, 420);
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
                Text = "Đã có phiên bản cập nhật mới!",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = accentColor,
                Location = new Point(20, 16),
                AutoSize = true
            };
            Controls.Add(lblHeaderTitle);

            // Version info
            string curVerStr = _currentVersion != null ? _currentVersion.ToDisplayString() : "v1.0.0";
            string newVerStr = _releaseInfo != null && _releaseInfo.Version != null ? _releaseInfo.Version.ToDisplayString() : _releaseInfo?.TagName;
            string sizeStr = _releaseInfo != null && _releaseInfo.ZipSizeBytes > 0 ? string.Format(" (~{0:F1} MB)", _releaseInfo.ZipSizeBytes / (1024.0 * 1024.0)) : "";

            lblVersionInfo = new Label
            {
                Text = string.Format("Phiên bản hiện tại:  {0}    ➔    Phiên bản mới:  {1}{2}", curVerStr, newVerStr, sizeStr),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
                ForeColor = fgColor,
                Location = new Point(22, 48),
                AutoSize = true
            };
            Controls.Add(lblVersionInfo);

            // Changelog Box
            Label lblNotesTitle = new Label
            {
                Text = "Nội dung thay đổi (Release Notes):",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = fgColor,
                Location = new Point(22, 78),
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
                Location = new Point(22, 102),
                Size = new Size(494, 190),
                Text = string.IsNullOrWhiteSpace(_releaseInfo?.Body) ? "Bản phát hành cập nhật và cải tiến tính năng." : _releaseInfo.Body.Trim()
            };
            Controls.Add(txtChangelog);

            // Progress bar
            progressBar = new ProgressBar
            {
                Location = new Point(22, 304),
                Size = new Size(494, 18),
                Style = ProgressBarStyle.Blocks,
                Visible = false
            };
            Controls.Add(progressBar);

            // Status message
            lblStatus = new Label
            {
                Text = "Sẵn sàng cập nhật.",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = _darkMode ? Color.FromArgb(160, 176, 198) : Color.FromArgb(100, 116, 139),
                Location = new Point(22, 328),
                Size = new Size(494, 20)
            };
            Controls.Add(lblStatus);

            // Buttons
            btnUpdate = new Button
            {
                Text = "Tải & Cập nhật",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                BackColor = accentColor,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(22, 360),
                Size = new Size(150, 36),
                Cursor = Cursors.Hand
            };
            btnUpdate.FlatAppearance.BorderSize = 0;
            btnUpdate.Click += async (s, e) => await StartDownloadAndApplyAsync();
            Controls.Add(btnUpdate);

            btnOpenReleasePage = new Button
            {
                Text = "Mở trang Release",
                Font = new Font("Segoe UI", 9F),
                BackColor = boxBgColor,
                ForeColor = fgColor,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(184, 360),
                Size = new Size(140, 36),
                Cursor = Cursors.Hand
            };
            btnOpenReleasePage.FlatAppearance.BorderColor = boxBorderColor;
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
                Location = new Point(396, 360),
                Size = new Size(120, 36),
                Cursor = Cursors.Hand
            };
            btnLater.FlatAppearance.BorderColor = boxBorderColor;
            btnLater.Click += (s, e) => Close();
            Controls.Add(btnLater);

            FormClosing += (s, e) =>
            {
                if (_isApplying && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; return; }
                if (_isDownloading && _cts != null)
                {
                    _cts.Cancel();
                }
            };
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
