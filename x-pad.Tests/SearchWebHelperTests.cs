using x_pad.Services;
using Xunit;

namespace x_pad.Tests;

public class SearchWebHelperTests
{
    [Fact]
    public void BuildGoogleSearchUrl_encodes_spaces_and_symbols()
    {
        var url = SearchWebHelper.BuildGoogleSearchUrl("foo bar & baz");
        Assert.Equal("https://www.google.com/search?q=foo%20bar%20%26%20baz", url);
    }

    [Fact]
    public void BuildGoogleSearchUrl_trims_query()
    {
        var url = SearchWebHelper.BuildGoogleSearchUrl("  hello  ");
        Assert.Equal("https://www.google.com/search?q=hello", url);
    }
}
