using System;
using System.Text.Json.Nodes;
using AvaloniaEdit.Document;

namespace x_pad.Services.Lsp;

/// <summary>Parses LSP server capability bits we care about.</summary>
public static class LspCapabilities
{
    public static bool SupportsIncrementalSync(JsonNode? initializeResult)
    {
        var sync = initializeResult?["capabilities"]?["textDocumentSync"];
        if (sync is null)
            return false;

        if (sync is JsonValue value && value.TryGetValue(out int kind))
            return kind == 2;

        if (sync is JsonObject options)
        {
            var change = options["change"];
            if (change is JsonValue changeValue && changeValue.TryGetValue(out int changeKind))
                return changeKind == 2;
        }

        return false;
    }

    /// <summary>
    /// True when <c>codeActionProvider</c> is an options object with <c>resolveProvider: true</c>.
    /// A bare boolean provider does not advertise resolve.
    /// </summary>
    public static bool SupportsCodeActionResolve(JsonNode? initializeResult)
    {
        var provider = initializeResult?["capabilities"]?["codeActionProvider"];
        if (provider is not JsonObject options)
            return false;

        var resolve = options["resolveProvider"];
        return resolve is JsonValue resolveValue &&
               resolveValue.TryGetValue(out bool resolveEnabled) &&
               resolveEnabled;
    }

    /// <summary>True when <c>workspaceSymbolProvider</c> is true or an options object.</summary>
    public static bool SupportsWorkspaceSymbol(JsonNode? initializeResult)
    {
        var provider = initializeResult?["capabilities"]?["workspaceSymbolProvider"];
        if (provider is null)
            return false;

        if (provider is JsonValue value && value.TryGetValue(out bool enabled))
            return enabled;

        return provider is JsonObject;
    }

    /// <summary>
    /// True when <c>workspaceSymbolProvider</c> is an options object with <c>resolveProvider: true</c>.
    /// A bare boolean provider does not advertise resolve.
    /// </summary>
    public static bool SupportsWorkspaceSymbolResolve(JsonNode? initializeResult)
    {
        var provider = initializeResult?["capabilities"]?["workspaceSymbolProvider"];
        if (provider is not JsonObject options)
            return false;

        var resolve = options["resolveProvider"];
        return resolve is JsonValue resolveValue &&
               resolveValue.TryGetValue(out bool resolveEnabled) &&
               resolveEnabled;
    }

    /// <summary>True when <c>renameProvider</c> is true or an options object.</summary>
    public static bool SupportsRename(JsonNode? initializeResult)
    {
        var provider = initializeResult?["capabilities"]?["renameProvider"];
        if (provider is null)
            return false;

        if (provider is JsonValue value && value.TryGetValue(out bool enabled))
            return enabled;

        return provider is JsonObject;
    }

    /// <summary>
    /// True when <c>renameProvider</c> is an options object with <c>prepareProvider: true</c>.
    /// </summary>
    public static bool SupportsPrepareRename(JsonNode? initializeResult)
    {
        var provider = initializeResult?["capabilities"]?["renameProvider"];
        if (provider is not JsonObject options)
            return false;

        var prepare = options["prepareProvider"];
        return prepare is JsonValue prepareValue &&
               prepareValue.TryGetValue(out bool prepareEnabled) &&
               prepareEnabled;
    }
}

/// <summary>One incremental <c>textDocument/didChange</c> content change (LSP positions are 0-based).</summary>
public readonly record struct LspContentChange(
    int StartLine,
    int StartCharacter,
    int EndLine,
    int EndCharacter,
    string Text);

/// <summary>Builds LSP content changes from AvaloniaEdit document edits.</summary>
public static class LspContentChangeFactory
{
    /// <summary>
    /// Convert a post-change <see cref="DocumentChangeEventArgs"/> into an incremental LSP range edit.
    /// Range is the replaced span in the pre-change document; <see cref="LspContentChange.Text"/> is the insertion.
    /// </summary>
    public static bool TryFromDocumentChange(
        TextDocument document,
        DocumentChangeEventArgs e,
        out LspContentChange change)
    {
        change = default;
        if (document == null || e == null)
            return false;

        try
        {
            var offset = Math.Clamp(e.Offset, 0, document.TextLength);
            var start = document.GetLocation(offset);
            var startLine = start.Line - 1;
            var startChar = start.Column - 1;
            Advance(startLine, startChar, e.RemovedText, e.RemovalLength, out var endLine, out var endChar);
            var text = e.InsertionLength <= 0 ? "" : e.InsertedText.Text;
            change = new LspContentChange(startLine, startChar, endLine, endChar, text);
            return startLine >= 0 && startChar >= 0 && endLine >= 0 && endChar >= 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Advance a 0-based LSP position across removed text (handles LF / CR / CRLF).</summary>
    public static void Advance(
        int startLine,
        int startCharacter,
        ITextSource? removed,
        int removalLength,
        out int endLine,
        out int endCharacter)
    {
        endLine = startLine;
        endCharacter = startCharacter;
        if (removed == null || removalLength <= 0)
            return;

        var length = Math.Min(removalLength, removed.TextLength);
        for (var i = 0; i < length; i++)
        {
            var c = removed.GetCharAt(i);
            if (c == '\r')
            {
                if (i + 1 < length && removed.GetCharAt(i + 1) == '\n')
                    i++;
                endLine++;
                endCharacter = 0;
            }
            else if (c == '\n')
            {
                endLine++;
                endCharacter = 0;
            }
            else
            {
                endCharacter++;
            }
        }
    }

    /// <summary>Test helper: advance across a plain string.</summary>
    public static void Advance(
        int startLine,
        int startCharacter,
        string removed,
        out int endLine,
        out int endCharacter)
    {
        var source = string.IsNullOrEmpty(removed)
            ? (ITextSource)new StringTextSource("")
            : new StringTextSource(removed);
        Advance(startLine, startCharacter, source, source.TextLength, out endLine, out endCharacter);
    }
}
