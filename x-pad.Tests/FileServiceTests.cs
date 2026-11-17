using System.Text;
using x_pad.Models;
using x_pad.Services;

namespace x_pad.Tests;

public class FileServiceTests
{
    [Fact]
    public void Decode_Utf8Bom()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i' };
        var (encoding, hasBom, text) = FileService.Decode(bytes);
        Assert.True(hasBom);
        Assert.Equal("hi", text);
        Assert.IsType<UTF8Encoding>(encoding);
    }

    [Fact]
    public void Decode_Utf8NoBom()
    {
        var bytes = Encoding.UTF8.GetBytes("hello");
        var (encoding, hasBom, text) = FileService.Decode(bytes);
        Assert.False(hasBom);
        Assert.Equal("hello", text);
        Assert.IsType<UTF8Encoding>(encoding);
    }

    [Fact]
    public void Decode_Utf16LeBom()
    {
        var bytes = new byte[] { 0xFF, 0xFE, (byte)'A', 0x00 };
        var (_, hasBom, text) = FileService.Decode(bytes);
        Assert.True(hasBom);
        Assert.Equal("A", text);
    }

    [Fact]
    public void Encode_RoundTrip_Utf8Bom()
    {
        var encoding = FileService.CreateUtf8(false);
        var encoded = FileService.Encode("abc", encoding, hasBom: true);
        var (decodedEncoding, hasBom, text) = FileService.Decode(encoded);
        Assert.True(hasBom);
        Assert.Equal("abc", text);
        Assert.IsType<UTF8Encoding>(decodedEncoding);
    }

    [Theory]
    [InlineData("a\r\nb\r\n", LineEndingKind.Crlf)]
    [InlineData("a\nb\n", LineEndingKind.Lf)]
    [InlineData("a\rb\r", LineEndingKind.Cr)]
    public void Detect_LineEndings(string text, LineEndingKind expected)
    {
        Assert.Equal(expected, LineEndingKindExtensions.Detect(text));
    }

    [Fact]
    public void NormalizeLineEndings_ToCrlf()
    {
        var result = FileService.NormalizeLineEndings("a\nb\r\nc\r", LineEndingKind.Crlf);
        Assert.Equal("a\r\nb\r\nc\r\n", result);
    }

    [Fact]
    public async Task ReadWrite_PreservesEncodingAndLineEndings()
    {
        var service = new FileService();
        var path = Path.Combine(Path.GetTempPath(), $"xpad-test-{Guid.NewGuid():N}.txt");
        try
        {
            var original = "line1\r\nline2\r\n";
            var encoding = FileService.CreateUtf8(false);
            File.WriteAllBytes(path, FileService.Encode(original, encoding, hasBom: true));

            var snapshot = service.ReadFile(path);
            Assert.Equal(LineEndingKind.Crlf, snapshot.LineEnding);
            Assert.True(snapshot.HasBom);
            Assert.Equal("UTF-8 BOM", snapshot.EncodingName);

            await service.WriteFileAsync(
                path,
                snapshot.Text + "line3\r\n",
                snapshot.Encoding,
                snapshot.HasBom,
                snapshot.LineEnding);

            var roundTrip = service.ReadFile(path);
            Assert.True(roundTrip.HasBom);
            Assert.Equal(LineEndingKind.Crlf, roundTrip.LineEnding);
            Assert.Contains("line3", roundTrip.Text);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
