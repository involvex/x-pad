using System.Text;
using x_pad.Services;

namespace x_pad.Tests;

public class DiskSearchTests
{
    [Fact]
    public void Search_finds_hit_and_skips_excluded_node_modules()
    {
        var root = Path.Combine(Path.GetTempPath(), "xpad-ds-" + Guid.NewGuid().ToString("N"));
        var src = Path.Combine(root, "src");
        var nm = Path.Combine(root, "node_modules", "pkg");
        Directory.CreateDirectory(src);
        Directory.CreateDirectory(nm);
        File.WriteAllText(Path.Combine(src, "a.txt"), "hello world");
        File.WriteAllText(Path.Combine(nm, "hidden.txt"), "hello hidden");

        try
        {
            var result = DiskSearch.Search(
                root,
                "hello",
                new TabSearchOptions(),
                WorkspaceService.DefaultExcludeNames,
                maxHits: 50);

            Assert.Single(result.Hits);
            Assert.Contains("a.txt", result.Hits[0].Path);
            Assert.False(result.Truncated);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Search_truncates_at_max_hits()
    {
        var root = Path.Combine(Path.GetTempPath(), "xpad-ds-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "a.txt"), "x x x x x");
        try
        {
            var result = DiskSearch.Search(root, "x", new TabSearchOptions(), maxHits: 2);
            Assert.Equal(2, result.Hits.Count);
            Assert.True(result.Truncated);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LooksBinary_null_byte_file_is_binary_utf8_bom_is_not()
    {
        var bin = Path.Combine(Path.GetTempPath(), "xpad-bin-" + Guid.NewGuid().ToString("N") + ".dat");
        var text = Path.Combine(Path.GetTempPath(), "xpad-txt-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            File.WriteAllBytes(bin, [0x41, 0x00, 0x42, 0x43, 0x00, 0x44]);
            File.WriteAllBytes(text, [0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i']);

            Assert.True(DiskSearch.LooksBinary(bin));
            Assert.False(DiskSearch.LooksBinary(text));
        }
        finally
        {
            if (File.Exists(bin)) File.Delete(bin);
            if (File.Exists(text)) File.Delete(text);
        }
    }

    [Fact]
    public void LooksBinary_utf16_le_bom_text_is_not_binary()
    {
        var path = Path.Combine(Path.GetTempPath(), "xpad-u16-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            File.WriteAllBytes(path, Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("AB")).ToArray());
            Assert.False(DiskSearch.LooksBinary(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
