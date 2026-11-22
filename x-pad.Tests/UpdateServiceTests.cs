using x_pad.Services;

namespace x_pad.Tests;

public class UpdateServiceTests
{
    [Theory]
    [InlineData("v0.2.10", "0.2.3", true)]
    [InlineData("0.2.4", "0.2.3", true)]
    [InlineData("v0.2.3", "0.2.3", false)]
    [InlineData("0.2.3", "0.2.3", false)]
    [InlineData("v0.2.2", "0.2.3", false)]
    [InlineData("1.0.0", "0.9.9", true)]
    [InlineData("v1.0.0-beta", "0.9.0", true)]
    public void IsNewerVersion_ComparesSemVerLabels(string latest, string current, bool expected)
    {
        Assert.Equal(expected, UpdateService.IsNewerVersion(latest, current));
    }

    [Theory]
    [InlineData("v1.2.3", true)]
    [InlineData("1.2.3", true)]
    [InlineData("10.0", true)]
    [InlineData("not-a-version", false)]
    [InlineData("", false)]
    public void TryParseVersion_HandlesTags(string raw, bool ok)
    {
        Assert.Equal(ok, UpdateService.TryParseVersion(raw, out _));
    }

    [Fact]
    public void SelectAssetUrl_PrefersManifestAsset()
    {
        var assets = new[]
        {
            new GitHubReleaseAsset
            {
                Name = "xpad-win-x64.zip",
                BrowserDownloadUrl = "https://github.com/involvex/x-pad/releases/download/v0.2.4/xpad-win-x64.zip"
            },
            new GitHubReleaseAsset
            {
                Name = "xpad-win-x64-selfcontained.zip",
                BrowserDownloadUrl = "https://github.com/involvex/x-pad/releases/download/v0.2.4/xpad-win-x64-selfcontained.zip"
            }
        };

        var url = UpdateService.SelectAssetUrl("xpad-win-x64-selfcontained.zip", assets);
        Assert.Equal(
            "https://github.com/involvex/x-pad/releases/download/v0.2.4/xpad-win-x64-selfcontained.zip",
            url);
    }

    [Fact]
    public void SelectAssetUrl_FallsBackToFrameworkZip()
    {
        var assets = new[]
        {
            new GitHubReleaseAsset
            {
                Name = "xpad-win-x64.zip",
                BrowserDownloadUrl = "https://github.com/involvex/x-pad/releases/download/v0.2.4/xpad-win-x64.zip"
            }
        };

        var url = UpdateService.SelectAssetUrl("missing.zip", assets);
        Assert.Equal(
            "https://github.com/involvex/x-pad/releases/download/v0.2.4/xpad-win-x64.zip",
            url);
    }

    [Fact]
    public void ParseReleaseJson_ReadsTagAndAssets()
    {
        const string json = """
            {
              "tag_name": "v0.2.4",
              "html_url": "https://github.com/involvex/x-pad/releases/tag/v0.2.4",
              "body": "Fixes",
              "assets": [
                {
                  "name": "xpad-win-x64.zip",
                  "browser_download_url": "https://github.com/involvex/x-pad/releases/download/v0.2.4/xpad-win-x64.zip"
                }
              ]
            }
            """;

        var release = UpdateService.ParseReleaseJson(json);
        Assert.NotNull(release);
        Assert.Equal("v0.2.4", release!.TagName);
        Assert.Single(release.Assets!);
        Assert.Equal("xpad-win-x64.zip", release.Assets![0].Name);
    }

    [Fact]
    public void ParseInstallManifest_ReadsAssetAndVersion()
    {
        var manifest = UpdateService.ParseInstallManifest("""{"asset":"xpad-win-x64.zip","version":"0.2.3"}""");
        Assert.NotNull(manifest);
        Assert.Equal("xpad-win-x64.zip", manifest!.Asset);
        Assert.Equal("0.2.3", manifest.Version);
    }

    [Theory]
    [InlineData("https://github.com/involvex/x-pad/releases/download/v1/x.zip", true)]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset/1", true)]
    [InlineData("https://release-assets.githubusercontent.com/a.zip", true)]
    [InlineData("https://evil.example/x.zip", false)]
    [InlineData("http://github.com/involvex/x-pad/x.zip", false)]
    public void IsAllowedDownloadUrl_RestrictsHosts(string url, bool allowed)
    {
        Assert.Equal(allowed, UpdateService.IsAllowedDownloadUrl(url));
    }

    [Fact]
    public void NormalizeVersionLabel_StripsVPrefix()
    {
        Assert.Equal("0.2.4", UpdateService.NormalizeVersionLabel("v0.2.4"));
        Assert.Equal("0.2.4", UpdateService.NormalizeVersionLabel("0.2.4"));
    }
}
