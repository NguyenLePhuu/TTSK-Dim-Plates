// Định dạng release, danh sách checksum, quy tắc đường dẫn và so sánh phiên bản.
// LUỒNG: đọc metadata → Validate → kiểm tra danh sách file/hash → UpdateVersion so sánh số phiên bản.
// ĐIỂM SỬA: schema ở ReleaseMetadata; CSV ở UpdateChecksumManifest; đường dẫn/file được quản lý ở UpdateSecurity.
// THAM SỐ: ExpectedProduct/ExpectedRepository và tên metadata là hợp đồng gói; sửa phải đồng bộ bộ đóng gói.
// LƯU Ý: không nới IsSafeRelativePath/IsManagedRuntimePath chỉ để một gói lỗi vượt kiểm tra.
// TRA CỨU: PROJECT_MAP.md ở gốc repo; chỉ mục kiểu/hàm và điểm chỉnh thông số.
// Mục lục:
//   Updater/UpdateManifest.cs
//   Updater/UpdateSecurity.cs
//   Updater/UpdateVersion.cs

#region UpdateManifest - Đọc manifest và danh sách file quản lý


namespace TTSK_AutoDim_Plates.Updater
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Web.Script.Serialization;

    /// <summary>
    /// Đối tượng chứa thông tin manifest từ release.json.
    /// </summary>
    public sealed class ReleaseMetadata
    {
        public int schemaVersion { get; set; } = 1;
        public string product { get; set; } = "TTSK Dim Plates";
        public string repository { get; set; } = "NguyenLePhuu/TTSK-Dim-Plates";
        public string version { get; set; } = string.Empty;
        public string tag { get; set; } = string.Empty;
        public string commit { get; set; } = string.Empty;
        public string createdUtc { get; set; } = string.Empty;
        public List<string> managedFiles { get; set; } = new List<string>();

        // HỢP ĐỒNG GÓI: tên metadata/product/repository phải đồng bộ UpdateSources và script đóng gói.
        public const string ReleaseJsonFileName = "release.json";
        public const string Sha256CsvFileName = "SHA256.csv";
        public const string ExpectedProduct = "TTSK Dim Plates";
        public const string ExpectedRepository = "NguyenLePhuu/TTSK-Dim-Plates";

        /// <summary>
        /// Xác thực tính hợp lệ của metadata phiên bản.
        /// </summary>
        public bool Validate(out string errorMessage)
        {
            errorMessage = null;
            if (schemaVersion != 1)
            {
                errorMessage = string.Format("Phiên bản schema không được hỗ trợ: {0}. Yêu cầu schemaVersion = 1.", schemaVersion);
                return false;
            }

            if (!string.Equals(product, ExpectedProduct, StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = string.Format("Tên sản phẩm không khớp: '{0}', mong đợi: '{1}'.", product, ExpectedProduct);
                return false;
            }

            if (!string.Equals(repository, ExpectedRepository, StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = string.Format("Repository không khớp: '{0}', mong đợi: '{1}'.", repository, ExpectedRepository);
                return false;
            }

            if (!UpdateVersion.TryParse(version, out UpdateVersion ver))
            {
                errorMessage = string.Format("Phiên bản không hợp lệ trong metadata: '{0}'.", version);
                return false;
            }

            if (string.IsNullOrWhiteSpace(tag) || (!tag.Equals("v" + ver.ToString(), StringComparison.OrdinalIgnoreCase) && !tag.Equals(ver.ToString(), StringComparison.OrdinalIgnoreCase)))
            {
                errorMessage = string.Format("Tag '{0}' không khớp với phiên bản '{1}'.", tag, version);
                return false;
            }

            if (string.IsNullOrWhiteSpace(commit) || !System.Text.RegularExpressions.Regex.IsMatch(commit, @"\A[0-9a-fA-F]{40}\z"))
            {
                errorMessage = string.Format("Mã hash commit không hợp lệ (cần 40 ký tự hex): '{0}'.", commit);
                return false;
            }

            if (managedFiles == null || managedFiles.Count == 0)
            {
                errorMessage = "Danh sách file quản lý (managedFiles) trống.";
                return false;
            }

            // Kiểm tra tính hợp lệ của từng đường dẫn trong managedFiles
            var pathSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in managedFiles)
            {
                if (!UpdateSecurity.IsSafeRelativePath(file))
                {
                    errorMessage = string.Format("Đường dẫn file không an toàn trong managedFiles: '{0}'.", file);
                    return false;
                }

                string normalized = UpdateSecurity.NormalizeRelativePath(file);
                if (normalized.Equals(ReleaseJsonFileName, StringComparison.OrdinalIgnoreCase) ||
                    normalized.Equals(Sha256CsvFileName, StringComparison.OrdinalIgnoreCase))
                {
                    errorMessage = string.Format("managedFiles không được chứa file metadata: '{0}'.", normalized);
                    return false;
                }

                if (!UpdateSecurity.IsManagedRuntimePath(normalized))
                {
                    errorMessage = string.Format("managedFiles không được chứa file cấu hình người dùng: '{0}'.", normalized);
                    return false;
                }

                if (!pathSet.Add(normalized))
                {
                    errorMessage = string.Format("Trùng lặp đường dẫn trong managedFiles: '{0}'.", normalized);
                    return false;
                }
            }

            return true;
        }

        public string ToJson()
        {
            var serializer = new JavaScriptSerializer();
            return serializer.Serialize(this);
        }

        public static ReleaseMetadata FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("Nội dung JSON không được rỗng.", nameof(json));
            }

            var serializer = new JavaScriptSerializer();
            var fields = serializer.Deserialize<Dictionary<string, object>>(json);
            foreach (string key in new[] { "schemaVersion", "product", "repository", "version", "tag", "commit", "createdUtc", "managedFiles" })
                if (fields == null || !fields.ContainsKey(key)) throw new InvalidDataException("Missing metadata field: " + key);
            return serializer.Deserialize<ReleaseMetadata>(json);
        }

        public static ReleaseMetadata LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            string content = File.ReadAllText(filePath, Encoding.UTF8);
            return FromJson(content);
        }

        public void SaveToFile(string filePath)
        {
            string json = ToJson();
            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(filePath, json, Encoding.UTF8);
        }
    }

    /// <summary>
    /// Xử lý đọc, ghi và đối chiếu SHA256.csv.
    /// </summary>
    public static class UpdateChecksumManifest
    {
        public const string Header = "Path,SHA256";

        /// <summary>
        /// Đọc bảng mã băm SHA256 từ file CSV.
        /// </summary>
        public static Dictionary<string, string> ReadChecksums(string csvFilePath)
        {
            if (!File.Exists(csvFilePath))
            {
                throw new FileNotFoundException("Không tìm thấy file SHA256.csv.", csvFilePath);
            }

            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string[] lines = File.ReadAllLines(csvFilePath, Encoding.UTF8);

            if (lines.Length == 0)
            {
                throw new InvalidDataException("File SHA256.csv trống.");
            }

            int startIndex = 0;
            if (lines[0].Trim().Equals(Header, StringComparison.OrdinalIgnoreCase))
            {
                startIndex = 1;
            }
            else throw new InvalidDataException("Missing CSV header Path,SHA256.");

            for (int i = startIndex; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                int commaIndex = line.IndexOf(',');
                if (commaIndex <= 0 || commaIndex >= line.Length - 1)
                {
                    throw new InvalidDataException(string.Format("Dòng CSV không hợp lệ (dòng {0}): '{1}'", i + 1, line));
                }

                string path = line.Substring(0, commaIndex);
                string hash = line.Substring(commaIndex + 1).Trim().ToLowerInvariant();

                string normalizedPath = UpdateSecurity.NormalizeRelativePath(path);
                if (!UpdateSecurity.IsSafeRelativePath(normalizedPath))
                {
                    throw new InvalidDataException(string.Format("Đường dẫn không an toàn trong SHA256.csv: '{0}'", path));
                }

                if (!System.Text.RegularExpressions.Regex.IsMatch(hash, @"\A[0-9a-f]{64}\z"))
                {
                    throw new InvalidDataException(string.Format("Mã hash không đúng độ dài 64 ký tự hex: '{0}' cho file '{1}'.", hash, path));
                }

                if (dict.ContainsKey(normalizedPath))
                {
                    throw new InvalidDataException(string.Format("Đường dẫn bị trùng lặp trong SHA256.csv: '{0}'.", normalizedPath));
                }

                dict[normalizedPath] = hash;
            }

            return dict;
        }

        /// <summary>
        /// Ghi danh sách mã băm ra file SHA256.csv với UTF-8.
        /// </summary>
        public static void WriteChecksums(string csvFilePath, IDictionary<string, string> checksums)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Header);

            var sortedKeys = new List<string>(checksums.Keys);
            sortedKeys.Sort(StringComparer.OrdinalIgnoreCase);

            foreach (string key in sortedKeys)
            {
                string normalizedKey = UpdateSecurity.NormalizeRelativePath(key);
                sb.Append(normalizedKey);
                sb.Append(',');
                sb.AppendLine(checksums[key].ToLowerInvariant());
            }

            string dir = Path.GetDirectoryName(csvFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(csvFilePath, sb.ToString(), new UTF8Encoding(false));
        }

        /// <summary>
        /// Xác thực toàn diện một thư mục đối chiếu với manifest release.json và SHA256.csv.
        /// </summary>
        public static bool ValidateDirectory(string directoryPath, ReleaseMetadata metadata, out string errorMessage, bool exactFiles = false)
        {
            errorMessage = null;
            if (metadata == null) { errorMessage = "Missing metadata."; return false; }
            if (!metadata.Validate(out errorMessage))
            {
                return false;
            }

            if (exactFiles)
            {
                var expected = new HashSet<string>(metadata.managedFiles, StringComparer.OrdinalIgnoreCase);
                expected.Add("release.json"); expected.Add("SHA256.csv");
                if (!expected.Contains("TTSK Dim Plates.exe") || !expected.Contains("TTSK Dim Plates.exe.config"))
                { errorMessage = "Package must contain EXE and EXE.config."; return false; }
                var actual = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var pending = new Stack<string>(); pending.Push(directoryPath);
                while (pending.Count > 0)
                {
                    string dir = pending.Pop(); UpdateSecurity.AssertNoReparseAncestors(dir);
                    foreach (string file in Directory.GetFiles(dir))
                    {
                        UpdateSecurity.AssertNoReparseAncestors(file);
                        actual.Add(UpdateSecurity.NormalizeRelativePath(file.Substring(Path.GetFullPath(directoryPath).TrimEnd('\\').Length + 1)));
                    }
                    foreach (string sub in Directory.GetDirectories(dir)) pending.Push(sub);
                }
                if (!expected.SetEquals(actual)) { errorMessage = "Unexpected or missing file in package."; return false; }
            }

            string csvPath = Path.Combine(directoryPath, ReleaseMetadata.Sha256CsvFileName);
            if (!File.Exists(csvPath))
            {
                errorMessage = "Thiếu file SHA256.csv trong gói cập nhật.";
                return false;
            }

            Dictionary<string, string> checksums;
            try
            {
                checksums = ReadChecksums(csvPath);
            }
            catch (Exception ex)
            {
                errorMessage = string.Format("Lỗi khi đọc SHA256.csv: {0}", ex.Message);
                return false;
            }

            // CSV phải chứa đúng: managedFiles + release.json
            var expectedInCsv = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in metadata.managedFiles)
            {
                expectedInCsv.Add(UpdateSecurity.NormalizeRelativePath(file));
            }
            expectedInCsv.Add(ReleaseMetadata.ReleaseJsonFileName);

            if (checksums.Count != expectedInCsv.Count)
            {
                errorMessage = string.Format("Số lượng file trong SHA256.csv ({0}) không khớp mong đợi ({1}).", checksums.Count, expectedInCsv.Count);
                return false;
            }

            foreach (string file in expectedInCsv)
            {
                if (!checksums.ContainsKey(file))
                {
                    errorMessage = string.Format("File '{0}' có trong manifest nhưng thiếu trong SHA256.csv.", file);
                    return false;
                }
            }

            // Kiểm tra hash thực tế của từng file
            foreach (var kvp in checksums)
            {
                string filePath = UpdateSecurity.GetSafeFullPath(directoryPath, kvp.Key);
                if (!File.Exists(filePath))
                {
                    errorMessage = string.Format("File không tồn tại trên đĩa: '{0}'.", kvp.Key);
                    return false;
                }

                string actualHash = UpdateSecurity.ComputeFileSha256(filePath);
                if (!string.Equals(actualHash, kvp.Value, StringComparison.OrdinalIgnoreCase))
                {
                    errorMessage = string.Format("Mã hash không khớp cho file '{0}'. Kỳ vọng: {1}, thực tế: {2}.", kvp.Key, kvp.Value, actualHash);
                    return false;
                }
            }

            return true;
        }
    }
}
#endregion

