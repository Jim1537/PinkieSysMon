using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PinkieSysMon;

// Listens for Windows shell/window transitions; never hooks Explorer code or
// changes any window itself. Native placement remains owned by OutputSessionManager.
internal sealed class DesktopShellZOrderWatcher : IDisposable
{
    internal const uint EventSystemForeground = 0x0003;
    internal const uint EventObjectReorder = 0x8004;
    internal const int ObjIdWindow = 0;
    internal const int ObjIdClient = -4;
    private const uint OutOfContextSkipOwnProcess = 0x0000u | 0x0002u;
    private readonly FileLogger _log;
    private readonly Action _reconcile;
    private readonly WinEventProc _callback; // Root delegate for native hook lifetime.
    private IntPtr _foregroundHook;
    private IntPtr _reorderHook;
    private bool _handling;
    private bool _disposed;

    internal static bool IsRelevantEvent(uint eventType, int objectId, int childId) =>
        eventType == EventSystemForeground ||
        (eventType == EventObjectReorder && childId == 0 &&
         (objectId == ObjIdWindow || objectId == ObjIdClient));

    internal DesktopShellZOrderWatcher(Action reconcile, FileLogger log)
    {
        _reconcile = reconcile ?? throw new ArgumentNullException(nameof(reconcile));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _callback = OnEvent;

        // OUTOFCONTEXT delivers events on this WinForms UI thread's message loop.
        // SKIPOWNPROCESS prevents our SetWindowPos calls feeding back into the hook.
        _foregroundHook = Register(EventSystemForeground);
        _reorderHook = Register(EventObjectReorder);
    }

    private IntPtr Register(uint eventType)
    {
        var hook = SetWinEventHook(eventType, eventType, IntPtr.Zero, _callback,
            0, 0, OutOfContextSkipOwnProcess);
        if (hook == IntPtr.Zero)
            _log.Warn($"Desktop shell WinEvent hook 0x{eventType:X} unavailable; automatic Z-order recovery may be limited.",
                new Win32Exception(Marshal.GetLastPInvokeError()));
        return hook;
    }

    private void OnEvent(IntPtr hook, uint eventType, IntPtr hwnd, int objectId,
        int childId, uint eventThread, uint eventTime)
    {
        // On the owner's Windows 11 build, Win+D promotes Progman *after* the
        // foreground notification and signals the completed change with
        // EVENT_OBJECT_REORDER/OBJID_CLIENT. Check current native stack rather
        // than relying on the earlier foreground notification alone.
        if (_disposed || _handling || !IsRelevantEvent(eventType, objectId, childId))
            return;
        _handling = true;
        try { _reconcile(); }
        catch (Exception ex) { _log.Warn("Desktop shell Z-order event handling failed.", ex); }
        finally { _handling = false; }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_foregroundHook != IntPtr.Zero)
            UnhookWinEvent(_foregroundHook);
        if (_reorderHook != IntPtr.Zero)
            UnhookWinEvent(_reorderHook);
        _foregroundHook = IntPtr.Zero;
        _reorderHook = IntPtr.Zero;
        GC.KeepAlive(_callback);
    }

    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd,
        int objectId, int childId, uint eventThread, uint eventTime);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax,
        IntPtr module, WinEventProc callback, uint process, uint thread, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);
}
