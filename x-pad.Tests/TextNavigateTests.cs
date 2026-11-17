using AvaloniaEdit.Document;
using Xunit;

namespace x_pad.Tests;

public class TextNavigateTests
{
    [Fact]
    public void ClampMatch_selects_needle_within_line()
    {
        var doc = new TextDocument("alpha\nbeta alpha gamma\n");
        var line = doc.GetLineByNumber(2);
        var column = 6; // 1-based start of "alpha"
        var offset = doc.GetOffset(2, column);
        var length = Math.Clamp(5, 0, doc.TextLength - offset);

        Assert.Equal("alpha", doc.GetText(offset, length));
        Assert.Equal(line.Offset + 5, offset);
    }
}
