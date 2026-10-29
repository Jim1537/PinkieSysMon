using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace PinkieSysMon;

// Only Runtime's accepted user gesture calls TryDispatch. No renderer or Editor owns this service.
internal sealed class ShortcutShellDispatcher : IDisposable
{
    private readonly BlockingCollection<(long Generation, string Target, string Arguments, LogOperation? Operation)> _queue = new(16);
    private readonly Action<int> _reportFailure;
    private readonly FileLogger? _log;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task Completion => _completion.Task;
    private readonly Func<string, string, int> _invoke;
    private readonly object _gate = new();
    private long _generation;
    private bool _started;
    private bool _disposed;

    public ShortcutShellDispatcher(Action<int> reportFailure, Func<string, string, int>? invoke = null, FileLogger? log = null)
    { _reportFailure = reportFailure; _invoke = invoke ?? InvokeShell; _log = log; }

    public void CancelPending() { Interlocked.Increment(ref _generation); }

    public bool TryDispatch(ShortcutInteraction shortcut)
    {
        var operation = _log is null ? null : new LogOperation(_log, "desktop.shortcut", shortcut.Id);
        if (string.IsNullOrWhiteSpace(shortcut.Target))
        { operation?.Cancel("refused.empty-target"); return false; }
        var refused = false;
        var full = false;
        lock (_gate)
        {
            if (_disposed) refused = true;
            else
            {
                if (!_started)
                {
                    var thread = new Thread(Run) { IsBackground = true, Name = "PinkieSysMon Shortcut Shell" };
                    if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
                    thread.Start(); _started = true;
                }
                full = !_queue.TryAdd((Interlocked.Read(ref _generation), shortcut.Target, shortcut.Arguments, operation));
            }
        }
        // Logging and callbacks never hold the dispatch queue gate.
        if (refused) { operation?.Cancel("refused.disposed"); return false; }
        if (full)
        {
            operation?.Fail(new System.ComponentModel.Win32Exception(1816), "refused.queue-full");
            _reportFailure(1816);
            return false;
        }
        return true;
    }

    private void Run()
    {
        var initialized = OperatingSystem.IsWindows() && CoInitializeEx(IntPtr.Zero, 2) >= 0;
        try
        {
            foreach (var request in _queue.GetConsumingEnumerable())
            {
                if (request.Generation != Interlocked.Read(ref _generation))
                { request.Operation?.Cancel("superseded-before-shell"); continue; }
                try
                {
                    request.Operation?.Phase("shell.dispatch");
                    var error = _invoke(request.Target, request.Arguments);
                    if (error != 0)
                    {
                        request.Operation?.Fail(new System.ComponentModel.Win32Exception(error), "Shell returned a native failure.");
                        _reportFailure(error);
                    }
                    else request.Operation?.Complete("shellAccepted=true; launchedApplicationSuccess=unconfirmed");
                }
                catch (Exception ex) when (ex is ExternalException or ArgumentException or InvalidOperationException)
                { request.Operation?.Fail(ex, "Shell invocation threw."); _reportFailure(ex.HResult); }
            }
        }
        finally
        {
            try { if (initialized) CoUninitialize(); _queue.Dispose(); }
            finally { _completion.TrySetResult(); }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true; CancelPending(); _queue.CompleteAdding();
            if (!_started) { _queue.Dispose(); _completion.TrySetResult(); }
        }
    }

    private static int InvokeShell(string target, string arguments)
    {
        var info = new ShellExecuteInfo
        {
            Size = Marshal.SizeOf<ShellExecuteInfo>(), Mask = 0x00000100 | 0x00004000, // NOASYNC (worker has no message loop), UNICODE
            File = target, Parameters = arguments, Show = 1
        };
        // No NO_UI flag: Windows owns association/security/error UI; do not promise it for every failure.
        return ShellExecuteEx(ref info) ? 0 : Marshal.GetLastPInvokeError();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int Size; public uint Mask; public IntPtr Window;
        public string? Verb; public string? File; public string? Parameters; public string? Directory;
        public int Show; public IntPtr Instance; public IntPtr IdList; public string? Class;
        public IntPtr ClassKey; public uint HotKey; public IntPtr IconOrMonitor; public IntPtr Process;
    }
    [DllImport("shell32.dll", EntryPoint = "ShellExecuteExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShellExecuteEx(ref ShellExecuteInfo info);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint apartment);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}

