using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using x_pad.Models;

namespace x_pad.Services;

public class FileService
{
    public Task<FileSnapshot> ReadFileAsync(string path) =>
        Task.FromResult(ReadFile(path));

    public FileSnapshot ReadFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var (encoding, hasBom, text) = Decode(bytes);
        var info = new FileInfo(path);
        return new FileSnapshot
        {
            Text = text,
            Encoding = encoding,
            EncodingName = FormatEncodingName(encoding, hasBom),
            HasBom = hasBom,
            LineEnding = LineEndingKindExtensions.Detect(text),
            LastWriteTimeUtc = info.LastWriteTimeUtc
        };
    }

    public Task WriteFileAsync(
        string path,
        string text,
        Encoding encoding,
        bool hasBom,
        LineEndingKind lineEnding,
        bool backupExisting = false)
    {
        if (backupExisting)
            TryCreateBackup(path);

        var normalized = NormalizeLineEndings(text, lineEnding);
        var payload = Encode(normalized, encoding, hasBom);
        return File.WriteAllBytesAsync(path, payload);
    }

    /// <summary>
    /// Copies an existing file to path.bak (overwrites previous backup).
    /// </summary>
    public static bool TryCreateBackup(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;

            var backupPath = path + ".bak";
            File.Copy(path, backupPath, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static (Encoding Encoding, bool HasBom, string Text) Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            return (new UTF8Encoding(false), true, text);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            var encoding = new UnicodeEncoding(false, true);
            var text = encoding.GetString(bytes, 2, bytes.Length - 2);
            return (encoding, true, text);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            var encoding = new UnicodeEncoding(true, true);
            var text = encoding.GetString(bytes, 2, bytes.Length - 2);
            return (encoding, true, text);
        }

        // Heuristic: UTF-16 LE without BOM (many nulls on odd indexes)
        if (LooksLikeUtf16Le(bytes))
        {
            var encoding = new UnicodeEncoding(false, false);
            return (encoding, false, encoding.GetString(bytes));
        }

        if (LooksLikeUtf16Be(bytes))
        {
            var encoding = new UnicodeEncoding(true, false);
            return (encoding, false, encoding.GetString(bytes));
        }

        var utf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);
        try
        {
            return (utf8, false, utf8.GetString(bytes));
        }
        catch (DecoderFallbackException)
        {
            return (Encoding.Latin1, false, Encoding.Latin1.GetString(bytes));
        }
    }

    public static byte[] Encode(string text, Encoding encoding, bool hasBom)
    {
        var body = encoding.GetBytes(text);
        if (!hasBom)
            return body;

        var preamble = encoding.GetPreamble();
        if (preamble.Length == 0)
        {
            if (encoding is UTF8Encoding)
                preamble = [0xEF, 0xBB, 0xBF];
            else if (encoding.CodePage == 1200)
                preamble = [0xFF, 0xFE];
            else if (encoding.CodePage == 1201)
                preamble = [0xFE, 0xFF];
        }

        if (preamble.Length == 0)
            return body;

        var result = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, result, preamble.Length, body.Length);
        return result;
    }

    public static string NormalizeLineEndings(string text, LineEndingKind lineEnding)
    {
        var unified = text.Replace("\r\n", "\n").Replace('\r', '\n');
        return lineEnding switch
        {
            LineEndingKind.Crlf => unified.Replace("\n", "\r\n"),
            LineEndingKind.Cr => unified.Replace("\n", "\r"),
            _ => unified
        };
    }

    public static string FormatEncodingName(Encoding encoding, bool hasBom)
    {
        var name = encoding switch
        {
            UTF8Encoding => "UTF-8",
            UnicodeEncoding u when u.CodePage == 1200 => "UTF-16 LE",
            UnicodeEncoding u when u.CodePage == 1201 => "UTF-16 BE",
            _ => encoding.WebName.ToUpperInvariant()
        };
        return hasBom ? $"{name} BOM" : name;
    }

    public static Encoding CreateUtf8(bool bom) => new UTF8Encoding(bom);

    private static bool LooksLikeUtf16Le(byte[] bytes)
    {
        if (bytes.Length < 4 || bytes.Length % 2 != 0)
            return false;
        var nullOdd = 0;
        var sample = Math.Min(bytes.Length, 64);
        for (var i = 1; i < sample; i += 2)
        {
            if (bytes[i] == 0)
                nullOdd++;
        }
        return nullOdd >= sample / 4;
    }

    private static bool LooksLikeUtf16Be(byte[] bytes)
    {
        if (bytes.Length < 4 || bytes.Length % 2 != 0)
            return false;
        var nullEven = 0;
        var sample = Math.Min(bytes.Length, 64);
        for (var i = 0; i < sample; i += 2)
        {
            if (bytes[i] == 0)
                nullEven++;
        }
        return nullEven >= sample / 4;
    }
}
