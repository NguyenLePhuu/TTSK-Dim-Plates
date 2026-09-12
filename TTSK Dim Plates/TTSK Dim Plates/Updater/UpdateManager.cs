using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace TTSK_AutoDim_Plates.Updater
{
    public sealed class UpdateStateCache
    {
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
        private readonly TimeSpan _checkInterval = TimeSpan.FromHours(6);
        private readonly SemaphoreSlim _checkGate = new SemaphoreSlim(1, 1);

        public UpdateVersion CurrentVersion { get; }

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
        /// Kiểm tra cập nhật mới nhất từ GitHub.
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
            GitHubReleaseInfo latestRelease = await GitHubReleaseService.GetLatestReleaseAsync(ct).ConfigureAwait(false);
            if (latestRelease == null) throw new InvalidDataException("GitHub did not return a valid stable release with both assets.");

            var newCache = new UpdateStateCache
            {
                LastCheckUtc = DateTime.UtcNow.ToString("o")
            };

            if (latestRelease != null && latestRelease.Version != null && latestRelease.Version > CurrentVersion)
            {
                newCache.HasUpdate = true;
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
            if (release == null || release.Version == null || release.TagName != "v" + release.Version || release.Version <= CurrentVersion)
                throw new InvalidDataException("Invalid selected release.");
            GitHubReleaseService.ValidateExactAssetUrl(release.ZipDownloadUrl, release.TagName, GitHubReleaseService.ExpectedZipAssetName);
            GitHubReleaseService.ValidateExactAssetUrl(release.Sha256DownloadUrl, release.TagName, GitHubReleaseService.ExpectedSha256AssetName);
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
                    StatusMessage = "Đang tải mã băm SHA256..."
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
                throw new InvalidDataException("Downloaded metadata does not match the selected GitHub release.");

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
