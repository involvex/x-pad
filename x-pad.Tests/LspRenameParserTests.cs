using System.Text.Json.Nodes;
using x_pad.Services.Lsp;

namespace x_pad.Tests;

public class LspRenameParserTests
{
    [Fact]
    public void ParsePrepareRename_null_is_rejected()
    {
        var result = LspNavigationParser.ParsePrepareRename(null);
        Assert.False(result.Allowed);
        Assert.False(result.UseDefaultBehavior);
    }

    [Fact]
    public void ParsePrepareRename_defaultBehavior()
    {
        var json = JsonNode.Parse("""{"defaultBehavior":true}""");
        var result = LspNavigationParser.ParsePrepareRename(json);
        Assert.True(result.Allowed);
        Assert.True(result.UseDefaultBehavior);
    }

    [Fact]
    public void ParsePrepareRename_bare_Range()
    {
        var json = JsonNode.Parse("""
            {"start":{"line":3,"character":4},"end":{"line":3,"character":9}}
            """);
        var result = LspNavigationParser.ParsePrepareRename(json);
        Assert.True(result.Allowed);
        Assert.False(result.UseDefaultBehavior);
        Assert.Equal(3, result.StartLine);
        Assert.Equal(4, result.StartCharacter);
        Assert.Equal(3, result.EndLine);
        Assert.Equal(9, result.EndCharacter);
        Assert.Null(result.Placeholder);
    }

    [Fact]
    public void ParsePrepareRename_range_with_placeholder()
    {
        var json = JsonNode.Parse("""
            {"range":{"start":{"line":1,"character":0},"end":{"line":1,"character":3}},"placeholder":"foo"}
            """);
        var result = LspNavigationParser.ParsePrepareRename(json);
        Assert.True(result.Allowed);
        Assert.Equal(1, result.StartLine);
        Assert.Equal(0, result.StartCharacter);
        Assert.Equal(1, result.EndLine);
        Assert.Equal(3, result.EndCharacter);
        Assert.Equal("foo", result.Placeholder);
    }

    [Fact]
    public void ParsePrepareRename_empty_object_is_rejected()
    {
        var json = JsonNode.Parse("""{}""");
        var result = LspNavigationParser.ParsePrepareRename(json);
        Assert.False(result.Allowed);
    }

    [Fact]
    public void ParseWorkspaceEdit_rename_changes_map()
    {
        var json = JsonNode.Parse("""
            {
              "changes": {
                "file:///C:/src/a.ts": [
                  {"range":{"start":{"line":0,"character":6},"end":{"line":0,"character":9}},"newText":"Bar"},
                  {"range":{"start":{"line":5,"character":2},"end":{"line":5,"character":5}},"newText":"Bar"}
                ],
                "file:///C:/src/b.ts": [
                  {"range":{"start":{"line":2,"character":10},"end":{"line":2,"character":13}},"newText":"Bar"}
                ]
              }
            }
            """);
        var edit = LspCodeActionParser.ParseWorkspaceEdit(json);
        Assert.NotNull(edit);
        Assert.Equal(2, edit!.DocumentEdits.Count);
        var a = Assert.Single(edit.DocumentEdits, d => d.Uri.Contains("a.ts", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, a.Edits.Count);
        Assert.Equal("Bar", a.Edits[0].NewText);
    }

    [Fact]
    public void SupportsRename_and_prepare_from_capabilities()
    {
        Assert.False(LspCapabilities.SupportsRename(null));
        Assert.False(LspCapabilities.SupportsPrepareRename(null));

        Assert.True(LspCapabilities.SupportsRename(JsonNode.Parse(
            """{"capabilities":{"renameProvider":true}}""")));
        Assert.False(LspCapabilities.SupportsPrepareRename(JsonNode.Parse(
            """{"capabilities":{"renameProvider":true}}""")));

        Assert.True(LspCapabilities.SupportsRename(JsonNode.Parse(
            """{"capabilities":{"renameProvider":{"prepareProvider":true}}}""")));
        Assert.True(LspCapabilities.SupportsPrepareRename(JsonNode.Parse(
            """{"capabilities":{"renameProvider":{"prepareProvider":true}}}""")));

        Assert.False(LspCapabilities.SupportsPrepareRename(JsonNode.Parse(
            """{"capabilities":{"renameProvider":{"prepareProvider":false}}}""")));
    }
}
