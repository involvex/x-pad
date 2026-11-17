using x_pad.Editor;

namespace x_pad.Tests;

public class OccurrenceSearchTests
{
    [Fact]
    public void FindNext_skips_identifier_inside_longer_word()
    {
        const string text = "alpha alphabet alpha";
        var first = OccurrenceSearch.FindNext(text, "alpha", 0);
        Assert.Equal((0, 5), first);

        var second = OccurrenceSearch.FindNext(text, "alpha", first!.Value.Start + 1);
        Assert.Equal((15, 5), second);
    }

    [Fact]
    public void FindNext_matches_plain_substring_when_not_identifier()
    {
        var hit = OccurrenceSearch.FindNext("a::b::c", "::", 0);
        Assert.Equal((1, 2), hit);
    }
}
