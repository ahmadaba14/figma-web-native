using System.IO;
using System.IO.Pipes;

namespace FigmaShell.Interop;

/// <summary>
/// Keeps one FigmaShell process per user. Later launches forward their command-line (e.g. a figma.com
/// link) to the running instance over a named pipe and exit.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();

    public SingleInstance(string id)
    {
        _mutex = new Mutex(initiallyOwned: true, $@"Local\{id}.{Environment.UserName}", out bool createdNew);
        IsFirstInstance = createdNew;
        _pipeName = $"{id}.{Environment.UserName}.pipe";
    }

    public bool IsFirstInstance { get; }

    /// <summary>Raised on a thread-pool thread with the forwarded arguments.</summary>
    public event Action<string[]>? ArgumentsReceived;

    public bool TrySendToFirstInstance(string[] args)
    {
        try
        {
            // Let the running instance bring its window to the foreground.
            Native.AllowSetForegroundWindow(Native.ASFW_ANY);
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            client.Connect(3000);
            using var writer = new StreamWriter(client);
            foreach (var a in args) writer.WriteLine(a);
            writer.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void StartListening()
    {
        var token = _cts.Token;
        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        _pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(token);
                    using var reader = new StreamReader(server);
                    var text = await reader.ReadToEndAsync(token);
                    var args = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    ArgumentsReceived?.Invoke(args);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(250, CancellationToken.None);
                }
            }
        }, token);
    }

    public void Dispose()
    {
        _cts.Cancel();
        if (IsFirstInstance)
        {
            try { _mutex.ReleaseMutex(); } catch { /* not owned */ }
        }
        _mutex.Dispose();
    }
}
