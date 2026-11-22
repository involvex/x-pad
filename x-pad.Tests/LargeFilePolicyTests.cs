using x_pad.Services;

namespace x_pad.Tests;

public class LargeFilePolicyTests
{
    [Fact]
    public void IsLarge_threshold_is_8mb()
    {
        Assert.False(LargeFilePolicy.IsLarge(LargeFilePolicy.ThresholdBytes));
        Assert.True(LargeFilePolicy.IsLarge(LargeFilePolicy.ThresholdBytes + 1));
    }

    [Fact]
    public void StatusMessage_includes_size()
    {
        var message = LargeFilePolicy.StatusMessage(9 * 1024 * 1024);
        Assert.Contains("9", message);
        Assert.Contains("folding", message, StringComparison.OrdinalIgnoreCase);
    }
}
