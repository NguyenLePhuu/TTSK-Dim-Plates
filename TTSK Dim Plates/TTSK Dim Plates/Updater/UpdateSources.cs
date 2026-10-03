// Đọc nguồn GitHub/Drive, chọn release và tải/giải nén có giới hạn.
// LUỒNG: provider đọc manifest → so sánh phiên bản → tải checksum/ZIP → ExtractAndValidatePackage.
// ĐIỂM SỬA: nguồn ở GitHubReleaseService và các provider; HTTP/giới hạn ZIP ở UpdateDownloader.
// THAM SỐ: DownloadTimeoutSeconds tính giây; Max*Bytes tính byte; MaxEntryCount giới hạn số mục ZIP.
// LƯU Ý: tên repo, product, ZIP và checksum phải đồng bộ UpdateManifest.cs và scripts phát hành; không sửa một phía.
// TRA CỨU: PROJECT_MAP.md ở gốc repo; chỉ mục kiểu/hàm và điểm chỉnh thông số.
// Mục lục:
//   Updater/GitHubReleaseService.cs
//   Updater/UpdateSources.cs
//   Updater/UpdateDownloader.cs

#region GitHubReleaseService - Đọc thông tin release GitHub


namespace TTSK_AutoDim_Plates.Updater
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web.Script.Serialization;

    /// <summary>
    /// Thông tin về bản phát hành trên GitHub Release.
    /// </summary>
    public sealed class GitHubReleaseInfo
    {
        public string Source { get; set; } = "GitHub";
        public string TagName { get; set; }
        public UpdateVersion Version { get; set; }
        public string Name { get; set; }
        public string Body { get; set; }
        public string HtmlUrl { get; set; }
        public string ZipDownloadUrl { get; set; }
        public long ZipSizeBytes { get; set; }
        public string Sha256DownloadUrl { get; set; }
        public long Sha256SizeBytes { get; set; }
        public DateTime PublishedAtUtc { get; set; }
    }

    /// <summary>
    /// Dịch vụ kết nối và kiểm tra bản phát hành mới nhất từ GitHub Releases API.
    /// </summary>
    public static class GitHubReleaseService
    {
        // HỢP ĐỒNG NGUỒN: owner/repo/API và tên asset phải khớp bộ phát hành, manifest và bộ kiểm URL.
        public const string RepositoryOwner = "NguyenLePhuu";
        public const string RepositoryName = "TTSK-Dim-Plates";
        public const string UserAgent = "TTSK-Dim-Plates-Updater/1.0";
        public const string LatestReleaseApiUrl = "https://api.github.com/repos/NguyenLePhuu/TTSK-Dim-Plates/releases/latest";
        public const string ExpectedZipAssetName = "TTSK-Dim-Plates-Portable.zip";
        public const string ExpectedSha256AssetName = "TTSK-Dim-Plates-Portable.zip.sha256";
        public const int MaxJsonResponseBytes = 2 * 1024 * 1024; // 2 MB

        /// <summary>
        /// Tạo HttpClient cấu hình chuẩn cho các lệnh gọi GitHub API.
        /// </summary>
        private static HttpClient CreateHttpClient(int timeoutSeconds)
        {
            // Thiết lập bảo mật TLS 1.2+ theo chuẩn của GitHub
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = false,
                MaxAutomaticRedirections = 5
            };

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(timeoutSeconds),
                MaxResponseContentBufferSize = MaxJsonResponseBytes
            };

            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            return client;
        }

        /// <summary>
        /// Kiểm tra bản phát hành chính thức mới nhất một cách bất đồng bộ.
        /// </summary>
        public static async Task<GitHubReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            using (HttpClient client = CreateHttpClient(10))
            {
                HttpResponseMessage response;
                try
                {
                    response = await client.GetAsync(LatestReleaseApiUrl, cancellationToken).ConfigureAwait(false);
                }
                catch (WebException wex) when (wex.Response is HttpWebResponse httpRes && httpRes.StatusCode == HttpStatusCode.NotFound)
                {
                    // 404: Chưa có release nào hoặc repo chưa publish
                    return null;
                }
                catch (HttpRequestException ex)
                {
                    if (ex.Message.IndexOf("404", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return null;
                    }
                    throw;
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    response.Dispose();
                    throw new InvalidDataException("Chưa có Release công khai hoặc không truy cập được repository (404).");
                }

                response.EnsureSuccessStatusCode();

                // Đọc nội dung JSON với giới hạn kích thước an toàn
                byte[] jsonBytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                if (jsonBytes.Length > MaxJsonResponseBytes)
                {
                    throw new InvalidDataException("Kích thước phản hồi từ GitHub API vượt quá giới hạn an toàn 2MB.");
                }

                string json = Encoding.UTF8.GetString(jsonBytes);
                response.Dispose();
                return ParseReleaseJson(json);
            }
        }

        /// <summary>
        /// Phân tích và kiểm tra các tiêu chuẩn an toàn của JSON phản hồi từ GitHub Release.
        /// </summary>
        public static GitHubReleaseInfo ParseReleaseJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                var serializer = new JavaScriptSerializer();
                var data = serializer.Deserialize<Dictionary<string, object>>(json);
                if (data == null)
                {
                    return null;
                }

                // Bỏ qua bản nháp (draft) hoặc bản phát hành trước (prerelease)
                if (!data.ContainsKey("draft") || !(data["draft"] is bool) || !data.ContainsKey("prerelease") || !(data["prerelease"] is bool)) return null;
                if (data.ContainsKey("draft") && data["draft"] is bool isDraft && isDraft)
                {
                    return null;
                }
                if (data.ContainsKey("prerelease") && data["prerelease"] is bool isPre && isPre)
                {
                    return null;
                }

                string tagName = data.ContainsKey("tag_name") ? Convert.ToString(data["tag_name"]) : null;
                if (string.IsNullOrWhiteSpace(tagName) || !UpdateVersion.TryParse(tagName, out UpdateVersion releaseVersion))
                {
                    return null;
                }
                if (tagName != "v" + releaseVersion.ToString()) return null;

                string releaseName = data.ContainsKey("name") ? Convert.ToString(data["name"]) : tagName;
                string releaseBody = data.ContainsKey("body") ? Convert.ToString(data["body"]) : string.Empty;
                string htmlUrl = data.ContainsKey("html_url") ? Convert.ToString(data["html_url"]) : string.Empty;

                DateTime publishedAt = DateTime.UtcNow;
                if (data.ContainsKey("published_at") && DateTime.TryParse(Convert.ToString(data["published_at"]), out DateTime parsedDate))
                {
                    publishedAt = parsedDate.ToUniversalTime();
                }

                // Kiểm tra danh sách assets: bắt buộc phải có đúng 2 assets chính thức
                if (!data.ContainsKey("assets") || !(data["assets"] is ArrayList assetsList))
                {
                    return null;
                }

                string zipUrl = null;
                long zipSize = 0;
                string shaUrl = null;
                long shaSize = 0;
                int validAssetCount = 0;

                foreach (object assetObj in assetsList)
                {
                    if (!(assetObj is Dictionary<string, object> asset))
                    {
                        continue;
                    }

                    string assetName = asset.ContainsKey("name") ? Convert.ToString(asset["name"]) : null;
                    string state = asset.ContainsKey("state") ? Convert.ToString(asset["state"]) : null;
                    string downloadUrl = asset.ContainsKey("browser_download_url") ? Convert.ToString(asset["browser_download_url"]) : null;
                    long size = 0;
                    if (asset.ContainsKey("size"))
                    {
                        long.TryParse(Convert.ToString(asset["size"]), out size);
                    }

                    // Chỉ nhận asset ở trạng thái uploaded hoàn tất
                    if (!string.Equals(state, "uploaded", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (string.Equals(assetName, ExpectedZipAssetName, StringComparison.Ordinal))
                    {
                        ValidateExactAssetUrl(downloadUrl, tagName, ExpectedZipAssetName);
                        zipUrl = downloadUrl;
                        zipSize = size;
                        validAssetCount++;
                    }
                    else if (string.Equals(assetName, ExpectedSha256AssetName, StringComparison.Ordinal))
                    {
                        ValidateExactAssetUrl(downloadUrl, tagName, ExpectedSha256AssetName);
                        shaUrl = downloadUrl;
                        shaSize = size;
                        validAssetCount++;
                    }
                }

                // Phải có đủ cả 2 assets: ZIP và SHA256
                if (validAssetCount != 2 || string.IsNullOrEmpty(zipUrl) || string.IsNullOrEmpty(shaUrl))
                {
                    return null;
                }

                return new GitHubReleaseInfo
                {
                    TagName = tagName,
                    Version = releaseVersion,
                    Name = releaseName,
                    Body = releaseBody.Length > 20000 ? releaseBody.Substring(0, 20000) : releaseBody,
                    HtmlUrl = htmlUrl,
                    ZipDownloadUrl = zipUrl,
                    ZipSizeBytes = zipSize,
                    Sha256DownloadUrl = shaUrl,
                    Sha256SizeBytes = shaSize,
                    PublishedAtUtc = publishedAt
                };
            }
            catch
            {
                // Bắt mọi lỗi ngoại lệ phân tích cú pháp JSON không hợp lệ (ví dụ mã lỗi HTML 404 từ proxy)
                return null;
            }
        }

        /// <summary>
        /// Xác thực URL tải asset phải bắt nguồn từ domain chính thức của GitHub.
        /// </summary>
        public static void ValidateAssetDownloadUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("URL tải asset không được rỗng.");
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
            {
                throw new InvalidOperationException(string.Format("URL tải asset không hợp lệ: '{0}'", url));
            }

            if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("URL tải bắt buộc phải sử dụng giao thức HTTPS bảo mật.");
            }

            // Chặn userinfo trong URL (ví dụ https://user:pass@host)
            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                throw new InvalidOperationException("URL tải chứa thông tin xác thực bất thường (userinfo).");
            }

            // URL tải ban đầu từ GitHub Releases phải thuộc github.com
            string host = uri.Host.ToLowerInvariant();
            if (host != "github.com" || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            {
                throw new InvalidOperationException(string.Format("Host tải asset không được phép: '{0}'. Chỉ chấp nhận github.com.", host));
            }

            // Đường dẫn phải thuộc repository hiện tại
            string path = uri.AbsolutePath;
            string expectedPrefix = string.Format("/{0}/{1}/releases/download/", RepositoryOwner, RepositoryName);
            if (!path.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(string.Format("Đường dẫn tải asset không thuộc repository: '{0}'.", path));
            }
        }

        public static void ValidateExactAssetUrl(string url, string tag, string asset)
        {
            ValidateAssetDownloadUrl(url);
            string expected = "/" + RepositoryOwner + "/" + RepositoryName + "/releases/download/" + tag + "/" + asset;
            if (!new Uri(url).AbsolutePath.Equals(expected, StringComparison.Ordinal)) throw new InvalidDataException("Asset URL does not match selected tag/name.");
        }
    }
}
#endregion

