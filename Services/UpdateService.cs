using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using App = x_pad.App;

namespace x_pad.Services;

public sealed class InstallManifest
{
    [JsonPropertyName("asset")]
    public string Asset { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";
}

public sealed class UpdateCheckResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public bool UpdateAvailable { get; init; }
    public string? CurrentVersion { get; init; }
    public string? LatestVersion { get; init; }
    public string? ReleaseNotes { get; init; }
    public string? ReleaseUrl { get; init; }
    public string? DownloadUrl { get; init; }
    public string? AssetName { get; init; }
    public bool CanApplyInPlace { get; init; }
}

public sealed class UpdateService
{
    public const string Owner = "involvex";
    public const string Repo = "x-pad";
    public const string InstallManifestFileName = "xpad.install.json";
    public const string DefaultFrameworkAsset = "xpad-win-x64.zip";
    public const string DefaultSelfContainedAsset = "xpad-win-x64-selfcontained.zip";

    private static readonly HashSet<string> AllowedDownloadHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com"
    };

    private readonly HttpClient _http;
    private readonly string _installDirectory;
    private readonly Func<string?> _readManifestAsset;

    public UpdateService(HttpClient? http = null, string? installDirectory = null, Func<string?>? readManifestAsset = null)
    {
        _http = http ?? CreateDefaultClient();
        _installDirectory = installDirectory ?? AppContext.BaseDirectory;
        _readManifestAsset = readManifestAsset ?? ReadManifestAssetName;
    }

    public static HttpClient CreateDefaultClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("X-Pad-Updater");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public string GetCurrentVersion() =>
        typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public bool CanApplyInPlace()
    {
        var asset = _readManifestAsset();
        if (string.IsNullOrWhiteSpace(asset))
            return false;

        // Refuse to overwrite a local build output folder.
        var dir = Path.GetFullPath(_installDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !dir.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }

    public string? GetPreferredAssetName() =>
        _readManifestAsset() ?? DefaultFrameworkAsset;

    public static bool IsNewerVersion(string latestTagOrVersion, string currentVersion)
    {
        if (!TryParseVersion(latestTagOrVersion, out var latest))
            return false;
        if (!TryParseVersion(currentVersion, out var current))
            return true;
        return latest > current;
    }

    public static bool TryParseVersion(string raw, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var s = raw.Trim();
        if (s.StartsWith('v') || s.StartsWith('V'))
            s = s[1..];

        var plus = s.IndexOf('+');
        if (plus >= 0)
            s = s[..plus];
        var dash = s.IndexOf('-');
        if (dash >= 0)
            s = s[..dash];

        var parts = s.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts.Length > 4)
            return false;

        var nums = new int[4];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out nums[i]))
                return false;
        }

        version = new Version(nums[0], nums[1], nums[2], nums[3]);
        return true;
    }

    public static string? SelectAssetUrl(string preferredAssetName, IEnumerable<GitHubReleaseAsset> assets)
    {
        var preferred = assets.FirstOrDefault(a =>
            string.Equals(a.Name, preferredAssetName, StringComparison.OrdinalIgnoreCase));
        if (preferred != null && !string.IsNullOrWhiteSpace(preferred.BrowserDownloadUrl))
            return preferred.BrowserDownloadUrl;

        var framework = assets.FirstOrDefault(a =>
            string.Equals(a.Name, DefaultFrameworkAsset, StringComparison.OrdinalIgnoreCase));
        if (framework != null && !string.IsNullOrWhiteSpace(framework.BrowserDownloadUrl))
            return framework.BrowserDownloadUrl;

        var sc = assets.FirstOrDefault(a =>
            string.Equals(a.Name, DefaultSelfContainedAsset, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(sc?.BrowserDownloadUrl) ? null : sc!.BrowserDownloadUrl;
    }

    public static bool IsAllowedDownloadUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        if (uri.Scheme != Uri.UriSchemeHttps)
            return false;
        return AllowedDownloadHosts.Contains(uri.Host);
    }

    public async Task<UpdateCheckResult> CheckLatestAsync(CancellationToken ct = default)
    {
        var current = GetCurrentVersion();
        try
        {
            using var response = await _http.GetAsync(
                $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest",
                ct);
            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult
                {
                    Success = false,
                    CurrentVersion = current,
                    Error = $"GitHub returned {(int)response.StatusCode}"
                };
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, JsonOptions, ct);
            if (release == null || string.IsNullOrWhiteSpace(release.TagName))
            {
                return new UpdateCheckResult
                {
                    Success = false,
                    CurrentVersion = current,
                    Error = "Could not parse release metadata"
                };
            }

            var preferred = GetPreferredAssetName() ?? DefaultFrameworkAsset;
            var downloadUrl = SelectAssetUrl(preferred, release.Assets ?? []);
            var assetName = (release.Assets ?? [])
                .FirstOrDefault(a => string.Equals(a.BrowserDownloadUrl, downloadUrl, StringComparison.OrdinalIgnoreCase))
                ?.Name;
            var updateAvailable = IsNewerVersion(release.TagName, current);

            return new UpdateCheckResult
            {
                Success = true,
                CurrentVersion = current,
                LatestVersion = NormalizeVersionLabel(release.TagName),
                ReleaseNotes = release.Body ?? "",
                ReleaseUrl = release.HtmlUrl,
                DownloadUrl = downloadUrl,
                AssetName = assetName,
                UpdateAvailable = updateAvailable,
                CanApplyInPlace = updateAvailable && CanApplyInPlace() && IsAllowedDownloadUrl(downloadUrl)
            };
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult
            {
                Success = false,
                CurrentVersion = current,
                Error = ex.Message
            };
        }
    }

    public async Task ApplyUpdateAsync(UpdateCheckResult check, CancellationToken ct = default)
    {
        if (!check.CanApplyInPlace || string.IsNullOrWhiteSpace(check.DownloadUrl))
            throw new InvalidOperationException("In-place update is not available for this install.");
        if (!IsAllowedDownloadUrl(check.DownloadUrl))
            throw new InvalidOperationException("Download URL is not allowed.");

        var tempRoot = Path.Combine(Path.GetTempPath(), "xpad-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var zipPath = Path.Combine(tempRoot, check.AssetName ?? "update.zip");
        var extractDir = Path.Combine(tempRoot, "extract");

        try
        {
            await DownloadFileAsync(check.DownloadUrl, zipPath, ct);
            ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);

            var exe = FindXpadExe(extractDir)
                       ?? throw new InvalidOperationException("Downloaded zip does not contain xpad.exe.");

            var sourceDir = Path.GetDirectoryName(exe)!;
            var targetDir = Path.GetFullPath(_installDirectory);
            var pid = Environment.ProcessId;
            var scriptPath = Path.Combine(tempRoot, "apply-update.ps1");
            var script = BuildApplyScript(pid, sourceDir, targetDir, Path.Combine(targetDir, "xpad.exe"));
            await File.WriteAllTextAsync(scriptPath, script, ct);

            var start = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = tempRoot
            };
            Process.Start(start);
        }
        catch
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* ignore */ }
            throw;
        }
    }

    public static string NormalizeVersionLabel(string tagOrVersion)
    {
        var s = tagOrVersion.Trim();
        if (s.StartsWith('v') || s.StartsWith('V'))
            s = s[1..];
        return s;
    }

    public static InstallManifest? ParseInstallManifest(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<InstallManifest>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static GitHubRelease? ParseReleaseJson(string json) =>
        JsonSerializer.Deserialize<GitHubRelease>(json, JsonOptions);

    private string? ReadManifestAssetName()
    {
        try
        {
            var path = Path.Combine(_installDirectory, InstallManifestFileName);
            if (!File.Exists(path))
                return null;
            var json = File.ReadAllText(path);
            var manifest = ParseInstallManifest(json);
            return string.IsNullOrWhiteSpace(manifest?.Asset) ? null : manifest!.Asset.Trim();
        }
        catch
        {
            return null;
        }
    }

    private async Task DownloadFileAsync(string url, string destPath, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = File.Create(destPath);
        await input.CopyToAsync(output, ct);
    }

    private static string? FindXpadExe(string root)
    {
        var direct = Path.Combine(root, "xpad.exe");
        if (File.Exists(direct))
            return direct;

        return Directory.EnumerateFiles(root, "xpad.exe", SearchOption.AllDirectories).FirstOrDefault();
    }

    private static string BuildApplyScript(int pid, string sourceDir, string targetDir, string exePath)
    {
        // Wait for the app to exit, copy files, then relaunch.
        return $$"""
            $ErrorActionPreference = 'Stop'
            $pidToWait = {{pid}}
            $source = '{{sourceDir.Replace("'", "''")}}'
            $target = '{{targetDir.Replace("'", "''")}}'
            $exe = '{{exePath.Replace("'", "''")}}'
            try {
              Wait-Process -Id $pidToWait -ErrorAction SilentlyContinue
            } catch {}
            Start-Sleep -Seconds 1
            Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse -Force
            Start-Process -FilePath $exe
            """;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}

public sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubReleaseAsset>? Assets { get; set; }
}

public sealed class GitHubReleaseAsset
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }
}
