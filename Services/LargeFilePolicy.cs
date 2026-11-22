namespace x_pad.Services;

/// <summary>Open-as-plain guard for oversized buffers.</summary>
public static class LargeFilePolicy
{
    public const long ThresholdBytes = 8L * 1024 * 1024;

    public static bool IsLarge(long byteLength) => byteLength > ThresholdBytes;

    public static string StatusMessage(long byteLength) =>
        $"Large file ({FormatSize(byteLength)}): folding, word completion, and LSP disabled";

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes / (1024.0 * 1024.0):0.#} MB";
    }
}
