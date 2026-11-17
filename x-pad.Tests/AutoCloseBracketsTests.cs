using AvaloniaEdit.Document;
using x_pad.Editor;

namespace x_pad.Tests;

public class AutoCloseBracketsTests
{
    [Fact]
    public void ShouldSkipTypingCloser_when_next_char_matches()
    {
        var doc = new TextDocument("()");
        Assert.True(AutoCloseBrackets.ShouldSkipTypingCloser(doc, caretOffset: 1, ')'));
    }

    [Fact]
    public void ShouldSkipTypingCloser_false_when_next_char_differs()
    {
        var doc = new TextDocument("(x)");
        Assert.False(AutoCloseBrackets.ShouldSkipTypingCloser(doc, caretOffset: 1, ')'));
    }

    [Fact]
    public void ShouldSkipTypingCloser_false_at_eof()
    {
        var doc = new TextDocument("(");
        Assert.False(AutoCloseBrackets.ShouldSkipTypingCloser(doc, caretOffset: 1, ')'));
    }

    [Fact]
    public void TryGetCloser_maps_pairs()
    {
        Assert.True(AutoCloseBrackets.TryGetCloser('{', out var close));
        Assert.Equal('}', close);
        Assert.True(AutoCloseBrackets.IsOpener('['));
        Assert.False(AutoCloseBrackets.IsOpener('x'));
    }
}
