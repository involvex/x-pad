using x_pad.Services;

namespace x_pad.Tests;

public class SingleInstanceServiceTests
{
    [Fact]
    public async Task Secondary_sends_paths_to_primary_listener()
    {
        var id = Guid.NewGuid().ToString("N");
        var mutex = $"Local\\XPadTestMutex-{id}";
        var pipe = $"x-pad-test-{id}";

        using var primary = new SingleInstanceService(mutex, pipe);
        Assert.True(primary.IsPrimary);

        string[]? received = null;
        var got = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        primary.FilesReceived += paths =>
        {
            received = paths;
            got.TrySetResult(true);
        };
        primary.StartListening();

        // Give the listen loop time to create the pipe server.
        await Task.Delay(150);

        using var secondary = new SingleInstanceService(mutex, pipe);
        Assert.False(secondary.IsPrimary);

        var payload = new[]
        {
            Path.Combine(Path.GetTempPath(), "a.txt"),
            Path.Combine(Path.GetTempPath(), "b.txt")
        };
        Assert.True(secondary.TrySendToPrimary(payload));

        var completed = await Task.WhenAny(got.Task, Task.Delay(3000));
        Assert.Same(got.Task, completed);
        Assert.NotNull(received);
        Assert.Equal(payload, received);
    }

    [Fact]
    public void TrySendToPrimary_returns_false_when_nobody_listens()
    {
        var id = Guid.NewGuid().ToString("N");
        using var orphan = new SingleInstanceService($"Local\\XPadOrphan-{id}", $"x-pad-orphan-{id}");
        Assert.False(orphan.TrySendToPrimary(["nope.txt"]));
    }
}
