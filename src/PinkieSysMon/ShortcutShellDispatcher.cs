using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace PinkieSysMon;

// Only Runtime's accepted user gesture calls TryDispatch. No renderer or Editor owns this service.
internal sealed class ShortcutShellDispatcher : IDisposable
{
    private readonly BlockingCollection<(long Generation, string Target, string Arguments)> _queue = new(16);
    private readonly Action<int> _reportFailure;
    private readonly Func<string, string, int> _invoke;
    private readonly object _gate = new();
    private long _generation;
    private bool _started;
    private bool _disposed;

    public ShortcutShellDispatcher(Action<int> reportFailure, Func<string, string, int>? invoke = null)
    { _reportFailure = reportFailure; _invoke = invoke ?? InvokeShell; }

    public void CancelPending() { Interlocked.Increment(ref _generation); }

    public bool TryDispatch(ShortcutInteraction shortcut)
    {
        if (string.IsNullOrWhiteSpace(shortcut.Target)) return false;
        lock (_gate)
        {
            if (_disposed) return false;
            if (!_started)
            {
                var thread = new Thread(Run) { IsBackground = true, Name = "PinkieSysMon Shortcut Shell" };
                if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
                thread.Start(); _started = true;
            }
            if (_queue.TryAdd((Interlocked.Read(ref _generation), shortcut.Target, shortcut.Arguments))) return true;
            _reportFailure(1816); // ERROR_NOT_ENOUGH_QUOTA; no private payload in instrumentation.
            return false;
        }
    }

    private void Run()
    {
        var initialized = OperatingSystem.IsWindows() && CoInitializeEx(IntPtr.Zero, 2) >= 0;
        try
        {
            foreach (var request in _queue.GetConsumingEnumerable())
            {
                if (request.Generation != Interlocked.Read(ref _generation)) continue;
                try
                {
                    var error = _invoke(request.Target, request.Arguments);
                    if (error != 0) _reportFailure(error);
                }
                catch (Exception ex) when (ex is ExternalException or ArgumentException or InvalidOperationException)
                { _reportFailure(ex.HResult); }
            }
        }
        finally
        {
            if (initialized) CoUninitialize();
            _queue.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true; CancelPending(); _queue.CompleteAdding();
            if (!_started) _queue.Dispose();
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
