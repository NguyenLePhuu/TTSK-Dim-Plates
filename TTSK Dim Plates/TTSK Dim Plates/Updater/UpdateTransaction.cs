using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TTSK_AutoDim_Plates.Updater
{
    /// <summary>
    /// Thực hiện giao dịch thay thế file an toàn với kiểm tra trước (preflight), sao lưu (backup), xác thực (verify) và hoàn tác toàn diện (rollback).
    /// </summary>
    public sealed class UpdateTransaction
    {
        private readonly string _targetDirectory;
        private readonly string _stagingDirectory;
        private readonly string _sessionGuid;
        private readonly ReleaseMetadata _newMetadata;
        private readonly Dictionary<string, string> _newChecksums;
        private readonly string _journalPath;
        private readonly string _backupDirectory;

        public UpdateTransaction(string targetDirectory, string stagingDirectory, string sessionGuid, ReleaseMetadata newMetadata)
        {
            _targetDirectory = Path.GetFullPath(targetDirectory).TrimEnd('\\', '/');
            _stagingDirectory = Path.GetFullPath(stagingDirectory).TrimEnd('\\', '/');
            _sessionGuid = sessionGuid;
            if (!UpdateSecurity.IsSafeRelativePath(sessionGuid) || sessionGuid.IndexOfAny(new[] { '/', '\\' }) >= 0)
                throw new InvalidDataException("Invalid session ID.");
            _newMetadata = newMetadata;

            string csvPath = Path.Combine(_stagingDirectory, ReleaseMetadata.Sha256CsvFileName);
            _newChecksums = UpdateChecksumManifest.ReadChecksums(csvPath);

            _journalPath = UpdateJournal.GetJournalPath(_targetDirectory);

            string pathHash = UpdateSecurity.GetCanonicalPathHash(_targetDirectory);
            _backupDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TTSK Dim Plates",
                "Updater",
                pathHash,
                "backup",
                _sessionGuid
            );
        }

        /// <summary>
        /// Kiểm tra quyền ghi và dung lượng đĩa trước khi can thiệp vào file ứng dụng.
        /// </summary>
        public void PreflightCheck()
        {
            UpdateSecurity.AssertNoReparseAncestors(_targetDirectory);
            UpdateSecurity.AssertNoReparseAncestors(_stagingDirectory);
            UpdateSecurity.AssertNoReparseAncestors(_backupDirectory);
            if (Overlaps(_targetDirectory, _stagingDirectory) || Overlaps(_targetDirectory, _backupDirectory) || Overlaps(_stagingDirectory, _backupDirectory))
                throw new InvalidDataException("Target, staging and backup must be separate directories.");
            if (_targetDirectory.Equals(Path.GetPathRoot(_targetDirectory).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Cannot update a drive root.");
            foreach (var folder in new[] { Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
            {
                string system = Environment.GetFolderPath(folder);
                if (!string.IsNullOrEmpty(system) && Overlaps(system, _targetDirectory)) throw new InvalidDataException("System directory is not an update target.");
            }
            string packageError;
            if (!UpdateChecksumManifest.ValidateDirectory(_stagingDirectory, _newMetadata, out packageError, true)) throw new InvalidDataException(packageError);
            if (UpdateVersion.Parse(_newMetadata.version) <= UpdateManager.ResolveCurrentVersion(_targetDirectory)) throw new InvalidDataException("Downgrade or equal-version update rejected.");
            if (!Directory.Exists(_targetDirectory))
            {
                throw new DirectoryNotFoundException(string.Format("Thư mục cài đặt đích không tồn tại: '{0}'", _targetDirectory));
            }

            // Kiểm tra reparse point / junction point nguy hiểm
            if (UpdateSecurity.IsReparsePoint(_targetDirectory))
            {
                throw new InvalidOperationException("Thư mục cài đặt là Reparse Point / Symlink, không an toàn để cập nhật tự động.");
            }

            // Kiểm tra quyền ghi thực tế bằng cách tạo và xóa file kiểm tra
            string testFile = Path.Combine(_targetDirectory, ".update_write_test_" + Guid.NewGuid().ToString("N"));
            try
            {
                File.WriteAllText(testFile, "test", Encoding.UTF8);
                File.Delete(testFile);
            }
            catch (Exception ex)
            {
                throw new UnauthorizedAccessException(string.Format("Không có quyền ghi vào thư mục cài đặt '{0}'. Hãy chạy ứng dụng với quyền hạn thích hợp hoặc chuyển thư mục sang vị trí người dùng có toàn quyền. Chi tiết: {1}", _targetDirectory, ex.Message));
            }

            // Kiểm tra dung lượng đĩa trống
            try
            {
                string driveName = Path.GetPathRoot(_targetDirectory);
                DriveInfo drive = new DriveInfo(driveName);
                // Cần tối thiểu 100 MB đĩa trống để thực hiện an toàn
                if (drive.AvailableFreeSpace < 100 * 1024 * 1024)
                {
                    throw new IOException(string.Format("Dung lượng đĩa trống không đủ ({0:F1} MB). Cần tối thiểu 100 MB để sao lưu và cập nhật.", drive.AvailableFreeSpace / (1024.0 * 1024.0)));
                }
            }
            catch (IOException)
            {
                throw;
            }
            catch
            {
                // Bỏ qua nếu không truy xuất được thông tin DriveInfo (ví dụ thư mục mạng)
            }
        }

        /// <summary>
        /// Thực thi toàn bộ giao dịch cập nhật: Preflight -> Backup -> Mutate -> Verify -> Commit.
        /// Tự động Rollback nếu xảy ra bất kỳ lỗi nào.
        /// </summary>
        public void Execute()
        {
            PreflightCheck();
            if (File.Exists(_journalPath)) throw new IOException("Recover the previous journal before updating.");
            if (Directory.Exists(_backupDirectory)) throw new IOException("Backup session already exists.");

            var journal = new UpdateJournal
            {
                SessionGuid = _sessionGuid,
                TargetDirectory = _targetDirectory,
                StagingDirectory = _stagingDirectory,
                BackupDirectory = _backupDirectory,
                TargetVersion = _newMetadata.version,
                State = UpdateTransactionState.Prepared,
                CreatedUtc = DateTime.UtcNow.ToString("o"),
                FileActions = new List<UpdateFileRecord>()
            };
            journal.SaveAtomic(_journalPath);

            // Xác định các file cũ bị loại bỏ (obsolete) nếu có manifest cũ
            ReleaseMetadata oldMetadata = null;
            Dictionary<string, string> oldChecksums = null;
            string oldReleaseJsonPath = Path.Combine(_targetDirectory, ReleaseMetadata.ReleaseJsonFileName);
            string oldSha256CsvPath = Path.Combine(_targetDirectory, ReleaseMetadata.Sha256CsvFileName);

            if (File.Exists(oldReleaseJsonPath) && File.Exists(oldSha256CsvPath))
            {
                try
                {
                    oldMetadata = ReleaseMetadata.LoadFromFile(oldReleaseJsonPath);
                    oldChecksums = UpdateChecksumManifest.ReadChecksums(oldSha256CsvPath);
                    string oldError;
                    if (oldMetadata == null || !oldMetadata.Validate(out oldError) || !oldChecksums.ContainsKey("release.json") ||
                        !oldChecksums["release.json"].Equals(UpdateSecurity.ComputeFileSha256(oldReleaseJsonPath), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Old manifest is invalid or not hash-bound.");
                }
                catch
                {
                    // Nếu metadata cũ lỗi, coi như legacy, không tự tiện xóa file
                    oldMetadata = null;
                    oldChecksums = null;
                }
            }

            // Lập danh sách các thao tác file
            var actionList = new List<UpdateFileRecord>();

            // 1. Các file trong manifest mới
            foreach (string relFile in _newMetadata.managedFiles)
            {
                string norm = UpdateSecurity.NormalizeRelativePath(relFile);
                string targetPath = UpdateSecurity.GetSafeFullPath(_targetDirectory, norm);
                bool exists = File.Exists(targetPath);

                actionList.Add(new UpdateFileRecord
                {
                    RelativePath = norm,
                    ActionType = exists ? UpdateFileActionType.Replace : UpdateFileActionType.Add,
                    ExistedBefore = exists,
                    BackupHash = exists ? UpdateSecurity.ComputeFileSha256(targetPath) : null,
                    TargetNewHash = _newChecksums[norm]
                });
            }

            // 2. Metadata files (release.json và SHA256.csv)
            foreach (string metaFile in new[] { ReleaseMetadata.ReleaseJsonFileName, ReleaseMetadata.Sha256CsvFileName })
            {
                string targetPath = Path.Combine(_targetDirectory, metaFile);
                bool exists = File.Exists(targetPath);
                string newHash = _newChecksums.ContainsKey(metaFile) ? _newChecksums[metaFile] : UpdateSecurity.ComputeFileSha256(Path.Combine(_stagingDirectory, metaFile));

                actionList.Add(new UpdateFileRecord
                {
                    RelativePath = metaFile,
                    ActionType = exists ? UpdateFileActionType.Replace : UpdateFileActionType.Add,
                    ExistedBefore = exists,
                    BackupHash = exists ? UpdateSecurity.ComputeFileSha256(targetPath) : null,
                    TargetNewHash = newHash
                });
            }

            // 3. Các file cũ cần xóa: chỉ xóa nếu có oldMetadata hợp lệ, file đó không có trong newMetadata, và hash đĩa khớp oldChecksums
            if (oldMetadata != null && oldChecksums != null)
            {
                var newManagedSet = new HashSet<string>(_newMetadata.managedFiles, StringComparer.OrdinalIgnoreCase);
                foreach (string oldRel in oldMetadata.managedFiles)
                {
                    string norm = UpdateSecurity.NormalizeRelativePath(oldRel);
                    if (!newManagedSet.Contains(norm))
                    {
                        // Không bao giờ xóa file của người dùng
                        if (!UpdateSecurity.IsManagedRuntimePath(norm))
                        {
                            continue;
                        }

                        string targetPath = UpdateSecurity.GetSafeFullPath(_targetDirectory, norm);
                        if (File.Exists(targetPath))
                        {
                            string currentDiskHash = UpdateSecurity.ComputeFileSha256(targetPath);
                            string expectedOldHash = oldChecksums.ContainsKey(norm) ? oldChecksums[norm] : null;

                            // Chỉ xóa nếu file chưa bị người dùng chỉnh sửa
                            if (expectedOldHash != null && string.Equals(currentDiskHash, expectedOldHash, StringComparison.OrdinalIgnoreCase))
                            {
                                actionList.Add(new UpdateFileRecord
                                {
                                    RelativePath = norm,
                                    ActionType = UpdateFileActionType.Delete,
                                    ExistedBefore = true,
                                    BackupHash = currentDiskHash,
                                    TargetNewHash = null
                                });
                            }
                        }
                    }
                }
            }

            // Metadata is committed after runtime changes, including obsolete removals.
            actionList.Sort((a, b) => (IsMetadata(a.RelativePath) ? 1 : 0).CompareTo(IsMetadata(b.RelativePath) ? 1 : 0));
            foreach (var r in actionList) r.TemporaryPath = r.RelativePath + "." + _sessionGuid + ".new_update";
            journal.FileActions = actionList;
            long backupBytes = 32L * 1024 * 1024;
            long incomingBytes = 32L * 1024 * 1024;
            foreach (var record in actionList)
            {
                if (record.ExistedBefore) backupBytes += new FileInfo(UpdateSecurity.GetSafeFullPath(_targetDirectory, record.RelativePath)).Length;
                if (record.ActionType != UpdateFileActionType.Delete) incomingBytes += new FileInfo(UpdateSecurity.GetSafeFullPath(_stagingDirectory, record.RelativePath)).Length;
            }
            string targetDrive = Path.GetPathRoot(_targetDirectory), backupDrive = Path.GetPathRoot(_backupDirectory);
            if (new DriveInfo(targetDrive).AvailableFreeSpace < incomingBytes + (targetDrive.Equals(backupDrive, StringComparison.OrdinalIgnoreCase) ? backupBytes : 0) ||
                new DriveInfo(backupDrive).AvailableFreeSpace < backupBytes) throw new IOException("Insufficient disk space for backup and replacement.");
            journal.State = UpdateTransactionState.BackingUp;
            journal.SaveAtomic(_journalPath);

            // BƯỚC 1: SAO LƯU (BACKUP)
            if (!Directory.Exists(_backupDirectory))
            {
                Directory.CreateDirectory(_backupDirectory);
            }

            foreach (var record in actionList)
            {
                if (record.ExistedBefore)
                {
                    string sourcePath = UpdateSecurity.GetSafeFullPath(_targetDirectory, record.RelativePath);
                    UpdateSecurity.AssertWritableFile(sourcePath);
                    string destPath = UpdateSecurity.GetSafeFullPath(_backupDirectory, record.RelativePath);
                    string destDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }

                    File.Copy(sourcePath, destPath, true);
                    string backupHash = UpdateSecurity.ComputeFileSha256(destPath);
                    if (!string.Equals(backupHash, record.BackupHash, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(string.Format("Lỗi toàn vẹn khi sao lưu file: '{0}'", record.RelativePath));
                    }
                }
            }

            journal.State = UpdateTransactionState.BackedUp;
            journal.SaveAtomic(_journalPath);

            // BƯỚC 2: ÁP DỤNG THAY ĐỔI (MUTATION)
            journal.State = UpdateTransactionState.Replacing;
            journal.SaveAtomic(_journalPath);

            try
            {
                foreach (var record in actionList)
                {
                    string targetPath = UpdateSecurity.GetSafeFullPath(_targetDirectory, record.RelativePath);

                    record.MutationStarted = true;
                    journal.SaveAtomic(_journalPath);

                    if (record.ActionType == UpdateFileActionType.Delete)
                    {
                        if (File.Exists(targetPath))
                        {
                            File.Delete(targetPath);
                        }
                    }
                    else // Replace hoặc Add
                    {
                        string stagingPath = UpdateSecurity.GetSafeFullPath(_stagingDirectory, record.RelativePath);
                        string targetDir = Path.GetDirectoryName(targetPath);
                        if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                        {
                            Directory.CreateDirectory(targetDir);
                        }

                        // Copy sang file tạm cạnh target trước
                        string tempTargetFile = UpdateSecurity.GetSafeFullPath(_targetDirectory, record.TemporaryPath);
                        File.Copy(stagingPath, tempTargetFile, false);
                        string tempHash = UpdateSecurity.ComputeFileSha256(tempTargetFile);
                        if (!string.Equals(tempHash, record.TargetNewHash, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException(string.Format("Mã hash file tạm không khớp cho: '{0}'", record.RelativePath));
                        }

                        // Hoán đổi vào vị trí chính thức
                        if (File.Exists(targetPath))
                        {
                            UpdateSecurity.ReplaceWithRetry(tempTargetFile, targetPath);
                        }
                        else File.Move(tempTargetFile, targetPath);
                    }
                }

                // BƯỚC 3: XÁC THỰC LẠI TARGET (VERIFY)
                journal.State = UpdateTransactionState.Verifying;
                journal.SaveAtomic(_journalPath);

                string verifyError;
                if (!UpdateChecksumManifest.ValidateDirectory(_targetDirectory, _newMetadata, out verifyError))
                {
                    throw new InvalidOperationException(string.Format("Xác thực sau khi cập nhật thất bại: {0}", verifyError));
                }

                // BƯỚC 4: HOÀN TẤT VÀ LƯU THÀNH CÔNG (COMMIT)
                journal.State = UpdateTransactionState.Committed;
                journal.SaveAtomic(_journalPath);

                // Ghi success marker để thông báo cho người dùng ở lần mở tiếp theo
                string pathHash = UpdateSecurity.GetCanonicalPathHash(_targetDirectory);
                string markerPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "TTSK Dim Plates",
                    "Updater",
                    pathHash,
                    "success.marker"
                );
                try { UpdateSecurity.AtomicWrite(markerPath, _newMetadata.version); }
                catch (Exception markerError) { UpdateSecurity.Log(_targetDirectory, "Committed, marker failed: " + markerError); }

                // Dọn dẹp journal sau khi commit thành công
                UpdateJournal.DeleteJournal(_journalPath);
            }
            catch (Exception ex)
            {
                UpdateSecurity.Log(_targetDirectory, "Apply failed: " + ex);
                // Có lỗi xảy ra trong quá trình thay thế hoặc xác thực -> KÍCH HOẠT ROLLBACK
                if (journal.State == UpdateTransactionState.Committed) throw;
                try { Rollback(journal); }
                catch (Exception recoveryError) { throw new AggregateException("Update and rollback failed. Keep journal and backup.", ex, recoveryError); }
                throw new InvalidOperationException(string.Format("Cập nhật thất bại. Toàn bộ file đã được hoàn tác (rollback) về trạng thái ban đầu an toàn. Lỗi: {0}", ex.Message), ex);
            }
        }

        /// <summary>
        /// Hoàn tác toàn bộ các thay đổi, phục hồi lại trạng thái cũ từ thư mục backup.
        /// </summary>
        public static void Rollback(UpdateJournal journal)
        {
            if (journal == null || journal.FileActions == null)
            {
                throw new InvalidDataException("Missing recovery journal.");
            }

            string expectedBackup = Path.Combine(Path.GetDirectoryName(UpdateJournal.GetJournalPath(journal.TargetDirectory)), "backup", journal.SessionGuid ?? "");
            if (!UpdateSecurity.IsSafeRelativePath(journal.SessionGuid) || journal.SessionGuid.IndexOfAny(new[] { '/', '\\' }) >= 0 ||
                !Path.GetFullPath(expectedBackup).Equals(Path.GetFullPath(journal.BackupDirectory), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Backup does not belong to this installation/session.");
            UpdateSecurity.AssertNoReparseAncestors(expectedBackup);
            UpdateSecurity.AssertNoReparseAncestors(journal.TargetDirectory);
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in journal.FileActions)
            {
                if (r == null || !unique.Add(r.RelativePath) || (!IsMetadata(r.RelativePath) && !UpdateSecurity.IsManagedRuntimePath(r.RelativePath)))
                    throw new InvalidDataException("Unsafe recovery file record.");
                UpdateSecurity.GetSafeFullPath(journal.TargetDirectory, r.RelativePath);
                if (r.TemporaryPath != null && r.TemporaryPath != r.RelativePath + "." + journal.SessionGuid + ".new_update")
                    throw new InvalidDataException("Unsafe recovery temporary file.");
            }
            if (journal.State == UpdateTransactionState.Committed || journal.State == UpdateTransactionState.RolledBack) return;
            if (journal.State == UpdateTransactionState.Prepared || journal.State == UpdateTransactionState.BackingUp || journal.State == UpdateTransactionState.BackedUp)
            {
                // No installation mutation has occurred; incomplete backup must not be restored.
                journal.State = UpdateTransactionState.RolledBack;
                journal.SaveAtomic(UpdateJournal.GetJournalPath(journal.TargetDirectory));
                return;
            }
            foreach (var r in journal.FileActions)
                if (r.ExistedBefore && !UpdateSecurity.ComputeFileSha256(UpdateSecurity.GetSafeFullPath(expectedBackup, r.RelativePath)).Equals(r.BackupHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Missing or corrupt backup: " + r.RelativePath);
            var errors = new List<Exception>();

            journal.State = UpdateTransactionState.RollingBack;
            string journalPath = UpdateJournal.GetJournalPath(journal.TargetDirectory);
            journal.SaveAtomic(journalPath);

            foreach (var record in journal.FileActions)
            {
                try
                {
                    string targetPath = UpdateSecurity.GetSafeFullPath(journal.TargetDirectory, record.RelativePath);

                    if (!record.ExistedBefore)
                    {
                        // File mới thêm vào -> xóa đi
                        if (File.Exists(targetPath))
                        {
                            File.Delete(targetPath);
                        }
                    }
                    else
                    {
                        // File đã tồn tại trước đó -> copy lại từ backup
                        string backupPath = UpdateSecurity.GetSafeFullPath(journal.BackupDirectory, record.RelativePath);
                        if (File.Exists(backupPath))
                        {
                            string targetDir = Path.GetDirectoryName(targetPath);
                            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                            {
                                Directory.CreateDirectory(targetDir);
                            }

                            if (!File.Exists(targetPath) || !UpdateSecurity.ComputeFileSha256(targetPath).Equals(record.BackupHash, StringComparison.OrdinalIgnoreCase))
                            {
                                string temp = targetPath + "." + Guid.NewGuid().ToString("N") + ".rollback";
                                try
                                {
                                    File.Copy(backupPath, temp, false);
                                    if (File.Exists(targetPath)) UpdateSecurity.ReplaceWithRetry(temp, targetPath);
                                    else File.Move(temp, targetPath);
                                }
                                finally { if (File.Exists(temp)) File.Delete(temp); }
                            }
                        }
                    }
                    if (record.ExistedBefore && !UpdateSecurity.ComputeFileSha256(targetPath).Equals(record.BackupHash, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Rollback hash mismatch: " + record.RelativePath);
                    if (!record.ExistedBefore && File.Exists(targetPath)) throw new IOException("Added file survived rollback.");
                    if (record.TemporaryPath != null)
                    {
                        string temp = UpdateSecurity.GetSafeFullPath(journal.TargetDirectory, record.TemporaryPath);
                        if (File.Exists(temp)) File.Delete(temp);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                    UpdateSecurity.Log(journal.TargetDirectory, "Rollback failed: " + ex);
                }
            }

            // Dọn sạch các file tạm có đuôi .new_update còn sót
            if (errors.Count > 0) throw new AggregateException("Rollback incomplete; retain backup/journal.", errors);

            journal.State = UpdateTransactionState.RolledBack;
            journal.SaveAtomic(journalPath);
        }

        private static bool IsMetadata(string p) { return p == "release.json" || p == "SHA256.csv"; }
        internal static bool Overlaps(string a, string b)
        {
            a = Path.GetFullPath(a).TrimEnd('\\', '/'); b = Path.GetFullPath(b).TrimEnd('\\', '/');
            return a.Equals(b, StringComparison.OrdinalIgnoreCase) || a.StartsWith(b + "\\", StringComparison.OrdinalIgnoreCase) || b.StartsWith(a + "\\", StringComparison.OrdinalIgnoreCase);
        }
    }
}
