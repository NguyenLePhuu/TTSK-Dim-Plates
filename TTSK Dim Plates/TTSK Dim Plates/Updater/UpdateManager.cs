// Điều phối cập nhật, giao diện tiến độ và worker áp dụng/khôi phục.
// LUỒNG: kiểm tra nguồn → chọn phiên bản → tải/xác minh → worker READY → ứng dụng thoát → transaction → khởi động lại.
// ĐIỂM SỬA: lịch kiểm tra trong UpdateManager; tiến độ/hủy trong UpdateDialog; giao dịch trong UpdateTransaction.cs.
// THAM SỐ: _checkInterval là chu kỳ giờ; cửa sổ thử lại 15 phút ở kiểm tra nền; mode worker là giao thức giữa tiến trình.
// LƯU Ý: chỉ áp dụng khi host cho phép; không chạy cập nhật giữa thao tác DIM.
// TRA CỨU: PROJECT_MAP.md ở gốc repo; chỉ mục kiểu/hàm và điểm chỉnh thông số.
// Mục lục:
//   Updater/UpdateDialog.cs
//   Updater/UpdateManager.cs
//   Updater/UpdateWorker.cs

#region UpdateDialog - Cửa sổ tiến độ cập nhật


namespace TTSK_AutoDim_Plates.Updater
{
    using System;
    using System.Diagnostics;
    using System.Drawing;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Windows.Forms;

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
                if (_releaseInfo?.Source == "GoogleDrive" && UpdateSources.IsDriveMediaUrl(_releaseInfo.ZipDownloadUrl))
                    url = "https://drive.google.com/file/d/" + new Uri(_releaseInfo.ZipDownloadUrl).Query.Substring(4).Split('&')[0] + "/view";
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
            btnUpdate.Text = "Đang cập nhật...";
            btnOpenReleasePage.Enabled = false;
            btnLater.Text = "Hủy bỏ";
            progressBar.Visible = true;
            progressBar.Value = 0;

