using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace x_pad.Services.Lsp;

/// <summary>Pure parsers for definition / documentSymbol / workspace/symbol LSP payloads.</summary>
public static class LspNavigationParser
{
    public const int DefaultWorkspaceSymbolMaxHits = 200;

    public static IReadOnlyList<LspLocation> ParseLocations(JsonNode? node)
    {
        if (node == null)
            return [];

        if (node is JsonArray array)
        {
            var list = new List<LspLocation>();
            foreach (var item in array)
            {
                var loc = ParseOneLocation(item);
                if (loc != null)
                    list.Add(loc);
            }
            return list;
        }

        var single = ParseOneLocation(node);
        return single == null ? [] : [single];
    }

    public static IReadOnlyList<LspDocumentSymbol> ParseDocumentSymbols(JsonNode? node)
    {
        if (node is not JsonArray array)
            return [];

        var list = new List<LspDocumentSymbol>();
        foreach (var item in array)
        {
            var symbol = ParseOneSymbol(item);
            if (symbol != null)
                list.Add(symbol);
        }
        return list;
    }

    public static IReadOnlyList<LspWorkspaceSymbol> ParseWorkspaceSymbols(
        JsonNode? node,
        int maxHits = DefaultWorkspaceSymbolMaxHits)
    {
        if (node is not JsonArray array)
            return [];

        var cap = Math.Max(1, maxHits);
        var list = new List<LspWorkspaceSymbol>();
        foreach (var item in array)
        {
            if (list.Count >= cap)
                break;

            var parsed = ParseOneWorkspaceSymbol(item);
            if (parsed != null)
                list.Add(parsed);
        }

        return list;
    }

    /// <summary>Parse a single WorkspaceSymbol (e.g. <c>workspaceSymbol/resolve</c> result).</summary>
    public static LspWorkspaceSymbol? ParseWorkspaceSymbol(JsonNode? node) =>
        ParseOneWorkspaceSymbol(node);

    public static string? TryUriToLocalPath(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
            return null;

        if (!parsed.IsFile)
            return null;

        return parsed.LocalPath;
    }

    /// <summary>
    /// Parse <c>textDocument/prepareRename</c> results:
    /// Range, <c>{ range, placeholder }</c>, <c>{ defaultBehavior: true }</c>, or null (rejected).
    /// </summary>
    public static LspPrepareRenameResult ParsePrepareRename(JsonNode? node)
    {
        if (node == null)
            return LspPrepareRenameResult.Rejected;

        if (node is JsonObject obj &&
            obj["defaultBehavior"] is JsonValue defaultValue &&
            defaultValue.TryGetValue(out bool defaultBehavior) &&
            defaultBehavior)
        {
            return LspPrepareRenameResult.DefaultBehavior;
        }

        // { range, placeholder? } or bare Range { start, end }
        var range = node["range"] ?? (node["start"] != null ? node : null);
        var start = range?["start"];
        var end = range?["end"];
        if (start == null || end == null)
            return LspPrepareRenameResult.Rejected;

        return new LspPrepareRenameResult
        {
            Allowed = true,
            StartLine = start["line"]?.GetValue<int>() ?? 0,
            StartCharacter = start["character"]?.GetValue<int>() ?? 0,
            EndLine = end["line"]?.GetValue<int>() ?? 0,
            EndCharacter = end["character"]?.GetValue<int>() ?? 0,
            Placeholder = node["placeholder"]?.GetValue<string>()
        };
    }

    private static LspWorkspaceSymbol? ParseOneWorkspaceSymbol(JsonNode? node)
    {
        if (node == null)
            return null;

        var name = node["name"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(name))
            return null;

        if (!TryParseLocation(node["location"], out var location, out var hasRange) || location == null)
            return null;

        return new LspWorkspaceSymbol
        {
            Name = name!,
            Kind = node["kind"]?.GetValue<int>() ?? 0,
            ContainerName = node["containerName"]?.GetValue<string>(),
            Location = location,
            HasRange = hasRange,
            Raw = node.DeepClone()
        };
    }

    private static LspLocation? ParseOneLocation(JsonNode? node)
    {
        TryParseLocation(node, out var location, out _);
        return location;
    }

    private static bool TryParseLocation(JsonNode? node, out LspLocation? location, out bool hasRange)
    {
        location = null;
        hasRange = false;
        if (node == null)
            return false;

        // LocationLink: { targetUri, targetRange, targetSelectionRange }
        var targetUri = node["targetUri"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(targetUri))
        {
            var range = node["targetSelectionRange"] ?? node["targetRange"];
            location = FromUriAndRange(targetUri!, range, out hasRange);
            return true;
        }

        // Location: { uri, range? }
        var uri = node["uri"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(uri))
            return false;

        location = FromUriAndRange(uri!, node["range"], out hasRange);
        return true;
    }

    private static LspLocation FromUriAndRange(string uri, JsonNode? range, out bool hasRange)
    {
        var start = range?["start"];
        var end = range?["end"];
        hasRange = start != null;
        var line = start?["line"]?.GetValue<int>() ?? 0;
        var character = start?["character"]?.GetValue<int>() ?? 0;
        return new LspLocation
        {
            Uri = uri,
            Line = line,
            Character = character,
            EndLine = end?["line"]?.GetValue<int>() ?? line,
            EndCharacter = end?["character"]?.GetValue<int>() ?? character
        };
    }

    private static LspDocumentSymbol? ParseOneSymbol(JsonNode? node)
    {
        if (node == null)
            return null;

        // DocumentSymbol: name, kind, range/selectionRange, children
        var name = node["name"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(name))
        {
            var sel = node["selectionRange"] ?? node["range"];
            var start = sel?["start"];
            var children = new List<LspDocumentSymbol>();
            if (node["children"] is JsonArray childArr)
            {
                foreach (var child in childArr)
                {
                    var parsed = ParseOneSymbol(child);
                    if (parsed != null)
                        children.Add(parsed);
                }
            }

            return new LspDocumentSymbol
            {
                Name = name!,
                Detail = node["detail"]?.GetValue<string>(),
                Kind = node["kind"]?.GetValue<int>() ?? 0,
                Line = (start?["line"]?.GetValue<int>() ?? 0) + 1,
                Character = (start?["character"]?.GetValue<int>() ?? 0) + 1,
                Children = children
            };
        }

        // SymbolInformation: name, kind, location
        var infoName = node["name"]?.GetValue<string>();
        var location = ParseOneLocation(node["location"]);
        if (string.IsNullOrWhiteSpace(infoName) || location == null)
            return null;

        return new LspDocumentSymbol
        {
            Name = infoName!,
            Kind = node["kind"]?.GetValue<int>() ?? 0,
            Line = location.Line + 1,
            Character = location.Character + 1
        };
    }
}
