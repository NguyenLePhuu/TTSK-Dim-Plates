using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Xml;

namespace TTSK_AutoDim_Plates.Updater
{
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

        public static string DriveMediaUrl(string id, string apiKey)
        {
            if (id == null || !Regex.IsMatch(id, @"\A[A-Za-z0-9_-]+\z") || string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidDataException("Google Drive: thiếu File ID hoặc API key.");
            return "https://www.googleapis.com/drive/v3/files/" + id + "?alt=media&key=" + Uri.EscapeDataString(apiKey);
        }

        public static bool IsDriveMediaUrl(string url)
        {
            Uri uri;
            return Uri.TryCreate(url, UriKind.Absolute, out uri) && uri.Scheme == "https" &&
                uri.Host == "www.googleapis.com" && uri.IsDefaultPort && uri.UserInfo == "" && uri.Fragment == "" &&
                Regex.IsMatch(uri.AbsolutePath, @"\A/drive/v3/files/[A-Za-z0-9_-]+\z") &&
                Regex.IsMatch(uri.Query, @"\A\?alt=media&key=[A-Za-z0-9_%.-]+\z");
        }

        public static GitHubReleaseInfo ParseDriveManifest(string json, string apiKey)
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
            string id = ReadSetting("GoogleDriveManifestId"), key = ReadSetting("GoogleDriveApiKey");
            // Bỏ qua im lặng nếu thiếu manifest ID hoặc API key — chức năng Drive chưa được cấu hình
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(key)) return null;
            string url = DriveMediaUrl(id, key);
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
                    return ParseDriveManifest(json, key);
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
