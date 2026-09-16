using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace TTSK_AutoDim_Plates.Updater
{
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
