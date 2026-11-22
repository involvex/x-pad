using x_pad.Services;

namespace x_pad.Tests;

public class WorkspaceServiceTests
{
    [Fact]
    public void DefaultExcludeNames_include_git_and_node_modules()
    {
        Assert.Contains(".git", WorkspaceService.DefaultExcludeNames);
        Assert.Contains("node_modules", WorkspaceService.DefaultExcludeNames);
        Assert.Contains("bin", WorkspaceService.DefaultExcludeNames);
        Assert.Contains("obj", WorkspaceService.DefaultExcludeNames);
        Assert.Contains(".vs", WorkspaceService.DefaultExcludeNames);
    }

    [Fact]
    public void TryOpen_sets_root_for_existing_directory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xpad-ws-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var ws = new WorkspaceService();
            var changed = 0;
            ws.Changed += (_, _) => changed++;

            Assert.True(ws.TryOpen(dir));
            Assert.Equal(Path.GetFullPath(dir), ws.Root, ignoreCase: OperatingSystem.IsWindows());
            Assert.Equal(1, changed);

            ws.Close();
            Assert.Null(ws.Root);
            Assert.Equal(2, changed);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TryOpen_rejects_missing_and_file_paths()
    {
        var ws = new WorkspaceService();
        Assert.False(ws.TryOpen(Path.Combine(Path.GetTempPath(), "xpad-missing-" + Guid.NewGuid().ToString("N"))));

        var file = Path.Combine(Path.GetTempPath(), "xpad-ws-file-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(file, "x");
        try
        {
            Assert.False(ws.TryOpen(file));
            Assert.Null(ws.Root);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