            _cts = new CancellationTokenSource();
            var progress = new Progress<UpdateDownloadProgress>(report =>
            {
                if (IsDisposed || !_isDownloading) return;
                if (_releaseInfo.Version != null)
                {
                    string size = _releaseInfo.ZipSizeBytes > 0 ? string.Format(" (~{0:F1} MB)", _releaseInfo.ZipSizeBytes / (1024.0 * 1024.0)) : "";
                    lblVersionInfo.Text = string.Format("{0}     →     {1}    {2}", _currentVersion.ToDisplayString(), _releaseInfo.Version.ToDisplayString(), size);
                    txtChangelog.Text = FormatReleaseNotes(_releaseInfo.Body);
                }
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
                    btnUpdate.Text = "↓   Cập nhật ngay";
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
                    UpdateSecurity.Log(Application.StartupPath, "Prepare update failed: " + ex.Message);
                    lblStatus.Text = "Chưa tải được bản cập nhật. Bấm Thử lại khi bạn sẵn sàng.";
                    btnUpdate.Enabled = true;
                    btnUpdate.Text = "Thử lại";
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
                btnUpdate.Text = "↓   Cập nhật ngay";
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
#endregion

#region UpdateManager - Điều phối kiểm tra và áp dụng phiên bản


namespace TTSK_AutoDim_Plates.Updater
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Reflection;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web.Script.Serialization;
    using System.Windows.Forms;

    public sealed class UpdateStateCache
    {
        public string Source { get; set; }
        public string LastCheckUtc { get; set; }
        public string LastAttemptUtc { get; set; }
        public string LatestVersion { get; set; }
        public string LatestTagName { get; set; }
        public string ReleaseNotes { get; set; }
        public string HtmlUrl { get; set; }
        public string ZipDownloadUrl { get; set; }
        public long ZipSizeBytes { get; set; }
        public string Sha256DownloadUrl { get; set; }
        public long Sha256SizeBytes { get; set; }
        public bool HasUpdate { get; set; }
    }

    /// <summary>
    /// Điều phối viên trung tâm quản lý quá trình kiểm tra, tải về và chuyển giao cập nhật trong ứng dụng WinForms.
    /// </summary>
    public sealed class UpdateManager
    {
        private readonly string _targetDirectory;
        private readonly string _stateCachePath;
        // CHỈNH KIỂM TRA NỀN: chu kỳ giữa các lần kiểm tra thành công; không phải thời hạn tải gói.
        private readonly TimeSpan _checkInterval = TimeSpan.FromHours(6);
        private readonly SemaphoreSlim _checkGate = new SemaphoreSlim(1, 1);

        public UpdateVersion CurrentVersion { get; }
        public string LastSourceWarning { get; private set; }

        public UpdateManager(string targetDirectory)
        {
            _targetDirectory = Path.GetFullPath(targetDirectory).TrimEnd('\\', '/');
            string pathHash = UpdateSecurity.GetCanonicalPathHash(_targetDirectory);

            string stateDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TTSK Dim Plates",
                "Updater",
                pathHash
            );

            if (!Directory.Exists(stateDir))
            {
                Directory.CreateDirectory(stateDir);
            }

            _stateCachePath = Path.Combine(stateDir, "state.json");
            CurrentVersion = ResolveCurrentVersion(_targetDirectory);
        }

        /// <summary>
        /// Xác định phiên bản hiện tại của ứng dụng theo thứ tự ưu tiên:
        /// 1. release.json hợp lệ
        /// 2. AssemblyVersion chuẩn hóa (1.0.0.0 -> 1.0.0)
        /// 3. Fallback mặc định 1.0.0
        /// </summary>
        public static UpdateVersion ResolveCurrentVersion(string directory)
        {
            string releaseJsonPath = Path.Combine(directory, ReleaseMetadata.ReleaseJsonFileName);
            if (File.Exists(releaseJsonPath))
            {
                try
                {
                    var metadata = ReleaseMetadata.LoadFromFile(releaseJsonPath);
                    string metadataError;
                    if (metadata != null && metadata.Validate(out metadataError) && UpdateVersion.TryParse(metadata.version, out UpdateVersion metaVer))
                    {
                        return metaVer;
                    }
                }
                catch { }
            }

            try
            {
                Version asmVer = Assembly.GetExecutingAssembly().GetName().Version;
                if (asmVer != null)
                {
                    return new UpdateVersion(asmVer.Major, asmVer.Minor, Math.Max(0, asmVer.Build));
                }
            }
            catch { }

            return new UpdateVersion(1, 0, 0);
        }

        /// <summary>
        /// Đọc thông tin kiểm tra cập nhật gần nhất được lưu trong bộ nhớ đệm.
        /// </summary>
        public UpdateStateCache LoadCachedState()
        {
            if (!File.Exists(_stateCachePath))
            {
                return null;
            }

            try
            {
                string json = File.ReadAllText(_stateCachePath, Encoding.UTF8);
                var serializer = new JavaScriptSerializer();
                return serializer.Deserialize<UpdateStateCache>(json);
            }
            catch
            {
                return null;
            }
        }

        private void SaveStateCache(UpdateStateCache cache)
        {
            try
            {
                var serializer = new JavaScriptSerializer();
                string json = serializer.Serialize(cache);
                UpdateSecurity.AtomicWrite(_stateCachePath, json);
            }
            catch { }
        }

        /// <summary>
        /// Kiểm tra cập nhật mới nhất từ GitHub và Google Drive.
        /// Tự động sử dụng cache nếu chưa hết thời hạn 6 giờ (trừ khi người dùng kiểm tra thủ công).
        /// </summary>
        public async Task<GitHubReleaseInfo> CheckForUpdatesAsync(bool isManualCheck, CancellationToken ct = default(CancellationToken))
        {
            await _checkGate.WaitAsync(ct).ConfigureAwait(false);
            try { return await CheckCoreAsync(isManualCheck, ct).ConfigureAwait(false); }
            catch (Exception ex) { UpdateSecurity.Log(_targetDirectory, "Check failed: " + ex.Message); throw; }
            finally { _checkGate.Release(); }
        }

        private async Task<GitHubReleaseInfo> CheckCoreAsync(bool isManualCheck, CancellationToken ct)
        {
            UpdateStateCache cached = LoadCachedState();
            if (!isManualCheck && cached != null && !string.IsNullOrEmpty(cached.LastCheckUtc))
            {
                if (DateTime.TryParse(cached.LastCheckUtc, out DateTime lastCheck))
                {
                    if (DateTime.UtcNow >= lastCheck.ToUniversalTime() && DateTime.UtcNow - lastCheck.ToUniversalTime() < _checkInterval)
                    {
                        if (cached.HasUpdate && UpdateVersion.TryParse(cached.LatestVersion, out UpdateVersion cachedVer) && cachedVer > CurrentVersion)
                        {
                            return new GitHubReleaseInfo
                            {
                                Source = cached.Source ?? "GitHub",
                                TagName = cached.LatestTagName,
                                Version = cachedVer,
                                Body = cached.ReleaseNotes,
                                HtmlUrl = cached.HtmlUrl,
                                ZipDownloadUrl = cached.ZipDownloadUrl,
                                ZipSizeBytes = cached.ZipSizeBytes,
                                Sha256DownloadUrl = cached.Sha256DownloadUrl,
                                Sha256SizeBytes = cached.Sha256SizeBytes
                            };
                        }
                        return null; // Đã kiểm tra gần đây và không có bản mới
                    }
                }
            }

            DateTime lastAttempt;
            if (!isManualCheck && cached != null && DateTime.TryParse(cached.LastAttemptUtc, out lastAttempt) &&
                DateTime.UtcNow >= lastAttempt.ToUniversalTime() && DateTime.UtcNow - lastAttempt.ToUniversalTime() < TimeSpan.FromMinutes(15))
                throw new IOException("Automatic update check is in retry backoff.");
            var attempt = cached ?? new UpdateStateCache();
            attempt.LastAttemptUtc = DateTime.UtcNow.ToString("o");
            SaveStateCache(attempt);
            LastSourceWarning = null;
            GitHubReleaseInfo latestRelease = await UpdateSources.GetLatestAsync(message => { LastSourceWarning += message + " "; UpdateSecurity.Log(_targetDirectory, message); }, ct).ConfigureAwait(false);
            if (latestRelease == null) throw new InvalidDataException("No valid stable release from configured sources.");

            var newCache = new UpdateStateCache
            {
                LastCheckUtc = string.IsNullOrEmpty(LastSourceWarning) ? DateTime.UtcNow.ToString("o") : null,
                LastAttemptUtc = DateTime.UtcNow.ToString("o")
            };

            if (latestRelease != null && latestRelease.Version != null && latestRelease.Version > CurrentVersion)
            {
                newCache.HasUpdate = true;
                newCache.Source = latestRelease.Source;
                newCache.LatestVersion = latestRelease.Version.ToString();
                newCache.LatestTagName = latestRelease.TagName;
                newCache.ReleaseNotes = latestRelease.Body;
                newCache.HtmlUrl = latestRelease.HtmlUrl;
                newCache.ZipDownloadUrl = latestRelease.ZipDownloadUrl;
                newCache.ZipSizeBytes = latestRelease.ZipSizeBytes;
                newCache.Sha256DownloadUrl = latestRelease.Sha256DownloadUrl;
                newCache.Sha256SizeBytes = latestRelease.Sha256SizeBytes;
                SaveStateCache(newCache);
                return latestRelease;
            }

            newCache.HasUpdate = false;
            SaveStateCache(newCache);
            return null;
        }

        /// <summary>
        /// Tải về gói cập nhật, xác thực toàn vẹn và giải nén vào thư mục staging độc lập.
        /// </summary>
        public async Task<string> DownloadAndPrepareStagingAsync(GitHubReleaseInfo release, IProgress<UpdateDownloadProgress> progress, CancellationToken ct)
        {
            // Dialogs and persisted state may outlive a deleted/replaced release.
            // Resolve both sources again at the actual download boundary; never fall back to cached URLs.
            var latest = await CheckForUpdatesAsync(true, ct).ConfigureAwait(false);
            UpdateSources.ValidateDownloadSelection(release, latest, CurrentVersion);
            release.Source = latest.Source;
            release.TagName = latest.TagName;
            release.Version = latest.Version;
            release.Body = latest.Body;
            release.HtmlUrl = latest.HtmlUrl;
            release.ZipDownloadUrl = latest.ZipDownloadUrl;
            release.ZipSizeBytes = latest.ZipSizeBytes;
            release.Sha256DownloadUrl = latest.Sha256DownloadUrl;
            release.Sha256SizeBytes = latest.Sha256SizeBytes;
            if (release == null || release.Version == null || release.TagName != "v" + release.Version || release.Version <= CurrentVersion)
                throw new InvalidDataException("Invalid selected release.");
            UpdateSources.ValidateAssets(release);
            string sessionGuid = Guid.NewGuid().ToString("N");
            string sessionTempRoot = Path.Combine(
                Path.GetTempPath(),
                "TTSK-Dim-Plates-Update",
                release.TagName,
                sessionGuid
            );

            UpdateSecurity.AssertNoReparseAncestors(sessionTempRoot);
            if (Directory.Exists(sessionTempRoot)) throw new IOException("Update session already exists.");
            Directory.CreateDirectory(sessionTempRoot);
            UpdateSecurity.AtomicWrite(Path.Combine(sessionTempRoot, "session.owner"), _targetDirectory);

            string zipPath = Path.Combine(sessionTempRoot, GitHubReleaseService.ExpectedZipAssetName);
            string stagingDir = Path.Combine(sessionTempRoot, "staging");

            if (progress != null)
            {
                progress.Report(new UpdateDownloadProgress
                {
                    ProgressPercentage = 0,
                    StatusMessage = "Đang chuẩn bị bản cập nhật..."
                });
            }

            // Tải mã băm kiểm tra
            string expectedHash = await UpdateDownloader.DownloadExpectedChecksumAsync(release.Sha256DownloadUrl, ct).ConfigureAwait(false);

            // Tải file ZIP gói cập nhật
            await UpdateDownloader.DownloadZipPackageAsync(release.ZipDownloadUrl, zipPath, expectedHash, progress, ct).ConfigureAwait(false);

            if (progress != null)
            {
                progress.Report(new UpdateDownloadProgress
                {
                    ProgressPercentage = 95,
                    StatusMessage = "Đang giải nén và xác thực gói cập nhật..."
                });
            }

            // Giải nén và đối chiếu manifest
            ReleaseMetadata metadata;
            UpdateDownloader.ExtractAndValidatePackage(zipPath, stagingDir, out metadata);
            ct.ThrowIfCancellationRequested();
            if (metadata.version != release.Version.ToString() || metadata.tag != release.TagName)
                throw new InvalidDataException("Downloaded metadata does not match the selected release.");

            if (progress != null)
            {
                progress.Report(new UpdateDownloadProgress
                {
                    ProgressPercentage = 100,
                    StatusMessage = "Xác thực gói cập nhật thành công!"
                });
            }

            return stagingDir;
        }

        /// <summary>
        /// Bàn giao việc áp dụng cập nhật cho tiến trình Worker độc lập và yêu cầu đóng ứng dụng chính.
        /// </summary>
        public async Task<bool> LaunchWorkerAndHandoff(string stagingDir, GitHubReleaseInfo release, Func<bool> initiateCloseCallback)
        {
            var metadata = ReleaseMetadata.LoadFromFile(Path.Combine(stagingDir, "release.json"));
            string validationError;
            if (!UpdateChecksumManifest.ValidateDirectory(stagingDir, metadata, out validationError, true) || metadata.version != release.Version.ToString() || metadata.tag != release.TagName)
                throw new InvalidDataException("Staging changed or does not match selected release: " + validationError);
            string stagedExe = Path.Combine(stagingDir, "TTSK Dim Plates.exe");
            if (!File.Exists(stagedExe))
            {
                MessageBox.Show("Không tìm thấy file thực thi trong bản cập nhật đã tải.", "Lỗi cập nhật", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            string sessionGuid = Guid.NewGuid().ToString("N");
            string signalName = @"Local\TTSK_Update_Ready_" + sessionGuid;

            using (var readyEvent = new EventWaitHandle(false, EventResetMode.ManualReset, signalName))
            using (var proceedEvent = new EventWaitHandle(false, EventResetMode.ManualReset, @"Local\TTSK_Update_Proceed_" + sessionGuid))
            using (var cancelEvent = new EventWaitHandle(false, EventResetMode.ManualReset, @"Local\TTSK_Update_Cancel_" + sessionGuid))
            {
                int currentPid = Process.GetCurrentProcess().Id;
                long startTicks = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;

                string arguments = string.Format(
                    "{0} --target \"{1}\" --staging \"{2}\" --wait-pid {3} --parent-start-ticks {4} --version \"{5}\" --session \"{6}\" --ready-signal \"{7}\"",
                    UpdateWorker.ModeApplyUpdate,
                    _targetDirectory,
                    stagingDir,
                    currentPid,
                    startTicks,
                    release.Version != null ? release.Version.ToString() : release.TagName,
                    sessionGuid,
                    signalName
                );

                var startInfo = new ProcessStartInfo
                {
                    FileName = stagedExe,
                    Arguments = arguments,
                    WorkingDirectory = stagingDir,
                    UseShellExecute = false
                };

                Process workerProcess = null;
                try
                {
                    workerProcess = Process.Start(startInfo);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(string.Format("Không thể khởi động tiến trình cập nhật: {0}", ex.Message), "Lỗi cập nhật", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }

                // Chờ tín hiệu READY từ Worker (tối đa 10 giây)
                bool isReady = await Task.Run(() => readyEvent.WaitOne(10000));
                if (!isReady)
                {
                    MessageBox.Show("Tiến trình cập nhật không phản hồi tín hiệu sẵn sàng trong thời gian quy định. Đã hủy bỏ cập nhật.", "Lỗi cập nhật", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    try
                    {
                        if (workerProcess != null && !workerProcess.HasExited)
                        {
                            cancelEvent.Set();
                        }
                    }
                    catch { }
                    return false;
                }

                // Đã nhận READY -> kích hoạt đóng ứng dụng chính
                if (initiateCloseCallback != null)
                {
                    // Signal intent before closing; worker still requires a clean parent exit.
                    proceedEvent.Set();
                    if (!initiateCloseCallback()) { cancelEvent.Set(); return false; }
                }

                return true;
            }
        }

        /// <summary>
        /// Kiểm tra và lấy thông báo cập nhật thành công từ lần khởi chạy trước (nếu có).
        /// </summary>
        public string CheckSuccessMarker()
        {
            string pathHash = UpdateSecurity.GetCanonicalPathHash(_targetDirectory);
            string markerPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TTSK Dim Plates",
                "Updater",
                pathHash,
                "success.marker"
            );

            if (File.Exists(markerPath))
            {
                try
                {
                    string version = File.ReadAllText(markerPath, Encoding.UTF8).Trim();
                    File.Delete(markerPath);
                    return version == CurrentVersion.ToString() ? version : null;
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        public void CleanupCompletedSessions()
        {
            // A pending journal owns its backup and staging indefinitely until recovery completes.
            if (File.Exists(UpdateJournal.GetJournalPath(_targetDirectory))) return;
            try
            {
                string root = Path.Combine(Path.GetTempPath(), "TTSK-Dim-Plates-Update");
                if (Directory.Exists(root))
                {
                    UpdateSecurity.AssertNoReparseAncestors(root);
                    foreach (string tag in Directory.GetDirectories(root))
                    {
                        UpdateVersion ignored;
                        if (!UpdateVersion.TryParse(Path.GetFileName(tag), out ignored)) continue;
                        UpdateSecurity.AssertNoReparseAncestors(tag);
                        foreach (string session in Directory.GetDirectories(tag))
                        {
                            Guid id;
                            if (!Guid.TryParseExact(Path.GetFileName(session), "N", out id)) continue;
                            string owner = Path.Combine(session, "session.owner");
                            UpdateSecurity.AssertNoReparseAncestors(owner);
                            if (!File.Exists(owner) || new FileInfo(owner).Length > 4096 ||
                                File.ReadAllText(owner) != _targetDirectory || File.GetLastWriteTimeUtc(owner) > DateTime.UtcNow.AddDays(-7)) continue;
                            bool running = false;
                            foreach (var process in Process.GetProcessesByName("TTSK Dim Plates"))
                            {
                                using (process)
                                {
                                    try { if (process.MainModule.FileName.StartsWith(session + "\\", StringComparison.OrdinalIgnoreCase)) running = true; }
                                    catch { running = true; }
                                }
                            }
                            if (!running) DeleteOwnedTree(root, session);
                        }
                    }
                }
                string backups = Path.Combine(Path.GetDirectoryName(_stateCachePath), "backup");
                if (Directory.Exists(backups))
                {
                    var dirs = new DirectoryInfo(backups).GetDirectories();
                    Array.Sort(dirs, (a,b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                    for (int i=1; i<dirs.Length; i++)
                        if (dirs[i].LastWriteTimeUtc < DateTime.UtcNow.AddDays(-7)) DeleteOwnedTree(backups, dirs[i].FullName);
                }
            }
            catch (Exception ex) { UpdateSecurity.Log(_targetDirectory, "Cleanup deferred: " + ex.Message); }
        }

        private static void DeleteOwnedTree(string root, string candidate)
        {
            string full = Path.GetFullPath(candidate);
            if (!full.StartsWith(Path.GetFullPath(root).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) throw new IOException("Cleanup escaped its root.");
            var pending = new System.Collections.Generic.Stack<string>(); pending.Push(full);
            while (pending.Count > 0)
            {
                string dir = pending.Pop(); UpdateSecurity.AssertNoReparseAncestors(dir);
                foreach (string file in Directory.GetFiles(dir)) UpdateSecurity.AssertNoReparseAncestors(file);
                foreach (string child in Directory.GetDirectories(dir)) pending.Push(child);
            }
            Directory.Delete(full, true);
        }
    }
}
#endregion

#region UpdateWorker - Tiến trình áp dụng và phục hồi cập nhật


namespace TTSK_AutoDim_Plates.Updater
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Windows.Forms;

    public static class UpdateWorker
    {
        public const string ModeApplyUpdate = "--apply-update", ModeRecovery = "--recovery-update";
        public static bool SilentMode { get; set; }

        public static int Run(string[] args)
        {
            string target = null;
            try
            {
                if (args == null || args.Length == 0 || (args[0] != ModeApplyUpdate && args[0] != ModeRecovery)) throw new InvalidDataException("Invalid worker mode.");
                bool recovery = args[0] == ModeRecovery;
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                for (int i = 1; i < args.Length; i++)
                {
                    if (args[i] == "--silent") { SilentMode = true; continue; }
                    string key = args[i];
                    if (i + 1 >= args.Length || values.ContainsKey(key) ||
                        Array.IndexOf(new[] { "--target", "--staging", "--wait-pid", "--parent-start-ticks", "--version", "--session", "--ready-signal" }, key) < 0)
                        throw new InvalidDataException("Invalid/duplicate worker argument.");
                    values.Add(key, args[++i]);
                }
                target = Path.GetFullPath(values["--target"]).TrimEnd('\\');
                if (recovery)
                {
                    using (var gate = new UpdateLock(target))
                    {
                        if (!gate.TryAcquireUpdateLock(10000)) throw new IOException("Another update is running.");
                        int result = RecoverLocked(target);
                        if (result != 0) return result;
                        gate.Release();
                        return Restart(target);
                    }
                }
                string staging = Path.GetFullPath(values["--staging"]).TrimEnd('\\');
                string session = values["--session"];
                Guid guid;
                if (!Guid.TryParseExact(session, "N", out guid)) throw new InvalidDataException("Invalid session.");
                string readyName = @"Local\TTSK_Update_Ready_" + session;
                if (values["--ready-signal"] != readyName) throw new InvalidDataException("Invalid handshake.");
                int pid = int.Parse(values["--wait-pid"]);
                long ticks = long.Parse(values["--parent-start-ticks"]);
                if (pid <= 0 || ticks <= 0) throw new InvalidDataException("Parent identity is required.");
                using (var parent = Process.GetProcessById(pid))
                {
                    if (parent.StartTime.ToUniversalTime().Ticks != ticks ||
                        !Path.GetFullPath(parent.MainModule.FileName).Equals(Path.Combine(target, "TTSK Dim Plates.exe"), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Parent does not match this installation.");
                }
                if (!AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\').Equals(staging, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Apply worker must execute from staging.");
                var meta = ReleaseMetadata.LoadFromFile(Path.Combine(staging, "release.json"));
                if (meta == null || meta.version != values["--version"]) throw new InvalidDataException("Selected version does not match package.");
                using (var gate = new UpdateLock(target))
                using (var ready = EventWaitHandle.OpenExisting(readyName))
                using (var proceed = EventWaitHandle.OpenExisting(@"Local\TTSK_Update_Proceed_" + session))
                using (var cancel = EventWaitHandle.OpenExisting(@"Local\TTSK_Update_Cancel_" + session))
                {
                    if (!gate.TryAcquireUpdateLock(5000)) throw new IOException("Another updater owns this target.");
                    if (File.Exists(UpdateJournal.GetJournalPath(target))) throw new IOException("Recover the previous update first.");
                    AssertNoInstances(target, pid);
                    var transaction = new UpdateTransaction(target, staging, session, meta);
                    transaction.PreflightCheck();
                    UpdateSecurity.Log(target, "READY " + session + " version " + meta.version);
                    ready.Set();
                    if (WaitHandle.WaitAny(new WaitHandle[] { cancel, proceed }, 15000) != 1) throw new IOException("Handoff cancelled or timed out.");
                    if (!UpdateLock.WaitForParentExit(pid, ticks, 30000) || cancel.WaitOne(0)) throw new IOException("Parent did not exit cleanly.");
                    AssertNoInstances(target, 0);
                    transaction.Execute();
                    // Release before launch: otherwise the new application rejects its own startup.
                    gate.Release();
                    return Restart(target);
                }
            }
            catch (Exception ex)
            {
                if (target != null) UpdateSecurity.Log(target, ex.ToString());
                ShowError(ex.Message);
                return 1;
            }
        }

        private static int Restart(string target)
        {
            try
            {
                using (var process = Process.Start(new ProcessStartInfo(Path.Combine(target, "TTSK Dim Plates.exe")) { WorkingDirectory = target, UseShellExecute = false })) { }
                return 0;
            }
            catch (Exception ex)
            {
                UpdateSecurity.Log(target, "Runtime ready; restart failed: " + ex);
                ShowError("Runtime đã sẵn sàng nhưng khởi động lại thất bại. Mở TTSK thủ công. " + ex.Message);
                return 2;
            }
        }

        public static IDisposable RegisterInstance(string target)
        {
            string dir = Path.Combine(Path.GetDirectoryName(UpdateJournal.GetJournalPath(target)), "instances");
            UpdateSecurity.AssertNoReparseAncestors(dir);
            Directory.CreateDirectory(dir);
            using (var self = Process.GetCurrentProcess())
                return new FileStream(Path.Combine(dir, self.Id + "-" + self.StartTime.ToUniversalTime().Ticks + ".lease"),
                    FileMode.Create, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose);
        }

        internal static void AssertNoInstances(string target, int allowedParent)
        {
            string dir = Path.Combine(Path.GetDirectoryName(UpdateJournal.GetJournalPath(target)), "instances");
            if (Directory.Exists(dir))
                foreach (string file in Directory.GetFiles(dir, "*.lease"))
                {
                    if (allowedParent > 0 && Path.GetFileName(file).StartsWith(allowedParent + "-", StringComparison.Ordinal)) continue;
                    try { using (var probe = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } }
                    catch (IOException) { throw new IOException("Another instance is using this installation. Close it before updating."); }
                }
            var processes = UpdateLock.FindRunningInstancesForTarget(target, Process.GetCurrentProcess().Id);
            try { foreach (var process in processes) if (process.Id != allowedParent) throw new IOException("Close all other TTSK instances before updating."); }
            finally { foreach (var process in processes) process.Dispose(); }
        }

        // Called while admission is locked, before loading any Tekla types. Recovery must not overwrite the running target EXE.
        public static int StartRecoveryFromStaging(string target)
        {
            var journal = UpdateJournal.LoadFromFile(UpdateJournal.GetJournalPath(target));
            if (journal == null) return 1;
            if (journal.State == UpdateTransactionState.Committed || journal.State == UpdateTransactionState.RolledBack)
            { UpdateJournal.DeleteJournal(UpdateJournal.GetJournalPath(target)); return RestartAfterExit(target); }
            string staging = journal.StagingDirectory;
            string error;
            if (string.IsNullOrEmpty(staging) || UpdateTransaction.Overlaps(target, staging) ||
                !UpdateChecksumManifest.ValidateDirectory(staging, ReleaseMetadata.LoadFromFile(Path.Combine(staging, "release.json")), out error, true))
                throw new InvalidDataException("Recovery staging unavailable. Keep backup at " + journal.BackupDirectory + "; restore manually with app closed.");
            using (var p = Process.Start(new ProcessStartInfo(Path.Combine(staging, "TTSK Dim Plates.exe")) {
                Arguments = ModeRecovery + " --target \"" + target + "\"", WorkingDirectory = staging, UseShellExecute = false })) { }
            return 0;
        }

        private static int RestartAfterExit(string target)
        {
            // A completed journal only needed cleanup; do not recursively launch while holding admission.
            ShowError("Phiên cập nhật đã kết thúc. Vui lòng mở lại TTSK.");
            return 0;
        }

        public static int ExecuteRecovery(string targetDir)
        {
            try
            {
                using (var gate = new UpdateLock(targetDir))
                {
                    if (!gate.TryAcquireUpdateLock(5000)) throw new IOException("Recovery target is locked.");
                    return RecoverLocked(targetDir);
                }
            }
            catch (Exception ex) { UpdateSecurity.Log(targetDir, "Recovery: " + ex); ShowError(ex.Message); return 1; }
        }

        private static int RecoverLocked(string targetDir)
        {
            string path = UpdateJournal.GetJournalPath(targetDir);
            var journal = UpdateJournal.LoadFromFile(path);
            if (journal == null) return 0;
            if (!Path.GetFullPath(journal.TargetDirectory).Equals(Path.GetFullPath(targetDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Journal target mismatch.");
            // Startup admission exits promptly after launching us; allow only a bounded wait for it.
            var wait = Stopwatch.StartNew();
            while (true)
            {
                try { AssertNoInstances(targetDir, 0); break; }
                catch (IOException) { if (wait.ElapsedMilliseconds >= 5000) throw; Thread.Sleep(100); }
            }
            UpdateTransaction.Rollback(journal);
            UpdateJournal.DeleteJournal(path);
            return 0;
        }

        private static void ShowError(string message)
        {
            Console.Error.WriteLine(message);
            if (!SilentMode) MessageBox.Show(message, "Cập nhật TTSK Dim Plates", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
#endregion