#region UpdateSecurity - Kiểm tra đường dẫn, hash và nội dung gói


namespace TTSK_AutoDim_Plates.Updater
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security.Cryptography;

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
#endregion

#region UpdateVersion - Phân tích và so sánh phiên bản


namespace TTSK_AutoDim_Plates.Updater
{
    using System;

    /// <summary>
    /// Đại diện cho phiên bản cập nhật dạng Major.Minor.Patch (ví dụ: 1.0.15 hoặc v1.0.15).
    /// Hỗ trợ so sánh số học chính xác và chống hạ cấp (downgrade).
    /// </summary>
    public sealed class UpdateVersion : IComparable<UpdateVersion>, IEquatable<UpdateVersion>
    {
        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }

        public UpdateVersion(int major, int minor, int patch)
        {
            if (major < 0 || minor < 0 || patch < 0)
            {
                throw new ArgumentOutOfRangeException("Các thành phần phiên bản không được là số âm.");
            }

            Major = major;
            Minor = minor;
            Patch = patch;
        }

        /// <summary>
        /// Thử phân tích chuỗi phiên bản dạng 1.0.15 hoặc v1.0.15.
        /// Bắt buộc phải có đúng 3 thành phần số học không âm.
        /// </summary>
        public static bool TryParse(string input, out UpdateVersion version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            string trimmed = input.Trim();
            if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(1).Trim();
            }

