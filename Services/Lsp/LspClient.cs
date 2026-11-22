using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace x_pad.Services.Lsp;

/// <summary>
/// Minimal Content-Length framed LSP client (stdio). Optional — missing servers are ignored.
/// </summary>
public sealed class LspClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly Process _process;
    private readonly Stream _stdin;
    private readonly Stream _stdout;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonNode?>> _pending = new();
    private readonly CancellationTokenSource _cts = new();
    private int _nextId = 1;
    private bool _initialized;
    private readonly string _rootUri;

    public event Action<string, IReadOnlyList<LspDiagnostic>>? DiagnosticsReceived;

    private LspClient(Process process, string rootUri)
    {
        _process = process;
        _stdin = process.StandardInput.BaseStream;
        _stdout = process.StandardOutput.BaseStream;
        _rootUri = rootUri;
        _ = ReadLoopAsync(_cts.Token);
    }

    public static async Task<LspClient?> TryStartAsync(string commandLine, string workspacePath)
    {
        try
        {
            var parts = SplitCommand(commandLine);
            if (parts.Count == 0)
                return null;

            var psi = new ProcessStartInfo
            {
                FileName = parts[0],
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workspacePath
            };
            foreach (var arg in parts.Skip(1))
                psi.ArgumentList.Add(arg);

            var process = Process.Start(psi);
            if (process == null)
                return null;

            var rootUri = new Uri(Path.GetFullPath(workspacePath) + Path.DirectorySeparatorChar).AbsoluteUri;
            var client = new LspClient(process, rootUri);
            await client.InitializeAsync();
            return client;
        }
        catch
        {
            return null;
        }
    }

    private async Task InitializeAsync()
    {
        var init = new JsonObject
        {
            ["processId"] = Environment.ProcessId,
            ["rootUri"] = _rootUri,
            ["capabilities"] = new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["completion"] = new JsonObject
                    {
                        ["completionItem"] = new JsonObject { ["snippetSupport"] = false }
                    },
                    ["hover"] = new JsonObject(),
                    ["publishDiagnostics"] = new JsonObject(),
                    ["definition"] = new JsonObject
                    {
                        ["linkSupport"] = true
                    },
                    ["documentSymbol"] = new JsonObject
                    {
                        ["hierarchicalDocumentSymbolSupport"] = true
                    },
                    ["references"] = new JsonObject()
                }
            },
            ["clientInfo"] = new JsonObject
            {
                ["name"] = "x-pad",
                ["version"] = "0.5.0"
            }
        };

        await RequestAsync("initialize", init);
        await NotifyAsync("initialized", new JsonObject());
        _initialized = true;
    }

    public async Task DidOpenAsync(string uri, string languageId, string text, int version = 1)
    {
        if (!_initialized) return;
        await NotifyAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = languageId,
                ["version"] = version,
                ["text"] = text
            }
        });
    }

    public async Task DidChangeAsync(string uri, string text, int version)
    {
        if (!_initialized) return;
        await NotifyAsync("textDocument/didChange", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["version"] = version
            },
            ["contentChanges"] = new JsonArray
            {
                new JsonObject { ["text"] = text }
            }
        });
    }

    public async Task DidCloseAsync(string uri)
    {
        if (!_initialized) return;
        await NotifyAsync("textDocument/didClose", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri }
        });
    }

    public async Task<IReadOnlyList<LspCompletionItem>> CompletionAsync(string uri, int line, int character)
    {
        if (!_initialized)
            return [];

        var result = await RequestAsync("textDocument/completion", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri },
            ["position"] = new JsonObject { ["line"] = line, ["character"] = character }
        });

        return ParseCompletion(result);
    }

    public async Task<string?> HoverAsync(string uri, int line, int character)
    {
        if (!_initialized)
            return null;

        var result = await RequestAsync("textDocument/hover", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri },
            ["position"] = new JsonObject { ["line"] = line, ["character"] = character }
        });

        return ParseHover(result);
    }

    public async Task<IReadOnlyList<LspLocation>> DefinitionAsync(string uri, int line, int character)
    {
        if (!_initialized)
            return [];

        var result = await RequestAsync("textDocument/definition", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri },
            ["position"] = new JsonObject { ["line"] = line, ["character"] = character }
        });

        return LspNavigationParser.ParseLocations(result);
    }

    public async Task<IReadOnlyList<LspDocumentSymbol>> DocumentSymbolsAsync(string uri)
    {
        if (!_initialized)
            return [];

        var result = await RequestAsync("textDocument/documentSymbol", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri }
        });

        return LspNavigationParser.ParseDocumentSymbols(result);
    }

    public async Task<IReadOnlyList<LspLocation>> ReferencesAsync(
        string uri,
        int line,
        int character,
        bool includeDeclaration = true)
    {
        if (!_initialized)
            return [];

        var result = await RequestAsync("textDocument/references", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri },
            ["position"] = new JsonObject { ["line"] = line, ["character"] = character },
            ["context"] = new JsonObject { ["includeDeclaration"] = includeDeclaration }
        });

        return LspNavigationParser.ParseLocations(result);
    }

    private static IReadOnlyList<LspCompletionItem> ParseCompletion(JsonNode? node)
    {
        if (node == null)
            return [];

        JsonArray? items = null;
        if (node is JsonArray arr)
            items = arr;
        else if (node["items"] is JsonArray nested)
            items = nested;

        if (items == null)
            return [];

        var list = new List<LspCompletionItem>();
        foreach (var item in items)
        {
            if (item == null) continue;
            list.Add(new LspCompletionItem
            {
                Label = item["label"]?.GetValue<string>() ?? "",
                Detail = item["detail"]?.GetValue<string>(),
                InsertText = item["insertText"]?.GetValue<string>(),
                Kind = item["kind"]?.GetValue<int>()
            });
        }
        return list;
    }

    private static string? ParseHover(JsonNode? node)
    {
        if (node == null)
            return null;

        var contents = node["contents"];
        if (contents == null)
            return null;

        if (contents is JsonValue value)
            return value.ToString();

        if (contents["value"] != null)
            return contents["value"]!.GetValue<string>();

        if (contents is JsonArray array)
        {
            var parts = new List<string>();
            foreach (var part in array)
            {
                if (part is JsonValue v)
                    parts.Add(v.ToString());
                else if (part?["value"] != null)
                    parts.Add(part["value"]!.GetValue<string>());
            }
            return string.Join("\n", parts);
        }

        return contents.ToJsonString();
    }

    private async Task<JsonNode?> RequestAsync(string method, JsonObject parameters)
    {
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        var message = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters
        };
        await WriteAsync(message);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var reg = timeout.Token.Register(() => tcs.TrySetResult(null));
        return await tcs.Task;
    }

    private Task NotifyAsync(string method, JsonObject parameters)
    {
        var message = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
            ["params"] = parameters
        };
        return WriteAsync(message);
    }

    private async Task WriteAsync(JsonObject message)
    {
        var json = message.ToJsonString(JsonOptions);
        var body = Encoding.UTF8.GetBytes(json);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        await _stdin.WriteAsync(header);
        await _stdin.WriteAsync(body);
        await _stdin.FlushAsync();
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buffer = new MemoryStream();
        var readBuffer = new byte[4096];

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var read = await _stdout.ReadAsync(readBuffer.AsMemory(0, readBuffer.Length), ct);
                if (read == 0)
                    break;

                buffer.Write(readBuffer, 0, read);
                while (TryConsumeMessage(buffer, out var message))
                    HandleMessage(message);
            }
        }
        catch
        {
            // Server exited
        }
    }

    private static bool TryConsumeMessage(MemoryStream buffer, out JsonNode? message)
    {
        message = null;
        var data = buffer.ToArray();
        var text = Encoding.UTF8.GetString(data);
        var headerEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (headerEnd < 0)
            return false;

        var headers = text[..headerEnd];
        var length = 0;
        foreach (var line in headers.Split("\r\n"))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                length = int.Parse(line["Content-Length:".Length..].Trim());
        }

        var bodyStart = headerEnd + 4;
        var bodyBytes = Encoding.UTF8.GetBytes(text[bodyStart..]);
        // Use original byte offsets more carefully
        var headerByteCount = Encoding.UTF8.GetByteCount(text[..bodyStart]);
        if (data.Length < headerByteCount + length)
            return false;

        var jsonBytes = data.AsSpan(headerByteCount, length);
        message = JsonNode.Parse(Encoding.UTF8.GetString(jsonBytes));

        var remaining = data.AsSpan(headerByteCount + length).ToArray();
        buffer.SetLength(0);
        buffer.Write(remaining);
        return true;
    }

    private void HandleMessage(JsonNode? message)
    {
        if (message == null)
            return;

        if (message["id"] != null && message["method"] == null)
        {
            var id = message["id"]!.GetValue<int>();
            if (_pending.TryRemove(id, out var tcs))
                tcs.TrySetResult(message["result"]);
            return;
        }

        var method = message["method"]?.GetValue<string>();
        if (method == "textDocument/publishDiagnostics")
        {
            var parms = message["params"];
            if (parms == null) return;
            var uri = parms["uri"]?.GetValue<string>() ?? "";
            var diagnostics = new List<LspDiagnostic>();
            if (parms["diagnostics"] is JsonArray arr)
            {
                foreach (var d in arr)
                {
                    if (d == null) continue;
                    diagnostics.Add(new LspDiagnostic
                    {
                        Message = d["message"]?.GetValue<string>() ?? "",
                        Severity = d["severity"]?.GetValue<int>() ?? 1,
                        Source = d["source"]?.GetValue<string>(),
                        Range = new LspRange
                        {
                            Start = new LspPosition
                            {
                                Line = d["range"]?["start"]?["line"]?.GetValue<int>() ?? 0,
                                Character = d["range"]?["start"]?["character"]?.GetValue<int>() ?? 0
                            },
                            End = new LspPosition
                            {
                                Line = d["range"]?["end"]?["line"]?.GetValue<int>() ?? 0,
                                Character = d["range"]?["end"]?["character"]?.GetValue<int>() ?? 0
                            }
                        }
                    });
                }
            }
            DiagnosticsReceived?.Invoke(uri, diagnostics);
        }
    }

    private static List<string> SplitCommand(string commandLine)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        foreach (var ch in commandLine)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }
            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }
            current.Append(ch);
        }
        if (current.Length > 0)
            result.Add(current.ToString());
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            if (_initialized)
                await NotifyAsync("shutdown", new JsonObject());
        }
        catch { /* ignore */ }

        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch { /* ignore */ }

        _process.Dispose();
        _cts.Dispose();
    }
}
