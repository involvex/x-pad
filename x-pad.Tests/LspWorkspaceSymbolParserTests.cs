using System.Text.Json.Nodes;
using x_pad.Services.Lsp;

namespace x_pad.Tests;

public class LspWorkspaceSymbolParserTests
{
    [Fact]
    public void ParseWorkspaceSymbols_null_and_non_array_are_empty()
    {
        Assert.Empty(LspNavigationParser.ParseWorkspaceSymbols(null));
        Assert.Empty(LspNavigationParser.ParseWorkspaceSymbols(JsonNode.Parse("""{"name":"x"}""")));
    }

    [Fact]
    public void ParseWorkspaceSymbols_SymbolInformation()
    {
        var json = JsonNode.Parse("""
            [
              {
                "name": "Foo",
                "kind": 5,
                "containerName": "Lib",
                "location": {
                  "uri": "file:///C:/src/a.cs",
                  "range": {
                    "start": { "line": 10, "character": 2 },
                    "end": { "line": 10, "character": 5 }
                  }
                }
              }
            ]
            """);
        var symbols = LspNavigationParser.ParseWorkspaceSymbols(json);
        var hit = Assert.Single(symbols);
        Assert.Equal("Foo", hit.Name);
        Assert.Equal(5, hit.Kind);
        Assert.Equal("Lib", hit.ContainerName);
        Assert.Equal("file:///C:/src/a.cs", hit.Location.Uri);
        Assert.Equal(10, hit.Location.Line);
        Assert.Equal(2, hit.Location.Character);
        Assert.Equal(10, hit.Location.EndLine);
        Assert.Equal(5, hit.Location.EndCharacter);
    }

    [Fact]
    public void ParseWorkspaceSymbols_WorkspaceSymbol_with_Location()
    {
        var json = JsonNode.Parse("""
            [
              {
                "name": "Bar",
                "kind": 6,
                "location": {
                  "uri": "file:///C:/src/b.ts",
                  "range": {
                    "start": { "line": 3, "character": 4 },
                    "end": { "line": 3, "character": 7 }
                  }
                }
              }
            ]
            """);
        var symbols = LspNavigationParser.ParseWorkspaceSymbols(json);
        var hit = Assert.Single(symbols);
        Assert.Equal("Bar", hit.Name);
        Assert.Equal(6, hit.Kind);
        Assert.Null(hit.ContainerName);
        Assert.Equal(3, hit.Location.Line);
        Assert.Equal(4, hit.Location.Character);
    }

    [Fact]
    public void ParseWorkspaceSymbols_uri_only_location_opens_at_origin()
    {
        var json = JsonNode.Parse("""
            [
              {
                "name": "NeedsResolve",
                "kind": 12,
                "location": { "uri": "file:///C:/src/c.rs" }
              }
            ]
            """);
        var symbols = LspNavigationParser.ParseWorkspaceSymbols(json);
        var hit = Assert.Single(symbols);
        Assert.Equal("NeedsResolve", hit.Name);
        Assert.Equal("file:///C:/src/c.rs", hit.Location.Uri);
        Assert.Equal(0, hit.Location.Line);
        Assert.Equal(0, hit.Location.Character);
    }

    [Fact]
    public void ParseWorkspaceSymbols_skips_nameless_and_uriless()
    {
        var json = JsonNode.Parse("""
            [
              { "kind": 5, "location": { "uri": "file:///C:/src/a.cs" } },
              { "name": "Orphan", "kind": 5, "location": {} },
              {
                "name": "Keep",
                "kind": 5,
                "location": {
                  "uri": "file:///C:/src/keep.cs",
                  "range": { "start": { "line": 1, "character": 0 }, "end": { "line": 1, "character": 4 } }
                }
              }
            ]
            """);
        var symbols = LspNavigationParser.ParseWorkspaceSymbols(json);
        var hit = Assert.Single(symbols);
        Assert.Equal("Keep", hit.Name);
    }

    [Fact]
    public void ParseWorkspaceSymbols_caps_hits()
    {
        var items = string.Join(",", Enumerable.Range(0, 12).Select(i =>
            "{\"name\":\"S" + i +
            "\",\"kind\":13,\"location\":{\"uri\":\"file:///C:/src/a.cs\",\"range\":{\"start\":{\"line\":" +
            i + ",\"character\":0},\"end\":{\"line\":" + i + ",\"character\":1}}}}"));
        var json = JsonNode.Parse("[" + items + "]");
        var symbols = LspNavigationParser.ParseWorkspaceSymbols(json, maxHits: 5);
        Assert.Equal(5, symbols.Count);
        Assert.Equal("S0", symbols[0].Name);
        Assert.Equal("S4", symbols[4].Name);
    }

    [Fact]
    public void SupportsWorkspaceSymbol_boolean_and_options()
    {
        Assert.False(LspCapabilities.SupportsWorkspaceSymbol(null));
        Assert.False(LspCapabilities.SupportsWorkspaceSymbol(JsonNode.Parse(
            """{"capabilities":{}}""")));
        Assert.False(LspCapabilities.SupportsWorkspaceSymbol(JsonNode.Parse(
            """{"capabilities":{"workspaceSymbolProvider":false}}""")));
        Assert.True(LspCapabilities.SupportsWorkspaceSymbol(JsonNode.Parse(
            """{"capabilities":{"workspaceSymbolProvider":true}}""")));
        Assert.True(LspCapabilities.SupportsWorkspaceSymbol(JsonNode.Parse(
            """{"capabilities":{"workspaceSymbolProvider":{"resolveProvider":true}}}""")));
    }
}
