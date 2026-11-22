using System.Text;
using x_pad.Services;

namespace x_pad.Tests;

public class DiskReplaceTests
{
    [Fact]
    public void Apply_dirty_open_tab_does_not_write_disk()
    {
        var path = Path.Combine(Path.GetTempPath(), "xpad-repl-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "foo on disk");
        try
        {
            var matches = TabSearch.Search("foo in memory", "foo", new TabSearchOptions()).Matches;
            var result = DiskReplace.Apply(
                path,
                matches,
                "bar",
                useRegex: false,
                pattern: null,
                new FileService(),
                openTabText: "foo in memory",
                openTabIsDirty: true);

            Assert.Equal(1, result.Count);
            Assert.Equal("bar in memory", result.OpenTabText);
            Assert.False(result.WroteToDisk);
            Assert.Equal("foo on disk", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Apply_writes_disk_and_preserves_utf8_bom()
    {
        var path = Path.Combine(Path.GetTempPath(), "xpad-repl-" + Guid.NewGuid().ToString("N") + ".txt");
        var encoding = FileService.CreateUtf8(false);
        File.WriteAllBytes(path, FileService.Encode("alpha beta", encoding, hasBom: true));
        try
        {
            var snapshot = new FileService().ReadFile(path);
            var matches = TabSearch.Search(snapshot.Text, "alpha", new TabSearchOptions()).Matches;
            var result = DiskReplace.Apply(
                path,
                matches,
                "gamma",
                useRegex: false,
                pattern: null,
                new FileService(),
                openTabText: null,
                openTabIsDirty: false);

            Assert.Equal(1, result.Count);
            Assert.True(result.WroteToDisk);
            var roundTrip = new FileService().ReadFile(path);
            Assert.True(roundTrip.HasBom);
            Assert.Equal("gamma beta", roundTrip.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
