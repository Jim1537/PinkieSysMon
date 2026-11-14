using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace PinkieSysMon.Launcher;

internal enum LauncherTab
{
    Application,
    Common,
    Runtime,
    Editor,
    Providers,
    About
}

internal sealed record LauncherRoute(LauncherTab? Tab, string? DirectAction)
{
    internal bool IsDirectAction => DirectAction is not null;
}

internal static class LauncherCommandLine
{
    internal static LauncherRoute Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length == 0)
            return new(LauncherTab.Application, null);
        if (args.Length != 1)
            throw new ArgumentException("Expected one Launcher command: --tab=<name>, --editor, --runtime or --both.");

        if (args[0] is "--editor" or "--runtime" or "--both")
            return new(null, args[0]);

        if (!args[0].StartsWith("--tab=", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Unknown Launcher command. Use --tab=<name> or a direct launch flag.");

        var tab = args[0][6..];
        if (tab.Length == 0 || !Enum.TryParse<LauncherTab>(tab, ignoreCase: true, out var parsed) ||
            !Enum.GetNames<LauncherTab>().Any(name => name.Equals(tab, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Unknown Launcher tab. Expected application, common, runtime, editor, providers or about.");

        return new(parsed, null);
    }

    internal static string TabArgument(LauncherTab tab) => "--tab=" + tab.ToString().ToLowerInvariant();
}

// Exactly one interactive owner for the current Windows account and normalized
// installation root. Direct --editor/--runtime/--both never acquire this lease.
internal sealed class LauncherUiLease : IDisposable
{
    private const string MutexPrefix = @"Local\PinkieSysMon.Launcher.UI.v1.";
    private const string PipePrefix = "PinkieSysMon.Launcher.Activate.v1.";
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stop = new();
    private Task? _listener;
    private bool _disposed;

    internal string PipeName { get; }

    private LauncherUiLease(Mutex mutex, string pipeName)
    {
        _mutex = mutex;
        PipeName = pipeName;
    }

    internal static (string MutexName, string PipeName) NamesFor(string root, string sid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(sid);
        var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        var identity = sid + "." + hash;
        return (MutexPrefix + identity, PipePrefix + identity);
    }

    internal static (string MutexName, string PipeName) CurrentNames(string root)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Launcher ownership requires Windows.");
        using var identity = WindowsIdentity.GetCurrent();
        return NamesFor(root, identity.User?.Value ??
            throw new InvalidOperationException("Current Windows account identity is unavailable."));
    }

    internal static LauncherUiLease? TryAcquire(string root)
    {
        var names = CurrentNames(root);
        return TryAcquire(names.MutexName, names.PipeName);
    }

    // Injectable identities make thread-contention tests independent of live installations.
    internal static LauncherUiLease? TryAcquire(string mutexName, string pipeName)
    {
        var mutex = new Mutex(false, mutexName);
        try
        {
            bool acquired;
            try { acquired = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            if (acquired)
                return new LauncherUiLease(mutex, pipeName);
            mutex.Dispose();
            return null;
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    internal void Listen(Action<LauncherTab> selectTab, Action<Exception>? report = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_listener is not null)
            throw new InvalidOperationException("Launcher activation listener has already started.");
        _listener = Task.Run(() => ListenLoopAsync(selectTab, report, _stop.Token));
    }

    private async Task ListenLoopAsync(Action<LauncherTab> selectTab, Action<Exception>? report,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                using var reader = new StreamReader(server, Encoding.UTF8, false, 1024, leaveOpen: true);
                using var writer = new StreamWriter(server, new UTF8Encoding(false), 1024, leaveOpen: true)
                { AutoFlush = true, NewLine = "\n" };
                var line = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
                if (line is null || line.Length > 512)
                    throw new InvalidDataException("Invalid Launcher activation payload.");
                var message = JsonSerializer.Deserialize<ActivationMessage>(line)
                    ?? throw new InvalidDataException("Missing Launcher activation payload.");
                if (!Guid.TryParseExact(message.RequestId, "N", out _) ||
                    LauncherCommandLine.Parse([message.Tab]).Tab is not { } tab)
                    throw new InvalidDataException("Invalid Launcher activation request.");
                // Acknowledgement is issued only after the owning window completed
                // the tab request. The callback must marshal to its UI thread.
                selectTab(tab);
                await writer.WriteLineAsync(JsonSerializer.Serialize(
                    new ActivationReply(message.RequestId, true)).AsMemory(), timeout.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                try { report?.Invoke(error); } catch { }
                // A bad client cannot terminate the owner's listener.
            }
        }
    }

    internal static async Task ActivateAsync(string pipeName, LauncherTab tab, int timeoutMs = 3000,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMs);
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous);
        try
        {
            await client.ConnectAsync(timeout.Token).ConfigureAwait(false);
            using var reader = new StreamReader(client, Encoding.UTF8, false, 1024, leaveOpen: true);
            using var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, leaveOpen: true)
            { AutoFlush = true, NewLine = "\n" };
            var id = Guid.NewGuid().ToString("N");
            await writer.WriteLineAsync(JsonSerializer.Serialize(
                new ActivationMessage(id, LauncherCommandLine.TabArgument(tab))).AsMemory(), timeout.Token)
                .ConfigureAwait(false);
            var line = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
            if (line is null || line.Length > 512)
                throw new InvalidDataException("Launcher activation acknowledgement was missing.");
            var reply = JsonSerializer.Deserialize<ActivationReply>(line);
            if (reply is not { Accepted: true } || reply.RequestId != id)
                throw new InvalidDataException("Launcher activation acknowledgement did not match the request.");
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                "An existing Launcher owns this installation but did not confirm tab activation. No second window was opened.", error);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new InvalidOperationException(
                "An existing Launcher could not confirm the requested tab. No second settings draft was opened.", error);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        // Mutex ownership is thread-affine: the UI thread releases this lease.
        _mutex.ReleaseMutex();
        _mutex.Dispose();
        _stop.Dispose();
    }

    private sealed record ActivationMessage(string RequestId, string Tab);
    private sealed record ActivationReply(string RequestId, bool Accepted);
}
