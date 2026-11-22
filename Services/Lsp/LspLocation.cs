using System.Collections.Generic;

namespace x_pad.Services.Lsp;

public sealed class LspLocation
{
    public required string Uri { get; init; }
    public required int Line { get; init; }
    public required int Character { get; init; }
    public int EndLine { get; init; }
    public int EndCharacter { get; init; }
}

public sealed class LspDocumentSymbol
{
    public required string Name { get; init; }
    public string? Detail { get; init; }
    public int Kind { get; init; }
    public required int Line { get; init; }
    public required int Character { get; init; }
    public IReadOnlyList<LspDocumentSymbol> Children { get; init; } = [];
}
