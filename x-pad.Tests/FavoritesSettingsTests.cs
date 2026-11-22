using x_pad.Models;
using Xunit;

namespace x_pad.Tests;

public class FavoritesSettingsTests
{
    [Fact]
    public void FavoriteFiles_defaults_empty_with_cap()
    {
        var s = new AppSettings();
        Assert.Empty(s.FavoriteFiles);
        Assert.Equal(40, s.MaxFavoriteFiles);
    }

    [Fact]
    public void FavoriteFiles_can_store_paths()
    {
        var s = new AppSettings();
        s.FavoriteFiles.Add(@"D:\notes\todo.txt");
        Assert.Single(s.FavoriteFiles);
    }
}