#region UpdateSources - Chọn nguồn GitHub hoặc Google Drive


namespace TTSK_AutoDim_Plates.Updater
{
    using System;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web.Script.Serialization;
    using System.Xml;

    public sealed class DriveReleaseManifest
    {
        public int schemaVersion { get; set; }
        public string product { get; set; }
        public string version { get; set; }
        public string notes { get; set; }
        public string zipFileId { get; set; }
        public string sha256FileId { get; set; }
        public long zipSizeBytes { get; set; }
        public long sha256SizeBytes { get; set; }
    }

    public static class UpdateSources
    {
        public static string ReadSetting(string name)
        {
            var xml = new XmlDocument { XmlResolver = null };
            xml.Load(AppDomain.CurrentDomain.SetupInformation.ConfigurationFile);
            return xml.SelectSingleNode("/configuration/appSettings/add[@key='" + name + "']/@value")?.Value ?? "";
        }

        public static string DriveMediaUrl(string id, string apiKey = null)
        {
            if (id == null || !Regex.IsMatch(id, @"\A[A-Za-z0-9_-]+\z"))
                throw new InvalidDataException("Google Drive: invalid File ID.");
            // Public downloads never contain publisher credentials or an API key.
            return "https://drive.usercontent.google.com/download?id=" + id + "&export=download";
        }

