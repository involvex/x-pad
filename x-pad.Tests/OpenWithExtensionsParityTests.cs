using System.Text.RegularExpressions;
using x_pad.Services;

namespace x_pad.Tests;

public class OpenWithExtensionsParityTests
{
    [Fact]
    public void Install_script_lists_match_OpenWithExtensions_Default()
    {
        var script = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "scripts", "install-open-with.ps1"));
        Assert.True(File.Exists(script), $"Missing {script}");

        var text = File.ReadAllText(script);
        var matches = Regex.Matches(text, @"\""(\.[A-Za-z0-9]+)\""");
        var fromScript = matches
            .Select(m => m.Groups[1].Value.ToLowerInvariant())
            .Where(e => e is not (".path" or ".exe"))
            .Distinct()
            .OrderBy(e => e)
            .ToArray();

        // Script also contains Resolve-Path / other dotted tokens; keep only known open-with set.
        var expected = OpenWithExtensions.Default
            .Select(e => e.ToLowerInvariant())
            .OrderBy(e => e)
            .ToArray();

        var scriptOpenWith = fromScript.Where(e => expected.Contains(e)).ToArray();
        Assert.Equal(expected, scriptOpenWith);
    }
}
