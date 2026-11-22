using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace x_pad.Services.Lsp;

/// <summary>Pure parsers for codeAction / WorkspaceEdit payloads.</summary>
public static class LspCodeActionParser
{
    public static IReadOnlyList<LspCodeAction> ParseCodeActions(JsonNode? node)
    {
        if (node is not JsonArray array)
            return [];

        var list = new List<LspCodeAction>();
        foreach (var item in array)
        {
            var action = ParseOne(item);
            if (action != null)
                list.Add(action);
        }

        return list
            .OrderByDescending(a => a.IsPreferred)
            .ThenBy(a => a.Title, System.StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static LspWorkspaceEdit? ParseWorkspaceEdit(JsonNode? node)
    {
        if (node == null)
            return null;

        var byUri = new Dictionary<string, List<LspTextEdit>>(System.StringComparer.OrdinalIgnoreCase);

        if (node["documentChanges"] is JsonArray docChanges)
        {
            foreach (var change in docChanges)
            {
                if (change == null)
                    continue;
                // Skip CreateFile / RenameFile / DeleteFile for first cut.
                var uri = change["textDocument"]?["uri"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(uri))
                    continue;
                if (change["edits"] is not JsonArray edits)
                    continue;
                AddEdits(byUri, uri!, edits);
            }
        }

        if (node["changes"] is JsonObject changes)
        {
            foreach (var (uri, value) in changes)
            {
                if (value is not JsonArray edits)
                    continue;
                AddEdits(byUri, uri, edits);
            }
        }

        if (byUri.Count == 0)
            return null;

        return new LspWorkspaceEdit
        {
            DocumentEdits = byUri
                .Select(kv => new LspDocumentEdit { Uri = kv.Key, Edits = kv.Value })
                .ToList()
        };
    }

    private static LspCodeAction? ParseOne(JsonNode? node)
    {
        if (node == null)
            return null;

        // Command-only item: { title, command } without edit
        var title = node["title"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(title))
            return null;

        var edit = ParseWorkspaceEdit(node["edit"]);
        var hasCommand = node["command"] != null;
        if (edit == null && !hasCommand)
        {
            // Some servers return Command shape at top level
            if (node["command"] is JsonValue)
                return new LspCodeAction { Title = title!, HasCommandOnly = true };
        }

        return new LspCodeAction
        {
            Title = title!,
            Kind = node["kind"]?.GetValue<string>(),
            IsPreferred = node["isPreferred"]?.GetValue<bool>() == true,
            Edit = edit,
            HasCommandOnly = edit == null && hasCommand
        };
    }

    private static void AddEdits(
        Dictionary<string, List<LspTextEdit>> byUri,
        string uri,
        JsonArray edits)
    {
        if (!byUri.TryGetValue(uri, out var list))
        {
            list = [];
            byUri[uri] = list;
        }

        foreach (var edit in edits)
        {
            var parsed = ParseTextEdit(edit);
            if (parsed != null)
                list.Add(parsed);
        }
    }

    private static LspTextEdit? ParseTextEdit(JsonNode? node)
    {
        if (node == null)
            return null;

        var range = node["range"];
        var start = range?["start"];
        var end = range?["end"];
        var newText = node["newText"]?.GetValue<string>();
        if (start == null || end == null || newText == null)
            return null;

        return new LspTextEdit
        {
            StartLine = start["line"]?.GetValue<int>() ?? 0,
            StartCharacter = start["character"]?.GetValue<int>() ?? 0,
            EndLine = end["line"]?.GetValue<int>() ?? 0,
            EndCharacter = end["character"]?.GetValue<int>() ?? 0,
            NewText = newText
        };
    }
}
