using System.Text.Json.Nodes;
using AvaloniaEdit.Document;
using x_pad.Services.Lsp;

namespace x_pad.Tests;

public class LspContentChangeTests
{
    [Theory]
    [InlineData("", 0, 0, 0, 0)]
    [InlineData("abc", 0, 0, 0, 3)]
    [InlineData("ab\ncd", 1, 2, 2, 2)]
    [InlineData("a\r\nb", 0, 1, 1, 1)]
    [InlineData("x\ny\nz", 2, 0, 4, 1)]
    public void Advance_handles_newlines(
        string removed,
        int startLine,
        int startChar,
        int endLine,
        int endChar)
    {
        LspContentChangeFactory.Advance(startLine, startChar, removed, out var line, out var character);
        Assert.Equal(endLine, line);
        Assert.Equal(endChar, character);
    }

    [Fact]
    public void TryFromDocumentChange_maps_insert_at_start()
    {
        var doc = new TextDocument("world");
        LspContentChange? captured = null;
        doc.Changed += (_, e) =>
        {
            Assert.True(LspContentChangeFactory.TryFromDocumentChange(doc, e, out var change));
            captured = change;
        };
        doc.Insert(0, "hello ");

        Assert.NotNull(captured);
        Assert.Equal(0, captured!.Value.StartLine);
        Assert.Equal(0, captured.Value.StartCharacter);
        Assert.Equal(0, captured.Value.EndLine);
        Assert.Equal(0, captured.Value.EndCharacter);
        Assert.Equal("hello ", captured.Value.Text);
    }

    [Fact]
    public void TryFromDocumentChange_maps_replace_across_lines()
    {
        var doc = new TextDocument("aa\nbb\ncc");
        LspContentChange? captured = null;
        doc.Changed += (_, e) =>
        {
            Assert.True(LspContentChangeFactory.TryFromDocumentChange(doc, e, out var change));
            captured = change;
        };

        // Replace "bb" on line 2 (1-based) → offset after "aa\n" = 3, length 2
        doc.Replace(3, 2, "BBB");

        Assert.NotNull(captured);
        Assert.Equal(1, captured!.Value.StartLine);
        Assert.Equal(0, captured.Value.StartCharacter);
        Assert.Equal(1, captured.Value.EndLine);
        Assert.Equal(2, captured.Value.EndCharacter);
        Assert.Equal("BBB", captured.Value.Text);
    }

    [Fact]
    public void SupportsIncrementalSync_reads_numeric_and_options()
    {
        Assert.False(LspCapabilities.SupportsIncrementalSync(null));
        Assert.False(LspCapabilities.SupportsIncrementalSync(JsonNode.Parse("""{"capabilities":{"textDocumentSync":1}}""")));
        Assert.True(LspCapabilities.SupportsIncrementalSync(JsonNode.Parse("""{"capabilities":{"textDocumentSync":2}}""")));
        Assert.True(LspCapabilities.SupportsIncrementalSync(JsonNode.Parse(
            """{"capabilities":{"textDocumentSync":{"openClose":true,"change":2}}}""")));
        Assert.False(LspCapabilities.SupportsIncrementalSync(JsonNode.Parse(
            """{"capabilities":{"textDocumentSync":{"change":1}}}""")));
    }

    [Fact]
    public void SupportsCodeActionResolve_reads_boolean_and_options()
    {
        Assert.False(LspCapabilities.SupportsCodeActionResolve(null));
        Assert.False(LspCapabilities.SupportsCodeActionResolve(JsonNode.Parse(
            """{"capabilities":{"codeActionProvider":true}}""")));
        Assert.False(LspCapabilities.SupportsCodeActionResolve(JsonNode.Parse(
            """{"capabilities":{"codeActionProvider":{"resolveProvider":false}}}""")));
        Assert.True(LspCapabilities.SupportsCodeActionResolve(JsonNode.Parse(
            """{"capabilities":{"codeActionProvider":{"resolveProvider":true}}}""")));
    }
}
