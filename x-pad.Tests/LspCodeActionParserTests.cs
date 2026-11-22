using System.Text.Json.Nodes;
using AvaloniaEdit.Document;
using x_pad.Services.Lsp;

namespace x_pad.Tests;

public class LspCodeActionParserTests
{
    [Fact]
    public void ParseCodeActions_prefers_isPreferred_and_keeps_edit()
    {
        var json = JsonNode.Parse("""
            [
              {"title":"Other","edit":{"changes":{"file:///a.ts":[{"range":{"start":{"line":0,"character":0},"end":{"line":0,"character":1}},"newText":"x"}]}}},
              {"title":"Fix it","isPreferred":true,"kind":"quickfix","edit":{"changes":{"file:///a.ts":[{"range":{"start":{"line":0,"character":0},"end":{"line":0,"character":3}},"newText":"bar"}]}}}
            ]
            """);

        var actions = LspCodeActionParser.ParseCodeActions(json);
        Assert.Equal(2, actions.Count);
        Assert.Equal("Fix it", actions[0].Title);
        Assert.True(actions[0].IsPreferred);
        Assert.NotNull(actions[0].Edit);
        Assert.Single(actions[0].Edit!.DocumentEdits);
    }

    [Fact]
    public void ParseWorkspaceEdit_documentChanges_and_changes()
    {
        var json = JsonNode.Parse("""
            {
              "documentChanges":[
                {"textDocument":{"uri":"file:///C:/src/a.ts","version":1},"edits":[{"range":{"start":{"line":1,"character":0},"end":{"line":1,"character":3}},"newText":"baz"}]}
              ],
              "changes":{
                "file:///C:/src/b.ts":[{"range":{"start":{"line":0,"character":0},"end":{"line":0,"character":0}},"newText":"//\n"}]
              }
            }
            """);

        var edit = LspCodeActionParser.ParseWorkspaceEdit(json);
        Assert.NotNull(edit);
        Assert.Equal(2, edit!.DocumentEdits.Count);
    }

    [Fact]
    public void ApplyToDocument_applies_from_end_so_offsets_stay_valid()
    {
        var document = new TextDocument("foo bar foo");
        var edits = new[]
        {
            new LspTextEdit { StartLine = 0, StartCharacter = 8, EndLine = 0, EndCharacter = 11, NewText = "baz" },
            new LspTextEdit { StartLine = 0, StartCharacter = 0, EndLine = 0, EndCharacter = 3, NewText = "qux" }
        };

        // Newest-first order as applier expects
        LspWorkspaceEditApplier.ApplyToDocument(document, edits);
        Assert.Equal("qux bar baz", document.Text);
    }

    [Fact]
    public void ParseCodeActions_marks_command_only()
    {
        var json = JsonNode.Parse("""
            [{"title":"Organize Imports","command":{"title":"Organize Imports","command":"editor.action.organizeImports"}}]
            """);
        var actions = LspCodeActionParser.ParseCodeActions(json);
        Assert.Single(actions);
        Assert.True(actions[0].HasCommandOnly);
        Assert.Null(actions[0].Edit);
    }
}