        public static bool IsDriveMediaUrl(string url)
        {
            Uri uri;
            return Uri.TryCreate(url, UriKind.Absolute, out uri) && uri.Scheme == "https" &&
                uri.Host == "drive.usercontent.google.com" && uri.IsDefaultPort && uri.UserInfo == "" && uri.Fragment == "" &&
                uri.AbsolutePath == "/download" &&
                Regex.IsMatch(uri.Query, @"\A\?id=[A-Za-z0-9_-]+&export=download\z");
        }

        public static GitHubReleaseInfo ParseDriveManifest(string json, string apiKey = null)
        {
            var manifest = new JavaScriptSerializer().Deserialize<DriveReleaseManifest>(json);
            UpdateVersion version;
            if (manifest == null || manifest.schemaVersion != 1 || manifest.product != ReleaseMetadata.ExpectedProduct ||
                !UpdateVersion.TryParse(manifest.version, out version) || manifest.version != version.ToString() ||
                manifest.zipSizeBytes <= 0 || manifest.zipSizeBytes > UpdateDownloader.MaxZipBytes ||
                manifest.sha256SizeBytes <= 0 || manifest.sha256SizeBytes > UpdateDownloader.MaxChecksumBytes ||
                manifest.zipFileId == manifest.sha256FileId)
                throw new InvalidDataException("Google Drive: chỉ mục bản phát hành không hợp lệ.");
            return new GitHubReleaseInfo
            {
                Source = "GoogleDrive", Version = version, TagName = "v" + version,
                Name = "TTSK Dim Plates v" + version,
                Body = (manifest.notes ?? "").Substring(0, Math.Min(20000, (manifest.notes ?? "").Length)),
                HtmlUrl = "https://drive.google.com/file/d/" + manifest.zipFileId + "/view",
                ZipDownloadUrl = DriveMediaUrl(manifest.zipFileId, apiKey), ZipSizeBytes = manifest.zipSizeBytes,
                Sha256DownloadUrl = DriveMediaUrl(manifest.sha256FileId, apiKey), Sha256SizeBytes = manifest.sha256SizeBytes
            };
        }

