using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Quill.Settings.Taskbar;

/// <summary>
/// Coordinates per-executable single-instance activation forwarding over a user-scoped named pipe
/// so Jump List clicks, file opens, and "New Window" tasks reuse the active process cleanly while
/// keeping Debug, Release, and side-by-side builds isolated.
/// </summary>
public static class SingleInstanceBridge
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static string ComputeInstanceKey(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        string normalized = Path.GetFullPath(executablePath).ToUpperInvariant();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash.AsSpan(0, 8));
    }

    public static string PipeNameForKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return $"Quill.Instance.{key}";
    }

    public static string MutexNameForKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return $"Local\\Quill.Instance.{key}";
    }

    public static SingleInstanceServer? AcquireOrForward(
        string executablePath,
        LaunchRequest request,
        out bool forwarded,
        int forwardTimeoutMs = 600)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(request);

        string key = ComputeInstanceKey(executablePath);
        string mutexName = MutexNameForKey(key);
        string pipeName = PipeNameForKey(key);

        Mutex? mutex = null;
        bool createdNew;
        try
        {
            mutex = new Mutex(initiallyOwned: true, mutexName, out createdNew);
        }
        catch (AbandonedMutexException ex)
        {
            mutex = ex.Mutex;
            createdNew = true;
        }
        catch (Exception)
        {
            forwarded = false;
            return null;
        }

        if (createdNew)
        {
            forwarded = false;
            return new SingleInstanceServer(mutex, pipeName);
        }

        mutex?.Dispose();
        forwarded = TryForwardToPipe(pipeName, request, forwardTimeoutMs);
        return null;
    }

    internal static bool TryForwardToPipe(string pipeName, LaunchRequest request, int timeoutMs = 600)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentNullException.ThrowIfNull(request);

        return Task.Run(() => TryForwardToPipeAsync(pipeName, request, timeoutMs)).GetAwaiter().GetResult();
    }

    private static async Task<bool> TryForwardToPipeAsync(string pipeName, LaunchRequest request, int timeoutMs)
    {
        long start = Stopwatch.GetTimestamp();
        int effectiveTimeout = Math.Max(50, timeoutMs);
        string payload = JsonSerializer.Serialize(request);

        while (true)
        {
            int elapsedMs = (int)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            int remainingMs = effectiveTimeout - elapsedMs;
            if (remainingMs <= 0)
            {
                return false;
            }

            try
            {
                using var client = new NamedPipeClientStream(
                    ".",
                    pipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                using var connectCts = new CancellationTokenSource(Math.Min(remainingMs, 200));
                await client.ConnectAsync(connectCts.Token).ConfigureAwait(false);

                using var ioCts = new CancellationTokenSource(Math.Max(effectiveTimeout, 500));
                using var reader = new StreamReader(client, Utf8NoBom, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                using var writer = new StreamWriter(client, Utf8NoBom, leaveOpen: true) { AutoFlush = true };

                string? pidLine = await reader.ReadLineAsync(ioCts.Token).ConfigureAwait(false);
                if (int.TryParse(pidLine, NumberStyles.Integer, CultureInfo.InvariantCulture, out int serverPid) &&
                    serverPid > 0 &&
                    OperatingSystem.IsWindows())
                {
                    NativeWindowFocus.TryAllowSetForegroundWindow(serverPid);
                }

                await writer.WriteLineAsync(payload.AsMemory(), ioCts.Token).ConfigureAwait(false);
                await writer.FlushAsync(ioCts.Token).ConfigureAwait(false);

                string? ack = await reader.ReadLineAsync(ioCts.Token).ConfigureAwait(false);
                return string.Equals(ack, "OK", StringComparison.Ordinal);
            }
            catch (Exception error) when (error is TimeoutException or IOException or OperationCanceledException)
            {
                int afterMs = (int)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                if (afterMs >= effectiveTimeout)
                {
                    return false;
                }

                await Task.Delay(Math.Min(20, effectiveTimeout - afterMs)).ConfigureAwait(false);
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static class NativeWindowFocus
    {
        public static void TryAllowSetForegroundWindow(int processId)
        {
            try
            {
                _ = AllowSetForegroundWindow(processId);
            }
            catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
            {
            }
        }

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllowSetForegroundWindow(int dwProcessId);
    }
}

/// <summary>
/// Listens on a user-scoped named pipe for forwarded <see cref="LaunchRequest"/> instances,
/// buffering arrivals until the application registers its dispatcher handler.
/// </summary>
public sealed class SingleInstanceServer : IDisposable
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly Mutex? _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _sync = new();
    private readonly Queue<LaunchRequest> _pending = new();
    private readonly Task _acceptLoop;
    private Action<LaunchRequest>? _handler;
    private int _disposed;

    internal SingleInstanceServer(Mutex? mutex, string pipeName)
    {
        _mutex = mutex;
        _pipeName = pipeName;

        NamedPipeServerStream? initial = null;
        try
        {
            initial = CreateServerStream(pipeName);
        }
        catch (Exception)
        {
        }

        _acceptLoop = Task.Run(() => AcceptLoopAsync(initial));
    }

    internal static SingleInstanceServer StartForPipe(string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        return new SingleInstanceServer(mutex: null, pipeName);
    }

    public void SetHandler(Action<LaunchRequest> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        List<LaunchRequest>? drained = null;
        lock (_sync)
        {
            _handler = handler;
            if (_pending.Count > 0)
            {
                drained = new List<LaunchRequest>(_pending.Count);
                while (_pending.TryDequeue(out var request))
                {
                    drained.Add(request);
                }
            }
        }

        if (drained is not null)
        {
            foreach (var request in drained)
            {
                handler(request);
            }
        }
    }

    private static NamedPipeServerStream CreateServerStream(string pipeName) =>
        new(
            pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private async Task AcceptLoopAsync(NamedPipeServerStream? initial)
    {
        NamedPipeServerStream? server = initial;

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                server ??= CreateServerStream(_pipeName);
                await server.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);

                var connected = server;
                server = null;
                _ = HandleClientAsync(connected, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                server?.Dispose();
                server = null;
                break;
            }
            catch (Exception)
            {
                server?.Dispose();
                server = null;
                if (!_cts.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(50, _cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        using (pipe)
        using (var ioCts = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            ioCts.CancelAfter(2000);
            try
            {
                using var reader = new StreamReader(pipe, Utf8NoBom, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                using var writer = new StreamWriter(pipe, Utf8NoBom, leaveOpen: true) { AutoFlush = true };

                await writer.WriteLineAsync(Environment.ProcessId.ToString(CultureInfo.InvariantCulture).AsMemory(), ioCts.Token).ConfigureAwait(false);
                await writer.FlushAsync(ioCts.Token).ConfigureAwait(false);

                string? line = await reader.ReadLineAsync(ioCts.Token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(line) &&
                    JsonSerializer.Deserialize<LaunchRequest>(line) is { } request)
                {
                    Deliver(request);
                    await writer.WriteLineAsync("OK".AsMemory(), ioCts.Token).ConfigureAwait(false);
                    await writer.FlushAsync(ioCts.Token).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
            }
        }
    }

    private void Deliver(LaunchRequest request)
    {
        Action<LaunchRequest>? handler;
        lock (_sync)
        {
            handler = _handler;
            if (handler is null)
            {
                _pending.Enqueue(request);
                return;
            }
        }

        handler(request);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _cts.Cancel();
        try
        {
            _acceptLoop.Wait(TimeSpan.FromMilliseconds(250));
        }
        catch (Exception)
        {
        }

        _cts.Dispose();

        if (_mutex is not null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
            finally
            {
                _mutex.Dispose();
            }
        }
    }
}
