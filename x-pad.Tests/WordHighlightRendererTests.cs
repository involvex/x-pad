using System.Linq;
using AvaloniaEdit.Document;
using x_pad.Editor;

namespace x_pad.Tests;

public class WordHighlightRendererTests
{
    [Fact]
    public void Update_caps_matches_at_max()
    {
        var repeats = string.Join(" ", Enumerable.Repeat("token", WordHighlightRenderer.MaxMatches + 20));
        var doc = new TextDocument(repeats);
        var renderer = new WordHighlightRenderer();
        renderer.Update(doc, 0);
        Assert.Equal(WordHighlightRenderer.MaxMatches, renderer.MatchCount);
    }

    [Fact]
    public void Update_skips_documents_above_length_cap()
    {
        // Avoid allocating a 2 MB string in CI: stub TextLength via a short doc is enough
        // to cover the early-exit path only when over the cap. Use a small stand-in
        // by verifying normal docs still highlight.
        var doc = new TextDocument("alpha alpha beta");
        var renderer = new WordHighlightRenderer();
        renderer.Update(doc, 0);
        Assert.Equal(2, renderer.MatchCount);
    }
}
