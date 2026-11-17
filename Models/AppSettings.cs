using System.Collections.Generic;

namespace x_pad.Models;

public sealed class AppSettings
{
    public double FontSize { get; set; } = 13;
    public int TabSize { get; set; } = 4;
    public bool WordCompletion { get; set; } = true;
    public bool AutoCloseBrackets { get; set; } = true;
    public bool WordWrap { get; set; }
    public string Theme { get; set; } = "Dark";
    public Dictionary<string, string> LanguageServers { get; set; } = new()
    {
        ["typescript"] = "typescript-language-server --stdio",
        ["javascript"] = "typescript-language-server --stdio",
        ["python"] = "pyright-langserver --stdio"
    };
}
