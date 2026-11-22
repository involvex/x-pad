using System.Text.Json.Nodes;
using x_pad.Services.Lsp;

namespace x_pad.Tests;

public class LspNavigationParserTests
{
    [Fact]
    public void ParseLocations_single_Location()
    {
        var json = JsonNode.Parse("""
            {"uri":"file:///C:/src/a.cs","range":{"start":{"line":10,"character":2},"end":{"line":10,"character":5}}}
            """);
        var locs = LspNavigationParser.ParseLocations(json);
        Assert.Single(locs);
        Assert.Equal("file:///C:/src/a.cs", locs[0].Uri);
        Assert.Equal(10, locs[0].Line);
        Assert.Equal(2, locs[0].Character);
    }

    [Fact]
    public void ParseLocations_LocationLink_array()
    {
        var json = JsonNode.Parse("""
            [{"targetUri":"file:///C:/src/b.ts","targetRange":{"start":{"line":1,"character":0},"end":{"line":20,"character":0}},"targetSelectionRange":{"start":{"line":3,"character":4},"end":{"line":3,"character":9}}}]
            """);
        var locs = LspNavigationParser.ParseLocations(json);
        Assert.Single(locs);
        Assert.Equal(3, locs[0].Line);
        Assert.Equal(4, locs[0].Character);
    }

    [Fact]
    public void ParseDocumentSymbols_nested_DocumentSymbol()
    {
        var json = JsonNode.Parse("""
            [{"name":"Foo","kind":5,"selectionRange":{"start":{"line":0,"character":6},"end":{"line":0,"character":9}},"range":{"start":{"line":0,"character":0},"end":{"line":10,"character":1}},"children":[{"name":"Bar","kind":6,"selectionRange":{"start":{"line":2,"character":4},"end":{"line":2,"character":7}},"range":{"start":{"line":2,"character":4},"end":{"line":5,"character":5}},"children":[]}]}]
            """);
        var symbols = LspNavigationParser.ParseDocumentSymbols(json);
        Assert.Single(symbols);
        Assert.Equal("Foo", symbols[0].Name);
        Assert.Equal(1, symbols[0].Line);
        Assert.Equal(7, symbols[0].Character);
        Assert.Single(symbols[0].Children);
        Assert.Equal("Bar", symbols[0].Children[0].Name);
        Assert.Equal(3, symbols[0].Children[0].Line);
    }

    [Fact]
    public void TryUriToLocalPath_file_uri()
    {
        var path = LspNavigationParser.TryUriToLocalPath("file:///C:/repos/x-pad/App.cs");
        Assert.NotNull(path);
        Assert.Contains("App.cs", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseLocations_references_array()
    {
        var json = JsonNode.Parse("""
            [
              {"uri":"file:///C:/src/a.ts","range":{"start":{"line":1,"character":0},"end":{"line":1,"character":3}}},
              {"uri":"file:///C:/src/b.ts","range":{"start":{"line":8,"character":4},"end":{"line":8,"character":7}}}
            ]
            """);
        var locs = LspNavigationParser.ParseLocations(json);
        Assert.Equal(2, locs.Count);
        Assert.Equal(8, locs[1].Line);
        Assert.Equal(4, locs[1].Character);
    }
}
