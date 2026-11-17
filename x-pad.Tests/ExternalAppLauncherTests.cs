using System.IO;
using x_pad.Services;
using Xunit;

namespace x_pad.Tests;

public class ExternalAppLauncherTests
{
    [Fact]
    public void ResolveCommand_finds_file_on_extra_path()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xpad-launcher-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var exe = Path.Combine(dir, OperatingSystem.IsWindows() ? "fakecode.cmd" : "fakecode");
            File.WriteAllText(exe, "echo");
            var found = ExternalAppLauncher.ResolveCommand("fakecode", [dir]);
            Assert.Equal(exe, found);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ResolveCommand_returns_null_when_missing()
    {
        var found = ExternalAppLauncher.ResolveCommand("definitely-not-a-real-xpad-tool-xyz");
        Assert.Null(found);
    }
}
