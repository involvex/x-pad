using x_pad.Editor;
using Xunit;

namespace x_pad.Tests;

public class InvertSelectionTests
{
    [Fact]
    public void Empty_selection_selects_whole_document()
    {
        var ranges = InvertSelection.ComputeRanges(10, 3, 0);
        Assert.Equal([(0, 10)], ranges);
    }

    [Fact]
    public void Middle_selection_yields_two_complements()
    {
        var ranges = InvertSelection.ComputeRanges(10, 2, 3);
        Assert.Equal(2, ranges.Count);
        Assert.Equal((0, 2), ranges[0]);
        Assert.Equal((5, 5), ranges[1]);
    }

    [Fact]
    public void Full_selection_yields_nothing()
    {
        var ranges = InvertSelection.ComputeRanges(8, 0, 8);
        Assert.Empty(ranges);
    }
}
