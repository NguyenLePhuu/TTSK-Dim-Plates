using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TTSK_AutoDim_Plates.Updater
{
    public sealed class UpdateDownloadProgress
    {
        public long BytesReceived { get; set; }
        public long TotalBytesToReceive { get; set; }
        public int ProgressPercentage { get; set; }
        public string StatusMessage { get; set; }
    }

    /// <summary>
    /// Xử lý tải về, giải nén an toàn và xác thực gói cập nhật.
    /// </summary>
    public static class UpdateDownloader
    {
        public const long MaxChecksumBytes = 4096; // 4 KB
        public const long MaxZipBytes = 200 * 1024 * 1024; // 200 MB
        public const long MaxUncompressedBytesTotal = 500 * 1024 * 1024; // 500 MB
        public const long MaxUncompressedBytesPerFile = 100 * 1024 * 1024; // 100 MB
        public const int MaxEntryCount = 500;
        public const int DownloadTimeoutSeconds = 300; // 5 phút

        private static readonly HashSet<string> AllowedCdnHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "github.com",
            "api.github.com",
            "objects.githubusercontent.com",
            "release-assets.githubusercontent.com",
            "github-releases.githubusercontent.com",
            "github-production-release-asset-2e65be.s3.amazonaws.com"
        };

        private static HttpClient CreateDownloadClient()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false // Tự xử lý redirect để kiểm soát host an toàn
            };

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(DownloadTimeoutSeconds)
            };

            client.DefaultRequestHeaders.UserAgent.ParseAdd(GitHubReleaseService.UserAgent);
            return client;
        }

        /// <summary>
        /// Thực hiện yêu cầu HTTP GET an toàn, kiểm soát redirect tối đa 5 hop và whitelist host.
        /// </summary>
        private static async Task<HttpResponseMessage> SendRequestWithSafeRedirectsAsync(HttpClient client, string initialUrl, CancellationToken ct)
        {
            GitHubReleaseService.ValidateAssetDownloadUrl(initialUrl);
            string currentUrl = initialUrl;
            int redirectHops = 0;

            while (redirectHops < 5)
            {
                ct.ThrowIfCancellationRequested();
                var request = new HttpRequestMessage(HttpMethod.Get, currentUrl);
                var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.MovedPermanently ||
                    response.StatusCode == HttpStatusCode.Found ||
                    response.StatusCode == HttpStatusCode.SeeOther ||
                    response.StatusCode == HttpStatusCode.TemporaryRedirect ||
                    (int)response.StatusCode == 308)
                {
                    Uri redirectUri = response.Headers.Location;
                    if (redirectUri == null)
                    {
                        throw new WebException("Redirect không cung cấp Location header.");
                    }

                    if (!redirectUri.IsAbsoluteUri)
                    {
                        redirectUri = new Uri(new Uri(currentUrl), redirectUri);
                    }

                    if (!string.Equals(redirectUri.Scheme, "https", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(redirectUri.UserInfo) || !redirectUri.IsDefaultPort)
                    {
                        throw new InvalidOperationException("Redirect đến địa chỉ không an toàn (không phải HTTPS).");
                    }

                    string host = redirectUri.Host.ToLowerInvariant();
                    bool isAllowedHost = false;
                    foreach (string allowed in AllowedCdnHosts)
                    {
                        if (host == allowed)
                        {
                            isAllowedHost = true;
                            break;
                        }
                    }

                    if (!isAllowedHost)
                    {
                        throw new InvalidOperationException(string.Format("Redirect đến host CDN không nằm trong danh sách cho phép: '{0}'.", host));
                    }

                    currentUrl = redirectUri.AbsoluteUri;
                    redirectHops++;
                    response.Dispose();
                    continue;
                }

                response.EnsureSuccessStatusCode();
                return response;
            }

            throw new WebException("Vượt quá giới hạn số lần redirect tối đa (5 lần).");
        }

        /// <summary>
        /// Tải nội dung file checksum SHA-256 từ GitHub Release.
        /// </summary>
        public static async Task<string> DownloadExpectedChecksumAsync(string sha256Url, CancellationToken ct)
        {
            using (HttpClient client = CreateDownloadClient())
            {
                using (HttpResponseMessage response = await SendRequestWithSafeRedirectsAsync(client, sha256Url, ct).ConfigureAwait(false))
                {
                    byte[] bytes = await ReadBoundedAsync(response.Content, MaxChecksumBytes, ct).ConfigureAwait(false);
                    if (bytes.Length > MaxChecksumBytes)
                    {
                        throw new InvalidDataException("Kích thước file checksum vượt quá giới hạn an toàn.");
                    }

                    string content = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF').Trim();
                    // Định dạng: "<64 hex hash>  TTSK-Dim-Plates-Portable.zip"
                    Match match = Regex.Match(content, @"\A([a-fA-F0-9]{64})[ \t]+([^\r\n]+)\z");
                    if (!match.Success)
                    {
                        throw new InvalidDataException(string.Format("Nội dung file checksum SHA256 không hợp lệ: '{0}'", content));
                    }

                    string expectedFile = match.Groups[2].Value.Trim();
                    if (!string.Equals(expectedFile, GitHubReleaseService.ExpectedZipAssetName, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(string.Format("Tên file trong checksum không khớp: '{0}', mong đợi: '{1}'.", expectedFile, GitHubReleaseService.ExpectedZipAssetName));
                    }

                    return match.Groups[1].Value.ToLowerInvariant();
                }
            }
        }

        /// <summary>
        /// Tải file ZIP gói cập nhật với tính toán mã băm SHA256 trực tiếp từ stream, lưu vào .partial trước khi đổi tên.
        /// </summary>
        public static async Task DownloadZipPackageAsync(
            string zipUrl,
            string targetZipPath,
            string expectedSha256,
            IProgress<UpdateDownloadProgress> progress,
            CancellationToken ct)
        {
            string partialPath = targetZipPath + ".partial";
            string dir = Path.GetDirectoryName(targetZipPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (File.Exists(partialPath))
            {
                File.Delete(partialPath);
            }
            if (File.Exists(targetZipPath))
            {
                File.Delete(targetZipPath);
            }

            using (HttpClient client = CreateDownloadClient())
            {
                using (HttpResponseMessage response = await SendRequestWithSafeRedirectsAsync(client, zipUrl, ct).ConfigureAwait(false))
                {
                    long? totalBytes = response.Content.Headers.ContentLength;
                    if (totalBytes.HasValue && totalBytes.Value > MaxZipBytes)
                    {
                        throw new InvalidDataException(string.Format("Kích thước file ZIP ({0} bytes) vượt quá giới hạn tối đa ({1} bytes).", totalBytes.Value, MaxZipBytes));
                    }

                    using (Stream networkStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (FileStream fileStream = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
                    using (SHA256 sha256 = SHA256.Create())
                    using (CryptoStream cryptoStream = new CryptoStream(fileStream, sha256, CryptoStreamMode.Write))
                    {
                        byte[] buffer = new byte[65536];
                        long receivedBytes = 0;
                        int bytesRead;
                        var elapsed = System.Diagnostics.Stopwatch.StartNew();

                        while ((bytesRead = await ReadWithTimeoutAsync(networkStream, buffer, ct).ConfigureAwait(false)) > 0)
                        {
                            if (elapsed.Elapsed.TotalSeconds > DownloadTimeoutSeconds) throw new TimeoutException("Download exceeded total timeout.");
                            await cryptoStream.WriteAsync(buffer, 0, bytesRead, ct).ConfigureAwait(false);
                            receivedBytes += bytesRead;

                            if (receivedBytes > MaxZipBytes)
                            {
                                throw new InvalidDataException("Kích thước dữ liệu tải về vượt quá giới hạn an toàn tối đa.");
                            }

                            if (progress != null)
                            {
                                int pct = totalBytes.HasValue && totalBytes.Value > 0 ? (int)((receivedBytes * 100) / totalBytes.Value) : 0;
                                progress.Report(new UpdateDownloadProgress
                                {
                                    BytesReceived = receivedBytes,
                                    TotalBytesToReceive = totalBytes ?? receivedBytes,
                                    ProgressPercentage = pct,
                                    StatusMessage = string.Format("Đang tải gói cập nhật: {0:F1} MB / {1:F1} MB", receivedBytes / (1024.0 * 1024.0), (totalBytes ?? receivedBytes) / (1024.0 * 1024.0))
                                });
                            }
                        }

                        cryptoStream.FlushFinalBlock();
                        string actualHash = BitConverter.ToString(sha256.Hash).Replace("-", "").ToLowerInvariant();

                        if (!string.Equals(actualHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidDataException(string.Format("Mã hash SHA256 của file ZIP không khớp! Kỳ vọng: {0}, Thực tế: {1}", expectedSha256, actualHash));
                        }
                    }
                }
            }

            // Đổi tên từ .partial sang .zip sau khi đã xác thực hash 100%
            File.Move(partialPath, targetZipPath);
        }

        /// <summary>
        /// Giải nén an toàn file ZIP vào thư mục staging, kiểm tra chặt chẽ cấu trúc và tính toàn vẹn.
        /// </summary>
        public static void ExtractAndValidatePackage(string zipFilePath, string stagingDirectory, out ReleaseMetadata metadata)
        {
            if (!File.Exists(zipFilePath))
            {
                throw new FileNotFoundException("Không tìm thấy file ZIP gói cập nhật.", zipFilePath);
            }

            UpdateSecurity.AssertNoReparseAncestors(stagingDirectory);
            if (Directory.Exists(stagingDirectory)) throw new IOException("Staging must be a fresh directory.");
            Directory.CreateDirectory(stagingDirectory);

            long totalUncompressedBytes = 0;
            const string ExpectedRootPrefix = "TTSK Dim Plates/";

            using (FileStream zipStream = new FileStream(zipFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
            {
                if (archive.Entries.Count > MaxEntryCount)
                {
                    throw new InvalidDataException(string.Format("Số lượng entries trong ZIP ({0}) vượt quá giới hạn an toàn ({1}).", archive.Entries.Count, MaxEntryCount));
                }

                var seenEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Inspect every entry before extracting any file. Directory/file collisions and aliases are rejected.
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                long declaredTotal = 0;
                foreach (var item in archive.Entries)
                {
                    string name = item.FullName.Replace('\\', '/');
                    if (((item.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (item.ExternalAttributes & 0x400) != 0)
                        throw new InvalidDataException("ZIP links/reparse entries are not allowed.");
                    if (name == ExpectedRootPrefix) continue;
                    if (!name.StartsWith(ExpectedRootPrefix, StringComparison.Ordinal)) throw new InvalidDataException("Unexpected ZIP root.");
                    string relative = name.Substring(ExpectedRootPrefix.Length).TrimEnd('/');
                    if (!UpdateSecurity.IsSafeRelativePath(relative) || !names.Add(relative)) throw new InvalidDataException("Invalid or duplicate ZIP entry.");
                    if (!name.EndsWith("/")) files.Add(relative);
                    if (item.Length > MaxUncompressedBytesPerFile) throw new InvalidDataException("Oversized ZIP entry.");
                    declaredTotal += item.Length;
                    if (declaredTotal > MaxUncompressedBytesTotal) throw new InvalidDataException("Oversized ZIP contents.");
                }
                foreach (string name in names)
                {
                    string parent = name;
                    while (parent.Contains("/"))
                    { parent = parent.Substring(0, parent.LastIndexOf('/')); if (files.Contains(parent)) throw new InvalidDataException("ZIP file/directory collision."); }
                }

                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string entryFullName = entry.FullName.Replace('\\', '/');

                    // Bỏ qua entry thư mục gốc rỗng
                    if (entryFullName.Equals("TTSK Dim Plates/", StringComparison.OrdinalIgnoreCase) ||
                        entryFullName.Equals("TTSK Dim Plates", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Toàn bộ entry bắt buộc phải nằm trong thư mục gốc TTSK Dim Plates/
                    if (!entryFullName.StartsWith(ExpectedRootPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(string.Format("Entry trong ZIP không nằm trong thư mục gốc 'TTSK Dim Plates/': '{0}'", entry.FullName));
                    }

                    string relativePath = entryFullName.Substring(ExpectedRootPrefix.Length);
                    if (string.IsNullOrWhiteSpace(relativePath))
                    {
                        continue;
                    }

                    // Nếu là thư mục con
                    if (relativePath.EndsWith("/"))
                    {
                        string subDir = UpdateSecurity.GetSafeFullPath(stagingDirectory, relativePath.TrimEnd('/'));
                        if (!Directory.Exists(subDir))
                        {
                            Directory.CreateDirectory(subDir);
                        }
                        continue;
                    }

                    // Kiểm tra tính an toàn của đường dẫn file
                    if (!UpdateSecurity.IsSafeRelativePath(relativePath))
                    {
                        throw new InvalidDataException(string.Format("Đường dẫn file trong ZIP không an toàn: '{0}'", relativePath));
                    }

                    if (!seenEntries.Add(relativePath))
                    {
                        throw new InvalidDataException(string.Format("Phát hiện entry bị trùng lặp trong ZIP: '{0}'", relativePath));
                    }

                    if (entry.Length > MaxUncompressedBytesPerFile)
                    {
                        throw new InvalidDataException(string.Format("Kích thước giải nén của file '{0}' vượt quá giới hạn an toàn 100MB.", relativePath));
                    }

                    totalUncompressedBytes += entry.Length;
                    if (totalUncompressedBytes > MaxUncompressedBytesTotal)
                    {
                        throw new InvalidDataException("Tổng dung lượng giải nén của gói ZIP vượt quá giới hạn an toàn 500MB.");
                    }

                    string destFullPath = UpdateSecurity.GetSafeFullPath(stagingDirectory, relativePath);
                    string destDir = Path.GetDirectoryName(destFullPath);
                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }

                    // Giải nén entry ra đĩa
                    using (var input = entry.Open())
                    using (var output = new FileStream(destFullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        byte[] buffer = new byte[65536]; int count; long written = 0;
                        while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            written += count;
                            if (written > entry.Length || written > MaxUncompressedBytesPerFile) throw new InvalidDataException("ZIP entry exceeds declared size.");
                            output.Write(buffer, 0, count);
                        }
                        if (written != entry.Length) throw new InvalidDataException("Truncated ZIP entry.");
                    }
                }
            }

            // Đọc và xác thực release.json từ staging
            string releaseJsonPath = Path.Combine(stagingDirectory, ReleaseMetadata.ReleaseJsonFileName);
            if (!File.Exists(releaseJsonPath))
            {
                throw new InvalidDataException("Gói cập nhật thiếu file release.json bắt buộc.");
            }

            metadata = ReleaseMetadata.LoadFromFile(releaseJsonPath);
            if (metadata == null)
            {
                throw new InvalidDataException("Không thể phân tích nội dung release.json.");
            }

            // Xác thực toàn bộ thư mục staging đối chiếu với release.json và SHA256.csv
            string error;
            if (!UpdateChecksumManifest.ValidateDirectory(stagingDirectory, metadata, out error, true))
            {
                throw new InvalidDataException(string.Format("Xác thực gói cập nhật thất bại: {0}", error));
            }
        }

        private static async Task<byte[]> ReadBoundedAsync(HttpContent content, long limit, CancellationToken ct)
        {
            using (var input = await content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var output = new MemoryStream())
            {
                byte[] buffer = new byte[4096]; int count;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                while ((count = await ReadWithTimeoutAsync(input, buffer, ct).ConfigureAwait(false)) > 0)
                { if (watch.Elapsed.TotalSeconds > 30) throw new TimeoutException("Checksum download timed out."); if (output.Length + count > limit) throw new InvalidDataException("Response exceeds limit."); output.Write(buffer, 0, count); }
                return output.ToArray();
            }
        }

        private static async Task<int> ReadWithTimeoutAsync(Stream input, byte[] buffer, CancellationToken ct)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                using (timeout.Token.Register(() => input.Dispose()))
                    return await input.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false);
            }
        }
    }
}
