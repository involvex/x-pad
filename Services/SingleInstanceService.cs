using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace x_pad.Services;

public sealed class SingleInstanceService : IDisposable
{
    public const string DefaultMutexName = "Local\\XPadSingleInstance";
    public const string DefaultPipeName = "x-pad-instance";

    private readonly Mutex _mutex;
    private readonly bool _isPrimary;
    private readonly string _pipeName;
    private CancellationTokenSource? _listenCts;

    public SingleInstanceService()
        : this(DefaultMutexName, DefaultPipeName)
    {
    }

    public SingleInstanceService(string mutexName, string pipeName)
    {
        _pipeName = pipeName;
        _mutex = new Mutex(true, mutexName, out _isPrimary);
    }

    public bool IsPrimary => _isPrimary;

    public string PipeName => _pipeName;

    public event Action<string[]>? FilesReceived;

    public bool TrySendToPrimary(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            client.Connect(2000);
            var payload = JsonSerializer.Serialize(args);
            var bytes = Encoding.UTF8.GetBytes(payload);
            client.Write(bytes);
            client.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void StartListening()
    {
        if (!_isPrimary)
            return;

        _listenCts = new CancellationTokenSource();
        _ = ListenLoopAsync(_listenCts.Token);
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct);
                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                var json = await reader.ReadToEndAsync(ct);
                if (string.IsNullOrWhiteSpace(json))
                    continue;

                var files = JsonSerializer.Deserialize<string[]>(json) ?? [];
                if (files.Length > 0)
                    FilesReceived?.Invoke(files);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                try
                {
                    await Task.Delay(200, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    public void Dispose()
    {
        _listenCts?.Cancel();
        _listenCts?.Dispose();
        if (_isPrimary)
        {
            try { _mutex.ReleaseMutex(); } catch { /* ignore */ }
        }
        _mutex.Dispose();
    }
}
