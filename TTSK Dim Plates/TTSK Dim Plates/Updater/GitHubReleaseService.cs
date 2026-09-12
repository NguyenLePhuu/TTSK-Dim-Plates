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

namespace TTSK_AutoDim_Plates.Updater
{
    /// <summary>
    /// Thông tin về bản phát hành trên GitHub Release.
    /// </summary>
    public sealed class GitHubReleaseInfo
    {
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