            string[] parts = trimmed.Split('.');
            if (!System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z")) return false;
            if (parts.Length != 3)
            {
                return false;
            }

            int major, minor, patch;
            if (!int.TryParse(parts[0], out major) || major < 0 ||
                !int.TryParse(parts[1], out minor) || minor < 0 ||
                !int.TryParse(parts[2], out patch) || patch < 0)
            {
                return false;
            }

            // Chặn các trường hợp có ký tự lạ hoặc khoảng trắng bên trong số
            if (parts[0].Trim() != parts[0] || parts[1].Trim() != parts[1] || parts[2].Trim() != parts[2])
            {
                return false;
            }

            version = new UpdateVersion(major, minor, patch);
            return true;
        }

        public static UpdateVersion Parse(string input)
        {
            if (TryParse(input, out UpdateVersion version))
            {
                return version;
            }

            throw new FormatException(string.Format("Định dạng phiên bản không hợp lệ: '{0}'. Yêu cầu dạng X.Y.Z hoặc vX.Y.Z.", input));
        }

        public int CompareTo(UpdateVersion other)
        {
            if (ReferenceEquals(other, null)) return 1;
            if (ReferenceEquals(this, other)) return 0;

            int cmp = Major.CompareTo(other.Major);
            if (cmp != 0) return cmp;

            cmp = Minor.CompareTo(other.Minor);
            if (cmp != 0) return cmp;

            return Patch.CompareTo(other.Patch);
        }

