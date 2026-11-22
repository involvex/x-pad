using x_pad.Services;

namespace x_pad.Tests;

public class CliArgsTests
{
    [Fact]
    public void Skips_flags_and_resolves_existing_relative_paths()
    {
        var temp = Path.Combine(Path.GetTempPath(), "xpad-cli-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(temp, "hi");
        try
        {
            var name = Path.GetFileName(temp);
            var dir = Path.GetDirectoryName(temp)!;
            var cwd = Environment.CurrentDirectory;
            Environment.CurrentDirectory = dir;
            try
            {
                var paths = CliArgs.ResolveFilePaths(["--verbose", "-h", name]);
                Assert.Single(paths);
                Assert.Equal(Path.GetFullPath(temp), paths[0], ignoreCase: OperatingSystem.IsWindows());
            }
            finally
            {
                Environment.CurrentDirectory = cwd;
            }
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public void Accepts_missing_file_in_existing_directory()
    {
        var dir = Path.GetTempPath();
        var missing = Path.Combine(dir, "xpad-missing-" + Guid.NewGuid().ToString("N") + ".txt");
        var paths = CliArgs.ResolveFilePaths([missing]);
        Assert.Single(paths);
        Assert.Equal(Path.GetFullPath(missing), paths[0], ignoreCase: OperatingSystem.IsWindows());
    }

    [Fact]
    public void Ignores_help_switches()
    {
        Assert.Empty(CliArgs.ResolveFilePaths(["/?", "--help", "-h"]));
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    [InlineData("/?")]
    public void Detects_help_request(string flag)
    {
        Assert.True(CliArgs.IsHelpRequest([flag]));
        Assert.False(CliArgs.IsHelpRequest(["file.txt"]));
        Assert.Contains("Usage:", CliArgs.GetHelpText(), StringComparison.Ordinal);
        Assert.Contains("folder", CliArgs.GetHelpText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveWorkspacePaths_returns_existing_directories()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xpad-ws-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var paths = CliArgs.ResolveWorkspacePaths([dir, "--help"]);
            Assert.Single(paths);
            Assert.Equal(Path.GetFullPath(dir), paths[0], ignoreCase: OperatingSystem.IsWindows());
            Assert.Empty(CliArgs.ResolveFilePaths([dir]));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
