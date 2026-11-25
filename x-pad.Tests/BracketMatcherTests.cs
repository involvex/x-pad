using AvaloniaEdit.Document;
using x_pad.Editor;

namespace x_pad.Tests;

public class BracketMatcherTests
{
    [Fact]
    public void Finds_matching_parens_from_open()
    {
        var doc = new TextDocument("foo(bar)");
        var match = BracketMatcher.FindMatch(doc, 4);
        Assert.NotNull(match);
        Assert.Equal(3, match!.Value.OpenOffset);
        Assert.Equal(7, match.Value.CloseOffset);
    }

    [Fact]
    public void Finds_matching_braces_from_close()
    {
        var doc = new TextDocument("a{b{c}}");
        var match = BracketMatcher.FindMatch(doc, 7);
        Assert.NotNull(match);
        Assert.Equal(1, match!.Value.OpenOffset);
        Assert.Equal(6, match.Value.CloseOffset);
    }

    [Fact]
    public void Returns_null_when_unbalanced()
    {
        var doc = new TextDocument("((oops)");
        Assert.Null(BracketMatcher.FindMatch(doc, 0));
    }

    [Fact]
    public void Returns_null_when_match_exceeds_scan_cap()
    {
        var open = new string('{', 1);
        var filler = new string('x', BracketMatcher.MaxScanDistance + 10);
        var doc = new TextDocument(open + filler + "}");
        Assert.Null(BracketMatcher.FindMatch(doc, 0));
    }
}