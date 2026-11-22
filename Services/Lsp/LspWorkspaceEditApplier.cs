using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaEdit.Document;

namespace x_pad.Services.Lsp;

public sealed class LspApplyEditResult
{
    public int DocumentsTouched { get; init; }
    public int EditsApplied { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
}

/// <summary>Applies LSP workspace edits to open documents or on-disk files.</summary>
public static class LspWorkspaceEditApplier
{
    /// <param name="resolveOpenDocument">Returns the live document for a local path, or null.</param>
    public static async Task<LspApplyEditResult> ApplyAsync(
        LspWorkspaceEdit edit,
        Func<string, TextDocument?> resolveOpenDocument,
        FileService fileService,
        Func<string, Task>? openFileAsync = null)
    {
        var errors = new List<string>();
        var docs = 0;
        var edits = 0;

        foreach (var docEdit in edit.DocumentEdits)
        {
            var path = LspNavigationParser.TryUriToLocalPath(docEdit.Uri);
            if (path == null)
            {
                errors.Add($"Unsupported URI: {docEdit.Uri}");
                continue;
            }

            var ordered = docEdit.Edits
                .OrderByDescending(e => e.StartLine)
                .ThenByDescending(e => e.StartCharacter)
                .ToList();

            var openDoc = resolveOpenDocument(path);
            if (openDoc != null)
            {
                try
                {
                    ApplyToDocument(openDoc, ordered);
                    edits += ordered.Count;
                    docs++;
                }
                catch (Exception ex)
                {
                    errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
                }

                continue;
            }

            if (!File.Exists(path))
            {
                errors.Add($"Missing file: {path}");
                continue;
            }

            try
            {
                var snapshot = fileService.ReadFile(path);
                var document = new TextDocument(snapshot.Text);
                ApplyToDocument(document, ordered);
                await fileService.WriteFileAsync(
                    path,
                    document.Text,
                    snapshot.Encoding,
                    snapshot.HasBom,
                    snapshot.LineEnding);
                edits += ordered.Count;
                docs++;
                if (openFileAsync != null)
                    await openFileAsync(path);
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        return new LspApplyEditResult
        {
            DocumentsTouched = docs,
            EditsApplied = edits,
            Errors = errors
        };
    }

    public static void ApplyToDocument(TextDocument document, IReadOnlyList<LspTextEdit> editsNewestFirst)
    {
        foreach (var edit in editsNewestFirst)
        {
            var start = OffsetAt(document, edit.StartLine, edit.StartCharacter);
            var end = OffsetAt(document, edit.EndLine, edit.EndCharacter);
            var length = Math.Max(0, end - start);
            document.Replace(start, length, edit.NewText ?? "");
        }
    }

    private static int OffsetAt(TextDocument document, int line0, int character0)
    {
        var lineNumber = Math.Clamp(line0 + 1, 1, document.LineCount);
        var line = document.GetLineByNumber(lineNumber);
        var col = Math.Clamp(character0, 0, line.Length);
        return line.Offset + col;
    }
}
