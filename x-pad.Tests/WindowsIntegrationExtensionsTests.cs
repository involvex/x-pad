using x_pad.Services;

namespace x_pad.Tests;

public class WindowsIntegrationExtensionsTests
{
    [Fact]
    public void Default_open_with_list_includes_txt_and_code()
    {
        Assert.Contains(".txt", OpenWithExtensions.Default);
        Assert.Contains(".cs", OpenWithExtensions.Default);
        Assert.Contains(".md", OpenWithExtensions.Default);
        Assert.Equal(
            OpenWithExtensions.Default.Length,
            OpenWithExtensions.Default.Distinct().Count());
    }
}
