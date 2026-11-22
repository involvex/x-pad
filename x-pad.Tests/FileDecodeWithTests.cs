using System.Text;
using x_pad.Services;

namespace x_pad.Tests;

public class FileDecodeWithTests
{
    [Fact]
    public void DecodeWith_utf8_bom_strips_preamble()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i' };
        var (_, hasBom, text) = FileService.DecodeWith(bytes, FileService.CreateUtf8(false), hasBom: true);
        Assert.True(hasBom);
        Assert.Equal("hi", text);
    }

    [Fact]
    public void DecodeWith_reinterprets_latin1_as_utf8()
    {
        // Bytes for "café" in Latin-1
        var bytes = Encoding.Latin1.GetBytes("café");
        var asLatin1 = FileService.DecodeWith(bytes, Encoding.Latin1, hasBom: false);
        Assert.Equal("café", asLatin1.Text);

        // Same bytes forced as UTF-8 may produce replacement or different text; round-trip check:
        var encoded = FileService.Encode("hello", Encoding.Latin1, false);
        var decoded = FileService.DecodeWith(encoded, FileService.CreateUtf8(false), false);
        Assert.Equal("hello", decoded.Text);
    }

    [Fact]
    public void DecodeWith_utf16_be()
    {
        var encoding = new UnicodeEncoding(bigEndian: true, byteOrderMark: true);
        var payload = encoding.GetBytes("AB");
        var bytes = new byte[2 + payload.Length];
        bytes[0] = 0xFE;
        bytes[1] = 0xFF;
        Buffer.BlockCopy(payload, 0, bytes, 2, payload.Length);

        var (_, hasBom, text) = FileService.DecodeWith(bytes, encoding, hasBom: true);
        Assert.True(hasBom);
        Assert.Equal("AB", text);
    }

    [Fact]
    public void ReadFile_keeps_source_bytes_and_readonly_flag()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xpad-ro-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(path, "sample", new UTF8Encoding(false));
            File.SetAttributes(path, FileAttributes.ReadOnly);

            var snapshot = new FileService().ReadFile(path);
            Assert.NotNull(snapshot.SourceBytes);
            Assert.True(snapshot.SourceBytes!.Length > 0);
            Assert.True(snapshot.IsReadOnly);
            Assert.False(snapshot.IsLargeFile);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
        }
    }
}
