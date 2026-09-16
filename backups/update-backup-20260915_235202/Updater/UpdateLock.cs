using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace TTSK_AutoDim_Plates.Updater
{
    /// <summary>
    /// Quản lý khóa ứng dụng và khóa tiến trình cập nhật theo từng thư mục cài đặt độc lập.
    /// </summary>
    public sealed class UpdateLock : IDisposable
    {
        private readonly string _targetDirectory;
        private readonly string _pathHash;
        private readonly string _lockFilePath;
        private FileStream _lockFileStream;
        private Mutex _updateMutex;
        private bool _hasMutex;

        public UpdateLock(string targetDirectory)
        {
            _targetDirectory = Path.GetFullPath(targetDirectory).TrimEnd('\\', '/');
            _pathHash = UpdateSecurity.GetCanonicalPathHash(_targetDirectory);

            string stateDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TTSK Dim Plates",
                "Updater",
                _pathHash
            );

            if (!Directory.Exists(stateDir))
            {
                Directory.CreateDirectory(stateDir);
            }

            _lockFilePath = Path.Combine(stateDir, "update.lock");
        }

        /// <summary>
        /// Thử lấy quyền khóa độc quyền để tiến hành cập nhật.
        /// Trả về true nếu thành công, false nếu có tiến trình khác đang thực hiện cập nhật.
        /// </summary>
        public bool TryAcquireUpdateLock(int timeoutMilliseconds = 1000)
        {
            string mutexName = @"Local\TTSK_Dim_Plates_Updater_" + _pathHash;
            try
            {
                _updateMutex = new Mutex(false, mutexName);
                try { _hasMutex = _updateMutex.WaitOne(timeoutMilliseconds, false); }
                catch (AbandonedMutexException) { _hasMutex = true; }
                if (!_hasMutex)
                {
                    return false;
                }

                _lockFileStream = new FileStream(_lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return true;
            }
            catch
            {
                Release();
                return false;
            }
        }

        /// <summary>
        /// Kiểm tra xem có tiến trình cập nhật nào đang hoạt động trên thư mục cài đặt này không.
        /// Dùng cho normal app lúc khởi động để chặn mở app khi đang ghi đè file.
        /// </summary>
        public static bool IsUpdateInProgress(string targetDirectory)
        {
            string canonical = Path.GetFullPath(targetDirectory).TrimEnd('\\', '/');
            string pathHash = UpdateSecurity.GetCanonicalPathHash(canonical);
            string mutexName = @"Local\TTSK_Dim_Plates_Updater_" + pathHash;

            try
            {
                using (var mutex = Mutex.OpenExisting(mutexName))
                {
                    // Nếu mutex đang tồn tại và không acquire được ngay lập tức thì có updater đang chạy
                    if (!mutex.WaitOne(0, false))
                    {
                        return true;
                    }
                    mutex.ReleaseMutex();
                    return false;
                }
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                return false; // Mutex chưa tồn tại, an toàn
            }
            catch { return true; }
        }

        /// <summary>
        /// Tìm tất cả các tiến trình TTSK Dim Plates đang chạy từ đúng thư mục target này (loại trừ chính process hiện tại).
        /// </summary>
        public static List<Process> FindRunningInstancesForTarget(string targetDirectory, int excludePid = 0)
        {
            var list = new List<Process>();
            string canonicalTarget = Path.GetFullPath(targetDirectory).TrimEnd('\\', '/');
            string targetExePath = Path.Combine(canonicalTarget, "TTSK Dim Plates.exe");

            Process[] processes = Process.GetProcessesByName("TTSK Dim Plates");
            foreach (Process proc in processes)
            {
                try
                {
                    if (proc.Id == excludePid)
                    {
                        continue;
                    }

                    if (proc.HasExited)
                    {
                        continue;
                    }

                    string procExePath = null;
                    try
                    {
                        procExePath = proc.MainModule.FileName;
                    }
                    catch { if (!proc.HasExited) list.Add(proc); continue; }

                    if (!string.IsNullOrEmpty(procExePath))
                    {
                        if (string.Equals(Path.GetFullPath(procExePath), targetExePath, StringComparison.OrdinalIgnoreCase))
                        {
                            list.Add(proc);
                        }
                    }
                }
                catch
                {
                    // Bỏ qua nếu process đã thoát trong lúc kiểm tra
                }
            }

            return list;
        }

        /// <summary>
        /// Chờ một tiến trình cha thoát hoàn toàn với kiểm tra StartTime để tránh tái sử dụng PID.
        /// </summary>
        public static bool WaitForParentExit(int parentPid, long expectedStartTicks, int timeoutMilliseconds)
        {
            if (parentPid <= 0)
            {
                return true;
            }

            Process parentProc = null;
            try
            {
                parentProc = Process.GetProcessById(parentPid);
                if (expectedStartTicks > 0)
                {
                    long actualStartTicks = parentProc.StartTime.ToUniversalTime().Ticks;
                    if (actualStartTicks != expectedStartTicks)
                    {
                        // PID đã bị hệ điều hành tái sử dụng cho tiến trình khác
                        return true;
                    }
                }
            }
            catch (ArgumentException)
            {
                // Tiến trình đã thoát
                return true;
            }
            catch { return false; }

            if (parentProc == null)
            {
                return true;
            }

            using (parentProc)
            {
                return parentProc.WaitForExit(timeoutMilliseconds);
            }
        }

        public void Release()
        {
            if (_lockFileStream != null)
            {
                try
                {
                    _lockFileStream.Dispose();
                }
                catch { }
                _lockFileStream = null;
            }

            if (_updateMutex != null)
            {
                if (_hasMutex)
                {
                    try
                    {
                        _updateMutex.ReleaseMutex();
                    }
                    catch { }
                    _hasMutex = false;
                }
                try
                {
                    _updateMutex.Dispose();
                }
                catch { }
                _updateMutex = null;
            }
        }

        public void Dispose()
        {
            Release();
        }
    }
}
