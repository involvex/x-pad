using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using x_pad.Models;

namespace x_pad.Services.Lsp;

public sealed class LspManager : IAsyncDisposable
{
    private readonly SettingsService _settings;
    private readonly Func<string?>? _workspaceRootProvider;
    private readonly Dictionary<string, LspClient> _clients = new(StringComparer.OrdinalIgnoreCase);

    public event Action<string, IReadOnlyList<LspDiagnostic>>? DiagnosticsReceived;

    public LspManager(SettingsService settings, Func<string?>? workspaceRootProvider = null)
    {
        _settings = settings;
        _workspaceRootProvider = workspaceRootProvider;
    }

    public async Task<LspClient?> GetClientAsync(string languageId, string? filePath)
    {
        if (string.IsNullOrWhiteSpace(languageId) || languageId == "plaintext")
            return null;

        if (!_settings.Current.LanguageServers.TryGetValue(languageId, out var command) ||
            string.IsNullOrWhiteSpace(command))
            return null;

        if (_clients.TryGetValue(languageId, out var existing))
            return existing;

        var workspaceRoot = _workspaceRootProvider?.Invoke();
        var workspace = !string.IsNullOrWhiteSpace(workspaceRoot) && Directory.Exists(workspaceRoot)
            ? workspaceRoot
            : string.IsNullOrWhiteSpace(filePath)
                ? Environment.CurrentDirectory
                : Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? Environment.CurrentDirectory;

        var client = await LspClient.TryStartAsync(command, workspace);
        if (client == null)
            return null;

        client.DiagnosticsReceived += (uri, diagnostics) =>
            DiagnosticsReceived?.Invoke(uri, diagnostics);

        _clients[languageId] = client;
        return client;
    }

    public async Task ResetAsync()
    {
        foreach (var client in _clients.Values)
            await client.DisposeAsync();
        _clients.Clear();
    }

    public async ValueTask DisposeAsync() => await ResetAsync();
}
