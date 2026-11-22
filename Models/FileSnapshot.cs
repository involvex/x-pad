using System;
using System.Text;

namespace x_pad.Models;

public sealed class FileSnapshot
{
    public required string Text { get; init; }
    public required Encoding Encoding { get; init; }
    public required string EncodingName { get; init; }
    public required bool HasBom { get; init; }
    public required LineEndingKind LineEnding { get; init; }
    public DateTime LastWriteTimeUtc { get; init; }

    /// <summary>Original on-disk bytes used for re-decode with another encoding.</summary>
    public byte[]? SourceBytes { get; init; }

    public bool IsReadOnly { get; init; }
    public bool IsLargeFile { get; init; }
}
