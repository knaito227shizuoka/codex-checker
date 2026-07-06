using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace CodexChecker;

public sealed class CodexCommandNotFoundException : Exception
{
    public CodexCommandNotFoundException(Exception innerException)
        : base("codex コマンドが見つかりません。PATH を確認してください。", innerException)
    {
    }
}

public sealed class AppServerClient : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private Process? _process;
    private CancellationTokenSource? _readerCts;
    private Task? _readerTask;
    private int _nextId;
    private bool _disposed;

    public async Task<RateLimitSnapshot> ReadRateLimitsAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        var response = await SendRpcAsync("account/rateLimits/read", new { }, timeout, cancellationToken).ConfigureAwait(false);
        return ParseRateLimits(response);
    }

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StopProcess();
            await StartProcessAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public static bool TryExtractRateLimitsResponse(string line, int expectedId, out RateLimitSnapshot snapshot)
    {
        snapshot = new RateLimitSnapshot(null, null);

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("id", out var idElement) ||
                idElement.ValueKind != JsonValueKind.Number ||
                idElement.GetInt32() != expectedId)
            {
                return false;
            }

            snapshot = ParseRateLimits(root);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_process is null || _process.HasExited)
            {
                StopProcess();
                await StartProcessAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task StartProcessAsync(CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "codex",
            Arguments = "-s read-only -a never app-server",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        try
        {
            _process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new CodexCommandNotFoundException(ex);
        }

        _readerCts = new CancellationTokenSource();
        _readerTask = Task.Run(() => ReadLoopAsync(_readerCts.Token), CancellationToken.None);

        await SendRpcAsync("initialize", new
        {
            clientInfo = new
            {
                name = "codex-checker-resident",
                version = "0.1.0"
            }
        }, TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);

    }

    private async Task<JsonElement> SendRpcAsync(string method, object parameters, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (_process is null || _process.HasExited)
        {
            throw new InvalidOperationException("app-server is not running.");
        }

        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        var payload = JsonSerializer.Serialize(new
        {
            id,
            method,
            @params = parameters
        });

        try
        {
            await _process.StandardInput.WriteLineAsync(payload).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync().ConfigureAwait(false);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            using (timeoutCts.Token.Register(() => tcs.TrySetCanceled(timeoutCts.Token)))
            {
                return await tcs.Task.ConfigureAwait(false);
            }
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        if (_process is null)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested && !_process.HasExited)
        {
            string? line;
            try
            {
                line = await _process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (line is null)
            {
                return;
            }

            DispatchLine(line);
        }
    }

    private void DispatchLine(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.Number)
            {
                return;
            }

            var id = idElement.GetInt32();
            if (_pending.TryGetValue(id, out var tcs))
            {
                tcs.TrySetResult(root.Clone());
            }
        }
        catch (JsonException)
        {
            // app-server may write logs or notifications; they are irrelevant for request matching.
        }
    }

    private static RateLimitSnapshot ParseRateLimits(JsonElement response)
    {
        if (!response.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("rateLimits", out var rateLimits))
        {
            return new RateLimitSnapshot(null, null);
        }

        return new RateLimitSnapshot(
            ParseWindow(rateLimits, "primary"),
            ParseWindow(rateLimits, "secondary"));
    }

    private static RateLimitWindow? ParseWindow(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var usedPercent = ReadInt(element, "usedPercent");
        if (usedPercent is null)
        {
            return null;
        }

        return new RateLimitWindow(
            usedPercent.Value,
            ReadDateTimeOffset(element, "resetsAt"),
            ReadInt(element, "resetsInSeconds"));
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value))
        {
            return value;
        }

        return property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out value)
            ? value
            : null;
    }

    private static DateTimeOffset? ReadDateTimeOffset(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateTimeOffset.TryParse(property.GetString(), out var value) ? value : null;
    }

    private void StopProcess()
    {
        _readerCts?.Cancel();
        _readerCts?.Dispose();
        _readerCts = null;
        _readerTask = null;

        foreach (var pending in _pending.Values)
        {
            pending.TrySetCanceled();
        }

        _pending.Clear();

        if (_process is not null)
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }

                _process.WaitForExit(1000);
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                _process.Dispose();
                _process = null;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopProcess();
        _gate.Dispose();
        _disposed = true;
    }
}
