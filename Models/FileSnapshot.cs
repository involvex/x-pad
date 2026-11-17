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
}
