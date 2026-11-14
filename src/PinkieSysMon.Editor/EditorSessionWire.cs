using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace PinkieSysMon.Editor;

// A process name is only a discovery hint; identity comes from a same-user
// private endpoint, a process creation timestamp, and an exact application root.
internal sealed record EditorSessionSnapshot(string Root, int ProcessId, long StartedUtcTicks,
    string? ActiveDashboard);

internal sealed record EditorSessionCloseResult(bool Closed, string Message);

internal sealed record EditorSessionReply(string RequestId, bool Accepted, string Message,
    EditorSessionSnapshot? Session);

internal sealed record EditorSessionRequest(string RequestId, string Action, string? Root,
    long? StartedUtcTicks, string? ExpectedDashboard);

internal static class EditorSessionWire
{
    private const string PipePrefix = "PinkieSysMon.Editor.Session.v1.";
    private const string StartupGatePrefix = @"Global\PinkieSysMon.Editor.StartupGate.v1.";

    internal static string NormalizeRoot(string root) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

    internal static string CurrentUserSid()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Editor session coordination requires Windows.");
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value ??
            throw new InvalidOperationException("Windows account identity cannot be determined.");
    }

    internal static string PipeName(int pid, string sid) =>
        PipePrefix + sid + "." + pid;

    internal static string CurrentPipeName(int pid) => PipeName(pid, CurrentUserSid());

    internal static string StartupGateName(string root, string sid) =>
        StartupGatePrefix + sid + "." +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeRoot(root).ToUpperInvariant())));

    internal static IDisposable AcquireStartupGate(string root, TimeSpan? timeout = null)
    {
        var mutex = new Mutex(false, StartupGateName(root, CurrentUserSid()));
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(timeout ?? TimeSpan.FromSeconds(20)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired)
                throw new TimeoutException("Another process is committing Settings for this installation. Editor was not started.");
            return new StartupGateLease(mutex);
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    private sealed class StartupGateLease(Mutex mutex) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }

    internal static async Task<EditorSessionReply> SendAsync(int pid, EditorSessionRequest request,
        int timeoutMs = 6000, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMs);
        using var pipe = new NamedPipeClientStream(".", CurrentPipeName(pid),
            PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true)
            { AutoFlush = true, NewLine = "\n" };
            await writer.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), timeout.Token)
                .ConfigureAwait(false);
            var line = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
            if (line is null || line.Length > 4096)
                throw new InvalidDataException("Editor session returned an empty or oversized acknowledgement.");
            var response = JsonSerializer.Deserialize<EditorSessionReply>(line)
                ?? throw new InvalidDataException("Editor session returned no acknowledgement.");
            if (response.RequestId != request.RequestId)
                throw new InvalidDataException("Editor session acknowledgement correlation failed.");
            return response;
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                "The Editor session failed to confirm the request. It may still be running or closing; no command was retried.", error);
        }
    }

    internal static async Task<EditorSessionSnapshot> GetStatusAsync(Process candidate,
        CancellationToken cancellationToken = default)
    {
        var ticks = candidate.StartTime.ToUniversalTime().Ticks;
        var reply = await SendAsync(candidate.Id,
            new(Guid.NewGuid().ToString("N"), "STATUS", null, ticks, null),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!reply.Accepted || reply.Session is not { } snapshot ||
            snapshot.ProcessId != candidate.Id || snapshot.StartedUtcTicks != ticks ||
            string.IsNullOrWhiteSpace(snapshot.Root))
            throw new InvalidDataException("Candidate Editor did not prove its process/root identity.");
        return snapshot;
    }

    internal static async Task<EditorSessionReply> RequestCloseAsync(EditorSessionSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var reply = await SendAsync(snapshot.ProcessId,
            new(Guid.NewGuid().ToString("N"), "CLOSE", snapshot.Root,
                snapshot.StartedUtcTicks, snapshot.ActiveDashboard),
            timeoutMs: 90000, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (reply.Session is not { } current ||
            current.ProcessId != snapshot.ProcessId ||
            current.StartedUtcTicks != snapshot.StartedUtcTicks ||
            !string.Equals(NormalizeRoot(current.Root), NormalizeRoot(snapshot.Root), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Editor close response did not prove session identity.");
        return reply;
    }

    internal static async Task<IReadOnlyList<EditorSessionSnapshot>> DiscoverAsync(string root,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeRoot(root);
        var results = new List<EditorSessionSnapshot>();
        foreach (var candidate in Process.GetProcessesByName("PinkieSysMon.Editor"))
        {
            using (candidate)
            {
                if (candidate.Id == Environment.ProcessId || candidate.HasExited)
                    continue;
                EditorSessionSnapshot status;
                try { status = await GetStatusAsync(candidate, cancellationToken).ConfigureAwait(false); }
                catch (Exception error) when (error is IOException or TimeoutException or InvalidDataException or
                                               UnauthorizedAccessException or System.ComponentModel.Win32Exception)
                {
                    if (candidate.HasExited) continue;
                    throw new InvalidOperationException(
                        "An Editor process cannot be verified through the account-local session endpoint. " +
                        "No Editors were closed and no Settings were saved.", error);
                }
                if (string.Equals(NormalizeRoot(status.Root), normalized, StringComparison.OrdinalIgnoreCase))
                    results.Add(status);
            }
        }
        return results;
    }
}

internal sealed class EditorSessionEndpoint : IDisposable
{
    private readonly string _pipeName;
    private readonly Func<Task<EditorSessionSnapshot>> _capture;
    private readonly Func<string?, Task<EditorSessionCloseResult>> _close;
    private readonly Action<Exception>? _report;
    private readonly CancellationTokenSource _stop = new();
    private readonly ManualResetEventSlim _closeReplySent = new(initialState: true);
    private Task? _listener;
    private bool _disposed;

    internal EditorSessionEndpoint(string pipeName, Func<Task<EditorSessionSnapshot>> capture,
        Func<string?, Task<EditorSessionCloseResult>> close, Action<Exception>? report = null)
    {
        _pipeName = pipeName;
        _capture = capture;
        _close = close;
        _report = report;
    }

    internal void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_listener is not null) throw new InvalidOperationException("Editor endpoint already started.");
        _listener = Task.Run(() => ListenAsync(_stop.Token));
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(_pipeName,
                    PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var inputDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                inputDeadline.CancelAfter(TimeSpan.FromSeconds(10));
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true)
                { AutoFlush = true, NewLine = "\n" };
                var line = await reader.ReadLineAsync(inputDeadline.Token).ConfigureAwait(false);
                if (line is null || line.Length > 4096)
                    throw new InvalidDataException("Malformed Editor session request.");
                var request = JsonSerializer.Deserialize<EditorSessionRequest>(line)
                    ?? throw new InvalidDataException("Editor request is null.");
                if (!Guid.TryParseExact(request.RequestId, "N", out _))
                    throw new InvalidDataException("Editor request correlation is invalid.");

                var snapshot = await _capture().ConfigureAwait(false);
                EditorSessionReply reply;
                if (request.StartedUtcTicks != snapshot.StartedUtcTicks)
                {
                    reply = new(request.RequestId, false, "Editor process identity changed.", snapshot);
                }
                else if (request.Action == "STATUS")
                {
                    reply = new(request.RequestId, true, "Editor session verified.", snapshot);
                }
                else if (request.Action == "CLOSE" && request.Root is not null &&
                    string.Equals(EditorSessionWire.NormalizeRoot(request.Root),
                        EditorSessionWire.NormalizeRoot(snapshot.Root), StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(request.ExpectedDashboard, snapshot.ActiveDashboard, StringComparison.OrdinalIgnoreCase))
                {
                    _closeReplySent.Reset();
                    try
                    {
                        var outcome = await _close(request.ExpectedDashboard).ConfigureAwait(false);
                        reply = new(request.RequestId, outcome.Closed, outcome.Message, snapshot);
                        await writer.WriteLineAsync(JsonSerializer.Serialize(reply).AsMemory(), cancellationToken)
                            .ConfigureAwait(false);
                    }
                    finally { _closeReplySent.Set(); }
                    continue;
                }
                else
                {
                    reply = new(request.RequestId, false,
                        "Editor root, open document or requested operation changed.", snapshot);
                }
                await writer.WriteLineAsync(JsonSerializer.Serialize(reply).AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                try { _report?.Invoke(error); } catch { }
                // Failed or malicious peers must never interrupt an Editor's UI.
            }
        }
    }

    // Application.Run has ended, but a managed CLOSE acknowledgement might still
    // be awaiting the named-pipe write. Never strand the requesting Settings host.
    internal bool WaitForFinalCloseAcknowledgement(TimeSpan timeout) =>
        _closeReplySent.Wait(timeout);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        _stop.Dispose();
        _closeReplySent.Dispose();
    }
}
