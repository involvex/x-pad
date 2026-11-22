using x_pad.Services;

namespace x_pad.Tests;

public class FormatCommandHelperTests
{
    [Fact]
    public void ExpandCommand_replaces_file_token()
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "sample.js"));
        var expanded = FormatCommandHelper.ExpandCommand("bunx prettier --write $FILE", path);
        Assert.Contains("bunx prettier --write", expanded, StringComparison.Ordinal);
        Assert.Contains(FormatCommandHelper.Quote(path), expanded, StringComparison.Ordinal);
        Assert.DoesNotContain("$FILE", expanded, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpandCommand_appends_path_when_token_missing()
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "a.ts"));
        var expanded = FormatCommandHelper.ExpandCommand("prettier --write", path);
        Assert.Equal($"prettier --write {FormatCommandHelper.Quote(path)}", expanded);
    }

    [Fact]
    public void MatchesExtension_empty_list_matches_all()
    {
        Assert.True(FormatCommandHelper.MatchesExtension(@"C:\x\file.ps1", ""));
        Assert.True(FormatCommandHelper.MatchesExtension(@"C:\x\file.ps1", null));
    }

    [Fact]
    public void MatchesExtension_filters_by_list()
    {
        Assert.True(FormatCommandHelper.MatchesExtension("app.ts", ".js,.ts,.json"));
        Assert.True(FormatCommandHelper.MatchesExtension("app.JS", "js, ts"));
        Assert.False(FormatCommandHelper.MatchesExtension("app.ps1", ".js,.ts,.json"));
        Assert.False(FormatCommandHelper.MatchesExtension("README", ".md"));
    }

    [Fact]
    public void Quote_wraps_path()
    {
        Assert.Equal("\"C:\\a b\\c.js\"", FormatCommandHelper.Quote(@"C:\a b\c.js"));
    }
}