        public bool Equals(UpdateVersion other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Major == other.Major && Minor == other.Minor && Patch == other.Patch;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as UpdateVersion);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + Major.GetHashCode();
                hash = hash * 31 + Minor.GetHashCode();
                hash = hash * 31 + Patch.GetHashCode();
                return hash;
            }
        }

        public override string ToString()
        {
            return string.Format("{0}.{1}.{2}", Major, Minor, Patch);
        }

        public string ToDisplayString()
        {
            return string.Format("v{0}.{1}.{2}", Major, Minor, Patch);
        }

        public static bool operator ==(UpdateVersion left, UpdateVersion right)
        {
            if (ReferenceEquals(left, null)) return ReferenceEquals(right, null);
            return left.Equals(right);
        }

        public static bool operator !=(UpdateVersion left, UpdateVersion right)
        {
            return !(left == right);
        }

        public static bool operator <(UpdateVersion left, UpdateVersion right)
        {
            if (ReferenceEquals(left, null)) return !ReferenceEquals(right, null);
            return left.CompareTo(right) < 0;
        }

        public static bool operator <=(UpdateVersion left, UpdateVersion right)
        {
            if (ReferenceEquals(left, null)) return true;
            return left.CompareTo(right) <= 0;
        }

        public static bool operator >(UpdateVersion left, UpdateVersion right)
        {
            if (ReferenceEquals(left, null)) return false;
            return left.CompareTo(right) > 0;
        }

        public static bool operator >=(UpdateVersion left, UpdateVersion right)
        {
            if (ReferenceEquals(left, null)) return ReferenceEquals(right, null);
            return left.CompareTo(right) >= 0;
        }
    }
}
#endregion
