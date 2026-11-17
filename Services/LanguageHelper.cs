using System.Collections.Generic;
using System.IO;
using System.Linq;
using TextMateSharp.Grammars;

namespace x_pad.Services;

public static class LanguageHelper
{
    /// <summary>Status-bar language menu: display name → probe extension (empty = Auto).</summary>
    public static IReadOnlyList<(string Display, string Extension)> CommonLanguageChoices { get; } =
    [
        ("Auto (from file)", ""),
        ("Plain Text", ".txt"),
        ("C#", ".cs"),
        ("C / C++", ".cpp"),
        ("CSS", ".css"),
        ("Go", ".go"),
        ("HTML", ".html"),
        ("Java", ".java"),
        ("JavaScript", ".js"),
        ("JSON", ".json"),
        ("Markdown", ".md"),
        ("PowerShell", ".ps1"),
        ("Python", ".py"),
        ("Rust", ".rs"),
        ("Shell", ".sh"),
        ("SQL", ".sql"),
        ("TypeScript", ".ts"),
        ("XML", ".xml"),
        ("YAML", ".yaml"),
    ];

    /// <summary>
    /// Path used only to resolve TextMate grammar (real file path, or a probe path from an override extension).
    /// </summary>
    public static string? GetProbePath(string? filePath, string? languageOverrideExtension)
    {
        if (!string.IsNullOrWhiteSpace(languageOverrideExtension))
        {
            var ext = languageOverrideExtension.StartsWith('.')
                ? languageOverrideExtension
                : "." + languageOverrideExtension;
            return "probe" + ext;
        }

        return filePath;
    }

    public static string GetLanguageDisplay(string? filePath, RegistryOptions registry)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return "Plain Text";

        var ext = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(ext))
            return "Plain Text";

        try
        {
            var language = registry.GetLanguageByExtension(ext);
            if (language == null)
                return "Plain Text";
            var alias = language.Aliases?.FirstOrDefault();
            return string.IsNullOrWhiteSpace(alias) ? language.Id : alias;
        }
        catch
        {
            return "Plain Text";
        }
    }

    public static string? GetScopeName(string? filePath, RegistryOptions registry)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        var ext = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(ext))
            return null;

        try
        {
            var language = registry.GetLanguageByExtension(ext);
            if (language == null)
                return null;
            return registry.GetScopeByLanguageId(language.Id);
        }
        catch
        {
            return null;
        }
    }

    public static string GetLspLanguageId(string? filePath)
    {
        var ext = Path.GetExtension(filePath ?? "").ToLowerInvariant();
        return ext switch
        {
            ".ts" or ".tsx" => "typescript",
            ".js" or ".jsx" or ".mjs" or ".cjs" => "javascript",
            ".py" => "python",
            ".cs" => "csharp",
            ".json" => "json",
            ".md" or ".markdown" => "markdown",
            ".html" or ".htm" => "html",
            ".css" => "css",
            ".xml" => "xml",
            ".yaml" or ".yml" => "yaml",
            ".rs" => "rust",
            ".go" => "go",
            _ => "plaintext"
        };
    }

    /// <summary>Line comment prefix for toggle-comment, or null when unsupported.</summary>
    public static string? GetLineCommentPrefix(string? filePath)
    {
        var ext = Path.GetExtension(filePath ?? "").ToLowerInvariant();
        return ext switch
        {
            ".py" or ".ps1" or ".psm1" or ".psd1" or ".sh" or ".bash" or ".zsh" or
                ".yaml" or ".yml" or ".toml" or ".ini" or ".cfg" or ".conf" or
                ".r" or ".rb" or ".pl" or ".pm" => "#",
            ".sql" => "--",
            ".bat" or ".cmd" => "REM ",
            ".html" or ".htm" or ".xml" or ".xhtml" or ".svg" => null,
            "" => "//",
            _ => "//"
        };
    }
}
