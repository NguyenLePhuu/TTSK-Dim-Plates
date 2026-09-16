using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace TTSK_AutoDim_Plates.Updater
{
    /// <summary>
    /// Các tiện ích bảo mật, kiểm tra đường dẫn an toàn, chống Zip Slip và bảo vệ file cấu hình người dùng.
    /// </summary>
    public static class UpdateSecurity
    {
        // Danh sách tên thiết bị cấm theo chuẩn Windows/DOS
        private static readonly HashSet<string> ReservedDeviceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        // Danh sách file thuộc quyền sở hữu của người dùng, updater tuyệt đối không được ghi đè hoặc xóa
        private static readonly string[] ProtectedUserFileNames = new[]
        {
            "theme.cfg",
            "shortcut.cfg",
            "auto_section.cfg",
            "Start TTSK Dim Plates.bat",
            "user.config"
        };

        /// <summary>
        /// Chuẩn hóa đường dẫn tương đối dùng dấu gạch chéo xuôi '/', loại bỏ dấu ở đầu và khoảng trắng thừa.
        /// </summary>
        public static string NormalizeRelativePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return string.Empty;
            }

            return relativePath.Replace('\\', '/');
        }

        /// <summary>
        /// Kiểm tra tính hợp lệ và an toàn của đường dẫn tương đối.
        /// Từ chối đường dẫn tuyệt đối, chứa '..', chứa luồng dữ liệu phụ ':', tên thiết bị Windows, v.v.
        /// </summary>
        public static bool IsSafeRelativePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return false;
            }

            string trimmed = relativePath.Trim();
            if (trimmed != relativePath) return false;
            // Chặn đường dẫn bắt đầu bằng / hoặc \ (rooted hoặc UNC)
            if (trimmed.StartsWith("/") || trimmed.StartsWith("\\"))
            {
                return false;
            }

            // Chặn đường dẫn tuyệt đối
            if (Path.IsPathRooted(trimmed))
            {
                return false;
            }

            string normalized = NormalizeRelativePath(trimmed);
            if (string.IsNullOrEmpty(normalized))
            {
                return false;
            }

            // Chặn đường dẫn tuyệt đối hoặc UNC
            if (normalized.StartsWith("//") || Path.IsPathRooted(normalized))
            {
                return false;
            }

            // Chặn luồng dữ liệu phụ (Alternate Data Streams) dấu hai chấm ':'
            if (normalized.IndexOf(':') >= 0)
            {
                return false;
            }

            string[] segments = normalized.Split('/');
            foreach (string seg in segments)
            {
                if (string.IsNullOrWhiteSpace(seg))
                {
                    return false;
                }
                if (seg.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || seg.Contains(",")) return false;

                // Chặn đường dẫn nhảy ngược thư mục '..' hoặc '.'
                if (seg == ".." || seg == ".")
                {
                    return false;
                }

                // Chặn đuôi có dấu chấm hoặc dấu cách (Windows alias bypass)
                if (seg.EndsWith(".") || seg.EndsWith(" "))
                {
                    return false;
                }

                // Chặn tên thiết bị Windows (ví dụ: NUL, CON, AUX, COM1...)
                string segWithoutExt = seg.Split('.')[0];
                if (ReservedDeviceNames.Contains(seg) || ReservedDeviceNames.Contains(segWithoutExt))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Ghép đường dẫn tương đối vào thư mục gốc và kiểm tra ngăn chặn triệt để tấn công Zip Slip và Sibling-prefix escape.
        /// </summary>
        public static string GetSafeFullPath(string rootDirectory, string relativePath)
        {
            if (!IsSafeRelativePath(relativePath))
            {
                throw new InvalidOperationException(string.Format("Đường dẫn tương đối không an toàn: '{0}'", relativePath));
            }

            string fullRoot = Path.GetFullPath(rootDirectory);
            if (!fullRoot.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                fullRoot += Path.DirectorySeparatorChar;
            }

            string combined = Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            string fullTarget = Path.GetFullPath(combined);

            // Kiểm tra fullTarget phải nằm trọn vẹn bên trong fullRoot
            if (!fullTarget.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(string.Format("Đường dẫn giải nén vượt ra ngoài thư mục đích (Zip Slip): '{0}'", relativePath));
            }

            AssertNoReparseAncestors(fullTarget);
            return fullTarget;
        }

        /// <summary>
        /// Kiểm tra xem một file có phải là file cấu hình / log của người dùng được bảo vệ hay không.
        /// </summary>
        public static bool IsProtectedUserFile(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return false;
            }

            string normalized = NormalizeRelativePath(relativePath);
            string fileName = Path.GetFileName(normalized);

            foreach (string protectedName in ProtectedUserFileNames)
            {
                if (string.Equals(fileName, protectedName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // Bảo vệ các file log và user config
            if (fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".user.config", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (normalized.StartsWith("logs/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Kiểm tra xem thư mục hoặc file có phải là Junction Point / Symlink / Reparse Point hay không.
        /// </summary>
        public static bool IsReparsePoint(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
            {
                return false;
            }

            try
            {
                FileAttributes attributes = File.GetAttributes(path);
                return (attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
            }
            catch { throw; }
        }

        public static void AssertNoReparseAncestors(string path)
        {
            string current = Path.GetFullPath(path);
            while (!string.IsNullOrEmpty(current))
            {
                if (IsReparsePoint(current)) throw new IOException("Reparse point is not allowed: " + current);
                current = Path.GetDirectoryName(current);
            }
        }

        public static bool IsManagedRuntimePath(string path)
        {
            if (!IsSafeRelativePath(path) || IsProtectedUserFile(path)) return false;
            string p = NormalizeRelativePath(path);
            if (p.Equals("TTSK Dim Plates.exe", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("TTSK Dim Plates.exe.config", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("Phu_Macro_GridVisibility.cs", StringComparison.OrdinalIgnoreCase)) return true;
            if (p.IndexOf('/') < 0 && p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                return !p.StartsWith("Tekla.Structures", StringComparison.OrdinalIgnoreCase) &&
                    !p.Equals("DPMPrinter.dll", StringComparison.OrdinalIgnoreCase) && !p.Equals("DotNetKit.dll", StringComparison.OrdinalIgnoreCase);
            return (p.StartsWith("Data/", StringComparison.OrdinalIgnoreCase) && p.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase)) ||
                (p.StartsWith("Resources/", StringComparison.OrdinalIgnoreCase) && p.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
        }

        public static void AtomicWrite(string path, string content)
        {
            AssertNoReparseAncestors(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] bytes = new System.Text.UTF8Encoding(false).GetBytes(content);
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (File.Exists(path)) ReplaceWithRetry(temp, path);
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        public static void Log(string target, string message)
        {
            try
            {
                string dir = Path.Combine(Path.GetDirectoryName(UpdateJournal.GetJournalPath(target)), "logs");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, DateTime.UtcNow.ToString("yyyyMMdd") + ".log");
                if (!File.Exists(path) || new FileInfo(path).Length < 4 * 1024 * 1024)
                    File.AppendAllText(path, DateTime.UtcNow.ToString("o") + " " + message + Environment.NewLine);
            }
            catch (Exception) { /* Diagnostics must not change a transaction's outcome. */ }
        }

        public static void ReplaceWithRetry(string source, string target)
        {
            for (int attempt = 0; ; attempt++)
            {
                AssertNoReparseAncestors(source); AssertNoReparseAncestors(target);
                try { File.Replace(source, target, null); return; }
                catch (IOException ex)
                {
                    int code = ex.HResult & 0xFFFF;
                    if (attempt >= 9 || (code != 32 && code != 33)) throw;
                    System.Threading.Thread.Sleep(150);
                }
            }
        }

        public static void AssertWritableFile(string path)
        {
            for (int attempt=0; ; attempt++)
            {
                try { using (var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } return; }
                catch (IOException ex)
                {
                    int code = ex.HResult & 0xFFFF;
                    if (attempt >= 19 || (code != 32 && code != 33)) throw;
                    System.Threading.Thread.Sleep(150);
                }
            }
        }

        /// <summary>
        /// Tính chuỗi băm SHA-256 (64 ký tự hex chữ thường) của một file.
        /// </summary>
        public static string ComputeFileSha256(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Không tìm thấy file để tính SHA-256.", filePath);
            }

            using (SHA256 sha256 = SHA256.Create())
            {
                using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536))
                {
                    byte[] hashBytes = sha256.ComputeHash(stream);
                    return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                }
            }
        }

        /// <summary>
        /// Tạo mã băm ngắn 16 ký tự đại diện cho đường dẫn cài đặt canonical để phân biệt giữa các bản portable khác nhau.
        /// </summary>
        public static string GetCanonicalPathHash(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "default";
            }

            string canonical = Path.GetFullPath(path).TrimEnd('\\', '/').ToLowerInvariant();
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(canonical);
                byte[] hash = sha256.ComputeHash(bytes);
                string hex = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                return hex.Substring(0, 16);
            }
        }
    }
}
