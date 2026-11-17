using x_pad.Models;
using x_pad.Services;

namespace x_pad.Tests;

public class BackupOnSaveTests
{
    [Fact]
    public async Task WriteFileAsync_creates_bak_when_requested()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xpad-bak-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "note.txt");
        var bak = path + ".bak";
        try
        {
            await File.WriteAllTextAsync(path, "old");
            var service = new FileService();
            await service.WriteFileAsync(
                path,
                "new",
                FileService.CreateUtf8(false),
                false,
                LineEndingKind.Lf,
                backupExisting: true);

            Assert.True(File.Exists(bak));
            Assert.Equal("old", await File.ReadAllTextAsync(bak));
            Assert.Equal("new", await File.ReadAllTextAsync(path));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }
}
