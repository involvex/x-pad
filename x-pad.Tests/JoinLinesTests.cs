using AvaloniaEdit.Document;

namespace x_pad.Tests;

public class JoinLinesTests
{
    [Fact]
    public void JoinSelectedLines_collapses_with_spaces()
    {
        var doc = new TextDocument("one\ntwo\nthree\n");
        const int startLine = 1;
        const int endLine = 3;

        var parts = new List<string>();
        for (var i = startLine; i <= endLine; i++)
        {
            var line = doc.GetLineByNumber(i);
            parts.Add(doc.GetText(line.Offset, line.Length).Trim());
        }

        var first = doc.GetLineByNumber(startLine);
        var last = doc.GetLineByNumber(endLine);
        var startOffset = first.Offset;
        var length = last.Offset + last.TotalLength - startOffset;
        var trailing = last.DelimiterLength > 0
            ? doc.GetText(last.Offset + last.Length, last.DelimiterLength)
            : "";
        var joined = string.Join(' ', parts.Where(p => p.Length > 0));
        doc.Replace(startOffset, length, joined + trailing);

        Assert.Equal("one two three\n", doc.Text);
    }

    [Fact]
    public void JoinTwoLines_keeps_trailing_content()
    {
        var doc = new TextDocument("alpha  \n  beta");
        var startLine = 1;
        var endLine = 2;
        var parts = new List<string>();
        for (var i = startLine; i <= endLine; i++)
        {
            var line = doc.GetLineByNumber(i);
            parts.Add(doc.GetText(line.Offset, line.Length).Trim());
        }

        var first = doc.GetLineByNumber(startLine);
        var last = doc.GetLineByNumber(endLine);
        var startOffset = first.Offset;
        var length = last.Offset + last.TotalLength - startOffset;
        var trailing = last.DelimiterLength > 0
            ? doc.GetText(last.Offset + last.Length, last.DelimiterLength)
            : "";
        doc.Replace(startOffset, length, string.Join(' ', parts.Where(p => p.Length > 0)) + trailing);

        Assert.Equal("alpha beta", doc.Text);
    }

    [Fact]
    public void RemoveEmptyLines_drops_whitespace_only()
    {
        var lines = new[] { "a", "  ", "", "b", "\t", "c" };
        var kept = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        Assert.Equal(new[] { "a", "b", "c" }, kept);
    }

    [Fact]
    public void RemoveDuplicateLines_keeps_first_of_consecutive()
    {
        var lines = new[] { "a", "a", "b", "b", "b", "a" };
        var kept = new List<string>();
        string? prev = null;
        foreach (var line in lines)
        {
            if (prev != null && prev == line)
                continue;
            kept.Add(line);
            prev = line;
        }
        Assert.Equal(new[] { "a", "b", "a" }, kept);
    }
}
