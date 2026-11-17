using System.IO;
using System.Linq;
using TextMateSharp.Grammars;

namespace x_pad.Services;

public static class LanguageHelper
{
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
}
