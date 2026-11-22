using System.Collections.Generic;

namespace x_pad.Services.Lsp;

public sealed class LspTextEdit
{
    public required int StartLine { get; init; }
    public required int StartCharacter { get; init; }
    public required int EndLine { get; init; }
    public required int EndCharacter { get; init; }
    public required string NewText { get; init; }
}

public sealed class LspDocumentEdit
{
    public required string Uri { get; init; }
    public required IReadOnlyList<LspTextEdit> Edits { get; init; }
}

public sealed class LspWorkspaceEdit
{
    public required IReadOnlyList<LspDocumentEdit> DocumentEdits { get; init; }
}

public sealed class LspCodeAction
{
    public required string Title { get; init; }
    public string? Kind { get; init; }
    public bool IsPreferred { get; init; }
    public LspWorkspaceEdit? Edit { get; init; }
    public bool HasCommandOnly { get; init; }
}
