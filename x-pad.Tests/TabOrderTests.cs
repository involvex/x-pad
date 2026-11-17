using System.Collections.ObjectModel;

namespace x_pad.Tests;

public class TabOrderTests
{
    [Fact]
    public void Move_left_swaps_with_previous()
    {
        var tabs = new ObservableCollection<string> { "a", "b", "c" };
        const int index = 1;
        tabs.Move(index, index - 1);
        Assert.Equal(new[] { "b", "a", "c" }, tabs);
    }

    [Fact]
    public void Move_right_swaps_with_next()
    {
        var tabs = new ObservableCollection<string> { "a", "b", "c" };
        const int index = 1;
        tabs.Move(index, index + 1);
        Assert.Equal(new[] { "a", "c", "b" }, tabs);
    }
}
