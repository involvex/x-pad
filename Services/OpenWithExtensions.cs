namespace x_pad.Services;

/// <summary>Common text/code extensions for per-user Open With (does not change defaults).</summary>
public static class OpenWithExtensions
{
    public static readonly string[] Default =
    [
        ".txt", ".log", ".md", ".markdown", ".csv", ".tsv", ".json", ".xml", ".yaml", ".yml",
        ".ini", ".cfg", ".conf", ".toml", ".editorconfig", ".gitignore", ".env",
        ".cs", ".fs", ".vb", ".py", ".js", ".ts", ".jsx", ".tsx", ".css", ".html", ".htm",
        ".sql", ".ps1", ".psm1", ".bat", ".cmd", ".sh", ".c", ".h", ".cpp", ".hpp",
        ".go", ".rs", ".java", ".kt", ".rb", ".php", ".r", ".lua"
    ];
}
