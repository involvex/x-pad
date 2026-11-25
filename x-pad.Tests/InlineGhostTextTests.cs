using x_pad.Editor;

namespace x_pad.Tests;

public class InlineGhostTextTests
{
    [Fact]
    public void TryAccept_ReturnsTextAtMatchingCaret()
    {
        var ghost = new InlineGhostSuggestion(10, "abc");
        Assert.Equal("abc", ghost.TryAccept(10));
        Assert.Null(ghost.TryAccept(11));
    }

    [Fact]
    public void FromCleaned_RejectsEmpty()
    {
        Assert.Null(InlineGhostSuggestion.FromCleaned(0, ""));
        Assert.Null(InlineGhostSuggestion.FromCleaned(0, null));
        Assert.NotNull(InlineGhostSuggestion.FromCleaned(3, "x"));
    }

    [Fact]
    public void ShouldClear_OnCaretOrDocumentChange()
    {
        var ghost = new InlineGhostSuggestion(5, "hint");
        Assert.True(ghost.ShouldClearOnCaretMove(6));
        Assert.False(ghost.ShouldClearOnCaretMove(5));
        Assert.True(ghost.ShouldClearOnDocumentChange(1, 2));
        Assert.False(ghost.ShouldClearOnDocumentChange(4, 4));
    }

    [Fact]
    public void DisplayLine_UsesFirstLineOnly()
    {
        var ghost = new InlineGhostSuggestion(0, "one\ntwo");
        Assert.Equal("one", ghost.DisplayLine);
        Assert.Equal("one\ntwo", ghost.TryAccept(0));
    }
}
