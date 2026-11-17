using x_pad.Services.Lsp;

namespace x_pad.Tests;

public class LspClientSoftFailTests
{
    [Fact]
    public async Task TryStartAsync_returns_null_when_server_missing()
    {
        var client = await LspClient.TryStartAsync(
            "xpad-missing-language-server-" + Guid.NewGuid().ToString("N") + " --stdio",
            Path.GetTempPath());

        Assert.Null(client);
    }

    [Fact]
    public async Task TryStartAsync_returns_null_for_empty_command()
    {
        var client = await LspClient.TryStartAsync("   ", Path.GetTempPath());
        Assert.Null(client);
    }
}