        public static async Task<GitHubReleaseInfo> GetDriveReleaseAsync(CancellationToken ct)
        {
            string id = ReadSetting("GoogleDriveManifestId");
            if (string.IsNullOrWhiteSpace(id)) return null;
            string url = DriveMediaUrl(id);
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            using (var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }))
            {
                client.Timeout = TimeSpan.FromSeconds(15);
                client.MaxResponseContentBufferSize = GitHubReleaseService.MaxJsonResponseBytes;
                client.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true, NoStore = true };
                using (var response = await client.GetAsync(url, ct).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode) throw new IOException("Google Drive HTTP " + (int)response.StatusCode);
                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var fields = new JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string, object>>(json);
                    if (fields != null && fields.ContainsKey("initialized") && fields["initialized"] is bool &&
                        !(bool)fields["initialized"] && fields.ContainsKey("product") &&
                        Convert.ToString(fields["product"]) == ReleaseMetadata.ExpectedProduct) return null;
                    return ParseDriveManifest(json);
                }
            }
        }

        public static GitHubReleaseInfo SelectNewest(GitHubReleaseInfo github, GitHubReleaseInfo drive)
        {
            return drive != null && (github == null || drive.Version > github.Version) ? drive : github;
        }

        public static void ValidateDownloadSelection(GitHubReleaseInfo selected, GitHubReleaseInfo latest, UpdateVersion current)
        {
            if (selected == null || selected.Version == null || current == null)
                throw new InvalidDataException("Invalid selected release.");
            if (latest == null || latest.Version == null || latest.Version <= current)
                throw new InvalidDataException("No newer release is currently available.");
            // A source may fail between opening the dialog and clicking Download.
            // Never silently replace the version the user chose with an older one.
            if (latest.Version < selected.Version)
                throw new IOException("The selected release is temporarily unavailable. Retry later.");
        }

        public static void ValidateAssets(GitHubReleaseInfo release)
        {
            if (release.Source == "GoogleDrive")
            {
                if (!IsDriveMediaUrl(release.ZipDownloadUrl) || !IsDriveMediaUrl(release.Sha256DownloadUrl))
                    throw new InvalidDataException("Invalid Google Drive asset URL.");
            }
            else
            {
                GitHubReleaseService.ValidateExactAssetUrl(release.ZipDownloadUrl, release.TagName, GitHubReleaseService.ExpectedZipAssetName);
                GitHubReleaseService.ValidateExactAssetUrl(release.Sha256DownloadUrl, release.TagName, GitHubReleaseService.ExpectedSha256AssetName);
            }
        }

        private static async Task<GitHubReleaseInfo> TrySource(Func<Task<GitHubReleaseInfo>> fetch, Action<string> failure, CancellationToken ct)
        {
            try { return await fetch().ConfigureAwait(false); }
            catch (Exception) { ct.ThrowIfCancellationRequested(); failure("Không truy cập được nguồn cập nhật."); return null; }
        }

        public static async Task<GitHubReleaseInfo> GetLatestAsync(Action<string> warning, CancellationToken ct)
        {
            return await GetLatestAsync(async () => {
                var release = await GitHubReleaseService.GetLatestReleaseAsync(ct).ConfigureAwait(false);
                if (release == null) throw new InvalidDataException("Invalid GitHub stable release.");
                return release;
            },
                () => GetDriveReleaseAsync(ct), warning, ct).ConfigureAwait(false);
        }

        public static async Task<GitHubReleaseInfo> GetLatestAsync(Func<Task<GitHubReleaseInfo>> fetchGitHub,
            Func<Task<GitHubReleaseInfo>> fetchDrive, Action<string> warning, CancellationToken ct)
        {
            string githubError = null, driveError = null;
            var github = TrySource(fetchGitHub, e => githubError = "GitHub: " + e, ct);
            var drive = TrySource(fetchDrive, e => driveError = "Google Drive: " + e, ct);
            await Task.WhenAll(github, drive).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (githubError != null) warning(githubError);
            if (driveError != null) warning(driveError);
            var latest = SelectNewest(github.Result, drive.Result);
            if (latest == null) throw new IOException("Không tìm được bản phát hành hợp lệ từ GitHub / Google Drive. Kiểm tra mạng và cấu hình nguồn cập nhật.");
            return latest;
        }
    }
}
#endregion

