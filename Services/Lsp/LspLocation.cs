using System.Collections.Generic;
using System.Text.Json.Nodes;

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

/// <summary>One <c>workspace/symbol</c> hit (SymbolInformation or WorkspaceSymbol).</summary>
public sealed class LspWorkspaceSymbol
{
    public required string Name { get; init; }
    public int Kind { get; init; }
    public string? ContainerName { get; init; }
    public required LspLocation Location { get; init; }

    /// <summary>
    /// True when the location included a range (including a real symbol at 0:0).
    /// False for URI-only locations that need <c>workspaceSymbol/resolve</c>.
    /// </summary>
    public bool HasRange { get; init; }

    /// <summary>
    /// Original symbol JSON (including <c>data</c>) for <c>workspaceSymbol/resolve</c>.
    /// </summary>
    public JsonNode? Raw { get; init; }
}

/// <summary>
/// Result of <c>textDocument/prepareRename</c>.
/// Positions are 0-based LSP coordinates when a range is present.
/// </summary>
public sealed class LspPrepareRenameResult
{
    public bool Allowed { get; init; }
    public bool UseDefaultBehavior { get; init; }
    public int StartLine { get; init; }
    public int StartCharacter { get; init; }
    public int EndLine { get; init; }
    public int EndCharacter { get; init; }
    public string? Placeholder { get; init; }

    public static LspPrepareRenameResult Rejected { get; } = new() { Allowed = false };

    public static LspPrepareRenameResult DefaultBehavior { get; } = new()
    {
        Allowed = true,
        UseDefaultBehavior = true
    };
}
