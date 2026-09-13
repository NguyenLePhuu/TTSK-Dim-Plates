using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using TTSK_AutoDim_Plates.Updater;

namespace TTSK_AutoDim_Plates
{
    public partial class MainForm
    {
        private UpdateManager _updateManager;
        private GitHubReleaseInfo _pendingUpdateRelease;
        private bool _updaterHandoffClosing = false;
        private ToolTip _versionToolTip;
        private readonly System.Threading.CancellationTokenSource _updaterLifetime = new System.Threading.CancellationTokenSource();
        private bool _updateCheckRunning;
        private DownloadUpdateButton _downloadUpdateButton;
        private bool _updateDialogOpen;

        /// <summary>
        /// Kiểm tra xem ứng dụng có đang rảnh rỗi để có thể áp dụng cập nhật an toàn hay không.
        /// Đảm bảo tất cả 5 cờ bận (busy flags) của ứng dụng đều đang tắt.
        /// </summary>
        public bool CanApplyUpdateNow()
        {
            if (_isBatchRunning) return false;
            if (_pdfCommandRunning) return false;
            if (_gridVisibilityMacroRunning) return false;
            if (_fitAndCleanupRunning) return false;
            if (_snapshotExportRunning) return false;
            return true;
        }

        /// <summary>
        /// Khởi tạo hệ thống tự động cập nhật, đăng ký sự kiện và kiểm tra phiên bản ngầm.
        /// </summary>
        private void InitializeUpdater()
        {
            try
            {
                _updateManager = new UpdateManager(Application.StartupPath);
            }
            catch (Exception)
            {
                // Không làm gián đoạn khởi động ứng dụng nếu có lỗi đọc thư mục updater
                return;
            }

            _versionToolTip = new ToolTip
            {
                AutoPopDelay = 8000,
                InitialDelay = 500,
                ReshowDelay = 200,
                ShowAlways = true
            };
            _downloadUpdateButton = new DownloadUpdateButton { Anchor = AnchorStyles.Right | AnchorStyles.Top,
                Size = new Size(32, 30), AccessibleName = "Tải bản cập nhật mới", TabStop = true };
            mainFooter.Controls.Add(_downloadUpdateButton);
            _downloadUpdateButton.Location = new Point(mainFooter.ClientSize.Width - 40, 5);
            _downloadUpdateButton.BringToFront();
            _downloadUpdateButton.Click += async (s, e) =>
            {
                if (_updateDialogOpen || _updateCheckRunning) return;
                await CheckForUpdatesManualAsync(true);
            };
            _versionToolTip.SetToolTip(_downloadUpdateButton, "Kiểm tra cập nhật phần mềm");
            UpdateVersionLabelTheme();

            if (mainVersionLabel != null)
            {
                mainVersionLabel.Cursor = Cursors.Hand;
                mainVersionLabel.AutoEllipsis = true;
                mainVersionLabel.Text = _updateManager.CurrentVersion.ToDisplayString();
                _versionToolTip.SetToolTip(mainVersionLabel, string.Format("Phiên bản hiện tại: {0}\r\nBấm vào để kiểm tra cập nhật.", _updateManager.CurrentVersion.ToDisplayString()));

                mainVersionLabel.Click += async (s, e) => await CheckForUpdatesManualAsync();
            }

            // Đọc trạng thái cache để hiển thị indicator nếu đã biết có bản mới
            try
            {
                UpdateStateCache cached = _updateManager.LoadCachedState();
                if (cached != null && cached.HasUpdate && UpdateVersion.TryParse(cached.LatestVersion, out UpdateVersion cachedVer) && cachedVer > _updateManager.CurrentVersion)
                {
                    _pendingUpdateRelease = new GitHubReleaseInfo
                    {
                        TagName = cached.LatestTagName,
                        Version = cachedVer,
                        Body = cached.ReleaseNotes,
                        HtmlUrl = cached.HtmlUrl,
                        ZipDownloadUrl = cached.ZipDownloadUrl,
                        ZipSizeBytes = cached.ZipSizeBytes,
                        Sha256DownloadUrl = cached.Sha256DownloadUrl,
                        Sha256SizeBytes = cached.Sha256SizeBytes
                    };
                    UpdateVersionLabelIndicator();
                }
            }
            catch { }

            // Đăng ký kiểm tra cập nhật ngầm sau khi cửa sổ đã hiển thị (Shown)
            this.Shown += (s, e) =>
            {
                string completed = _updateManager.CheckSuccessMarker();
                if (completed != null) MessageBox.Show(this, "Đã cập nhật TTSK lên v" + completed, "TTSK Dim Plates");
                Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(2000, _updaterLifetime.Token);
                        _updateManager.CleanupCompletedSessions();
                        GitHubReleaseInfo release = await _updateManager.CheckForUpdatesAsync(true, _updaterLifetime.Token);
                        if (!_updaterLifetime.IsCancellationRequested)
                        {
                            if (!IsDisposed && IsHandleCreated) this.BeginInvoke(new Action(() =>
                            {
                                if (IsDisposed) return;
                                _pendingUpdateRelease = release != null && release.Version > _updateManager.CurrentVersion ? release : null;
                                UpdateVersionLabelIndicator();
                            }));
                        }
                    }
                    catch (Exception ex)
                    {
                        UpdateSecurity.Log(Application.StartupPath, "Automatic check: " + ex.Message);
                    }
                });
            };
            FormClosed += (s, e) => { _updaterLifetime.Cancel(); if (_versionToolTip != null) _versionToolTip.Dispose(); };
        }

        /// <summary>
        /// Cập nhật hiển thị nhãn phiên bản khi có bản cập nhật mới (ví dụ: v1.0.0 ↑).
        /// </summary>
        private void UpdateVersionLabelIndicator()
        {
            if (mainVersionLabel == null || IsDisposed) return;

            string curVerStr = _updateManager != null ? _updateManager.CurrentVersion.ToDisplayString() : "v1.0.0";
            if (_pendingUpdateRelease != null)
            {
                mainVersionLabel.Text = curVerStr;
                string newVerStr = _pendingUpdateRelease.Version != null ? _pendingUpdateRelease.Version.ToDisplayString() : _pendingUpdateRelease.TagName;
                _versionToolTip.SetToolTip(mainVersionLabel, string.Format("Có bản cập nhật mới: {0}!\r\nBấm vào đây để xem nội dung và tải về.", newVerStr));
            }
            else
            {
                mainVersionLabel.Text = curVerStr;
                _versionToolTip.SetToolTip(mainVersionLabel, string.Format("Phiên bản hiện tại: {0}\r\nBấm vào để kiểm tra cập nhật.", curVerStr));
            }

            UpdateVersionLabelTheme();
            if (_downloadUpdateButton != null)
            {
                _downloadUpdateButton.Available = _pendingUpdateRelease != null;
                _downloadUpdateButton.Invalidate();
                _versionToolTip.SetToolTip(_downloadUpdateButton, _pendingUpdateRelease == null ? "Đang dùng bản mới nhất • Bấm để kiểm tra lại" :
                    "Có bản " + _pendingUpdateRelease.Version.ToDisplayString() + " • Bấm một lần để tải, cập nhật và khởi động lại");
            }
        }

        /// <summary>
        /// Hook cập nhật lại màu sắc nhãn phiên bản theo theme hiện tại mà không làm mất trạng thái indicator.
        /// </summary>
        public void UpdateVersionLabelTheme()
        {
            if (_downloadUpdateButton != null)
            {
                _downloadUpdateButton.DarkMode = _darkMode;
                _downloadUpdateButton.ForeColor = _darkMode ? PrimaryButtonColor : Color.FromArgb(24, 75, 164);
                _downloadUpdateButton.Invalidate();
            }
            if (mainVersionLabel == null || mainHeaderActions == null) return;

            mainVersionLabel.BackColor = mainHeaderActions.BackColor;
            if (_pendingUpdateRelease != null)
            {
                // Khi có cập nhật mới, làm nổi bật màu nhãn phiên bản
                mainVersionLabel.ForeColor = _darkMode ? PrimaryButtonColor : Color.FromArgb(24, 75, 164);
            }
            else
            {
                mainVersionLabel.ForeColor = _darkMode ? PrimaryButtonColor : Color.FromArgb(65, 85, 112);
            }
        }

        /// <summary>
        /// Kiểm tra cập nhật thủ công khi người dùng click vào nhãn phiên bản.
        /// </summary>
        private async Task CheckForUpdatesManualAsync(bool startImmediately = false)
        {
            if (mainVersionLabel == null || _updateCheckRunning || _updaterHandoffClosing) return;
            _updateCheckRunning = true;

            string oldText = mainVersionLabel.Text;
            mainVersionLabel.Text = "Checking...";
            mainVersionLabel.Enabled = false;

            try
            {
                GitHubReleaseInfo release = await _updateManager.CheckForUpdatesAsync(true, _updaterLifetime.Token);
                if (IsDisposed) return;
                if (release != null && release.Version > _updateManager.CurrentVersion)
                {
                    _pendingUpdateRelease = release;
                    UpdateVersionLabelIndicator();
                    ShowUpdateDialog(release, startImmediately);
                }
                else
                {
                    _pendingUpdateRelease = null;
                    UpdateVersionLabelIndicator();
                    MessageBox.Show(
                        string.Format("Bạn đang sử dụng phiên bản mới nhất ({0}).\r\nChưa có bản cập nhật mới trên hệ thống.", _updateManager.CurrentVersion.ToDisplayString()),
                        "Kiểm tra cập nhật",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                }
            }
            catch (Exception ex)
            {
                if (IsDisposed) return;
                UpdateVersionLabelIndicator();
                MessageBox.Show(
                    string.Format("Kiểm tra cập nhật thất bại: {0}\r\nHãy kiểm tra kết nối mạng Internet hoặc truy cập trực tiếp trang GitHub Release.", ex.Message),
                    "Lỗi kết nối cập nhật",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }
            finally
            {
                _updateCheckRunning = false;
                if (!IsDisposed && mainVersionLabel != null)
                {
                    mainVersionLabel.Enabled = true;
                }
            }
        }

        /// <summary>
        /// Mở hộp thoại thông tin cập nhật cho người dùng xem và lựa chọn.
        /// </summary>
        private void ShowUpdateDialog(GitHubReleaseInfo release, bool startImmediately = false)
        {
            if (_updateDialogOpen || IsDisposed) return;
            _updateDialogOpen = true;
            try
            {
            using (var dlg = new UpdateDialog(
                release,
                _updateManager.CurrentVersion,
                _darkMode,
                () => CanApplyUpdateNow(),
                async (rel, progress, ct) => await _updateManager.DownloadAndPrepareStagingAsync(rel, progress, ct),
                (stagingDir, rel) => ApplyUpdateAndHandoff(stagingDir, rel)
            ))
            {
                if (startImmediately) dlg.StartAutomatically = true;
                dlg.ShowDialog(this);
            }
            }
            finally { _updateDialogOpen = false; }
        }

        private sealed class DownloadUpdateButton : Button
        {
            public bool Available { get; set; }
            public bool DarkMode { get; set; }
            private bool _hover;
            private bool _pressed;
            public DownloadUpdateButton()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand;
            }
            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnMouseDown(MouseEventArgs e) { _pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
            protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
            protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { _pressed = true; Invalidate(); } base.OnKeyDown(e); }
            protected override void OnKeyUp(KeyEventArgs e) { _pressed = false; Invalidate(); base.OnKeyUp(e); }
            protected override void OnPaint(PaintEventArgs e)
            {
                // Always use the live parent surface: the footer changes color after the theme hook.
                e.Graphics.Clear(Parent == null ? BackColor : Parent.BackColor);
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                float scale = Math.Min(Width / 32f, Height / 30f);
                e.Graphics.ScaleTransform(scale, scale);
                Color ink = Available ? (DarkMode ? Color.FromArgb(201, 122, 64) : Color.White) :
                    (DarkMode ? Color.FromArgb(201, 122, 64) : Color.FromArgb(61, 88, 123));
                if (Available || _hover)
                {
                    Color fill = DarkMode ? Color.FromArgb(37, 30, 25) : Color.FromArgb(28, 80, 170);
                    if (_hover) fill = DarkMode ? Color.FromArgb(57, 40, 28) : Color.FromArgb(38, 104, 206);
                    if (_pressed) fill = DarkMode ? Color.FromArgb(27, 23, 20) : Color.FromArgb(19, 58, 130);
                    using (var shape = new System.Drawing.Drawing2D.GraphicsPath())
                    {
                        shape.AddArc(1, 1, 10, 10, 180, 90); shape.AddArc(20, 1, 10, 10, 270, 90);
                        shape.AddArc(20, 19, 10, 10, 0, 90); shape.AddArc(1, 19, 10, 10, 90, 90); shape.CloseFigure();
                        using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(1, 1, 29, 28),
                            ControlPaint.Light(fill, _hover ? .14F : .07F), fill, 90F)) e.Graphics.FillPath(brush, shape);
                        using (var border = new Pen(DarkMode ? Color.FromArgb(_hover ? 201 : 109, _hover ? 122 : 73, _hover ? 64 : 47) : Color.FromArgb(66, 116, 200), 1F))
                            e.Graphics.DrawPath(border, shape);
                        using (var shine = new Pen(Color.FromArgb(DarkMode ? 55 : 65, Color.White), 1F))
                            e.Graphics.DrawLine(shine, 8, 2, 21, 2);
                    }
                    if (!Available && !DarkMode) ink = Color.White;
                }
                using (var pen = new Pen(ink, 2f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
                {
                    if (_pressed) e.Graphics.TranslateTransform(0, 1);
                    if (Available)
                    {
                        e.Graphics.DrawLine(pen, 15, 7, 15, 18);
                        e.Graphics.DrawLines(pen, new[] {new PointF(10, 13), new PointF(15, 18), new PointF(20, 13)});
                        e.Graphics.DrawLines(pen, new[] {new PointF(8, 19), new PointF(8, 23), new PointF(22, 23), new PointF(22, 19)});
                    }
                    else { e.Graphics.DrawArc(pen, 9, 8, 13, 13, 40, 285); e.Graphics.DrawLines(pen, new[] {new PointF(22, 7),new PointF(22, 12),new PointF(17, 12)}); }
                    if (_pressed) e.Graphics.TranslateTransform(0, -1);
                }
                if (Available)
                {
                    using (var rim = new SolidBrush(Parent == null ? BackColor : Parent.BackColor)) e.Graphics.FillEllipse(rim, 23, 0, 8, 8);
                    using (var brush = new SolidBrush(DarkMode ? Color.FromArgb(233, 159, 95) : Color.FromArgb(0, 153, 104))) e.Graphics.FillEllipse(brush, 24, 1, 6, 6);
                    using (var dot = new SolidBrush(Color.FromArgb(245, 248, 252))) e.Graphics.FillEllipse(dot, 25.5F, 2.5F, 2F, 2F);
                }
                e.Graphics.ResetTransform();
                if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(2, 2, Width-4, Height-4));
            }
        }

        /// <summary>
        /// Bàn giao và khởi động tiến trình worker để thay thế file ứng dụng.
        /// </summary>
        private async Task ApplyUpdateAndHandoff(string stagingDir, GitHubReleaseInfo release)
        {
            if (!CanApplyUpdateNow())
            {
                MessageBox.Show("Các tác vụ đang chạy trong ứng dụng. Hãy hoàn tất tác vụ trước khi cập nhật.", "TTSK Dim Plates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _updaterHandoffClosing = true;
            try
            {
                await _updateManager.LaunchWorkerAndHandoff(stagingDir, release, () =>
                {
                    if (!CanApplyUpdateNow() || IsDisposed) return false;
                    this.Close();
                    return IsDisposed || !Visible;
                });
            }
            finally { _updaterHandoffClosing = false; }
        }
    }
}
