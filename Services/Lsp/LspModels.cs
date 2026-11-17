using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace x_pad.Services.Lsp;

public sealed class LspPosition
{
    [JsonPropertyName("line")]
    public int Line { get; set; }

    [JsonPropertyName("character")]
    public int Character { get; set; }
}

public sealed class LspRange
{
    [JsonPropertyName("start")]
    public LspPosition Start { get; set; } = new();

    [JsonPropertyName("end")]
    public LspPosition End { get; set; } = new();
}

public sealed class LspDiagnostic
{
    [JsonPropertyName("range")]
    public LspRange Range { get; set; } = new();

    [JsonPropertyName("severity")]
    public int Severity { get; set; } = 1;

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("source")]
    public string? Source { get; set; }
}

public sealed class LspCompletionItem
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }

    [JsonPropertyName("insertText")]
    public string? InsertText { get; set; }

    [JsonPropertyName("kind")]
    public int? Kind { get; set; }
}

public sealed class LspHover
{
    [JsonPropertyName("contents")]
    public object? Contents { get; set; }
}

public sealed class LspPublishDiagnostics
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = "";

    [JsonPropertyName("diagnostics")]
    public List<LspDiagnostic> Diagnostics { get; set; } = [];
}