#region UpdateDownloader - Tải và kiểm tra gói cập nhật


namespace TTSK_AutoDim_Plates.Updater
{
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
        // GIỚI HẠN TẢI/GIẢI NÉN: Max*Bytes tính byte; tăng có thể tăng bộ nhớ/đĩa và phạm vi gói được chấp nhận.
        public const long MaxZipBytes = 200 * 1024 * 1024; // 200 MB
        public const long MaxUncompressedBytesTotal = 500 * 1024 * 1024; // 500 MB
        public const long MaxUncompressedBytesPerFile = 100 * 1024 * 1024; // 100 MB
        // CHỈNH GIỚI HẠN: số mục ZIP tối đa; DownloadTimeoutSeconds là thời gian tải tổng, đơn vị giây.
        public const int MaxEntryCount = 500;
        public const int DownloadTimeoutSeconds = 300; // 5 phút

        private static readonly HashSet<string> AllowedCdnHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "github.com",
            "api.github.com",
            "objects.githubusercontent.com",
            "release-assets.githubusercontent.com",
            "github-releases.githubusercontent.com",
            "github-production-release-asset-2e65be.s3.amazonaws.com",
            // Google Drive API hosts — dùng cho tải gói cập nhật từ Google Drive
            "www.googleapis.com"
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
            bool isDrive = UpdateSources.IsDriveMediaUrl(initialUrl);
            if (!isDrive) GitHubReleaseService.ValidateAssetDownloadUrl(initialUrl);
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
                    if (isDrive) { response.Dispose(); throw new WebException("Google Drive download requires direct public access."); }
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
#endregion
