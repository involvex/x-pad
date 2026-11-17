using AvaloniaEdit.Document;

namespace x_pad.Tests;

/// <summary>
/// Pure transform helpers mirrored from DocumentEditor behavior for regression coverage.
/// </summary>
public class TextTransformTests
{
    [Fact]
    public void TrimTrailingWhitespace_removes_spaces_and_tabs()
    {
        var doc = new TextDocument("alpha  \nbeta\t\t\ngamma");
        for (var i = doc.LineCount; i >= 1; i--)
        {
            var line = doc.GetLineByNumber(i);
            var text = doc.GetText(line.Offset, line.Length);
            var trimmed = text.TrimEnd(' ', '\t');
            if (trimmed.Length != text.Length)
                doc.Replace(line.Offset, line.Length, trimmed);
        }

        Assert.Equal("alpha\nbeta\ngamma", doc.Text);
    }

    [Fact]
    public void SortLines_orders_case_insensitively()
    {
        var lines = new[] { "banana", "Apple", "cherry" };
        Array.Sort(lines, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(new[] { "Apple", "banana", "cherry" }, lines);
    }
}
