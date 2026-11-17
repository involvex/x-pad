using AvaloniaEdit.Document;
using x_pad.Editor;

namespace x_pad.Tests;

public class BookmarkStoreTests
{
    [Fact]
    public void Toggle_and_navigate_wraps()
    {
        var doc = new TextDocument("one\ntwo\nthree\nfour\n");
        var store = new BookmarkStore();
        store.Attach(doc);

        Assert.True(store.ToggleAtOffset(0));          // line 1
        Assert.True(store.ToggleAtOffset(doc.GetOffset(3, 1))); // line 3
        Assert.Equal(2, store.Count);

        var next = store.GoToNext(0);
        Assert.Equal(doc.GetOffset(3, 1), next);

        var wrap = store.GoToNext(doc.GetOffset(3, 1));
        Assert.Equal(doc.GetOffset(1, 1), wrap);

        Assert.False(store.ToggleAtOffset(0)); // remove line 1
        Assert.Equal(1, store.Count);
    }
}
