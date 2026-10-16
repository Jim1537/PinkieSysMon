using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace PinkieSysMon;

// The wallpaper WorkerW is an undocumented Explorer implementation detail.
// Only our own HWND is changed. Unknown shell layouts fail closed.
internal static class DesktopExplorerHost
{
    internal readonly record struct Host(IntPtr Handle, uint ExplorerProcessId);
    private const int GwlStyle = -16;
    private const long WsChild = 0x40000000;
    private const long WsPopup = unchecked((long)0x80000000);
    private static bool _requestedWorker;

    internal static Host? Discover()
    {
        var progman = FindWindow("Progman", null);
        if (!IsExplorerWindow(progman, out var explorerId))
            return null;
        var worker = FindWorker(progman, explorerId);
        if (worker == IntPtr.Zero && !_requestedWorker)
        {
            // Classic Explorer may create a wallpaper WorkerW on this shell message.
            // Request at most once; never keep manipulating Explorer when unsupported.
            _requestedWorker = true;
            SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero,
                0x0002, 500, out _);
            worker = FindWorker(progman, explorerId);
        }
        return worker == IntPtr.Zero ? null : new Host(worker, explorerId);
    }

    private static IntPtr FindWorker(IntPtr progman, uint explorerId)
    {
        // Windows 11 raised desktop: WorkerW is a child of Progman.
        for (var child = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
             child != IntPtr.Zero;
             child = FindWindowEx(progman, child, "WorkerW", null))
            if (ValidWorker(child, explorerId))
                return child;

        // Classic desktop: a top-level WorkerW sits behind the icon host.
        var selected = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            if (!ValidWorker(window, explorerId))
                return true;
            selected = window;
            return false;
        }, IntPtr.Zero);
        return selected;
    }

    // The shell host may not be an icon container or belong to another process.
    // Keep this decision pure for deterministic regression tests.
    internal static bool IsWallpaperCandidate(bool explorerOwned, bool visible,
        bool workerClass, bool containsIconView) =>
        explorerOwned && visible && workerClass && !containsIconView;

    private static bool ValidWorker(IntPtr window, uint explorerId)
    {
        if (window == IntPtr.Zero || !IsWindow(window))
            return false;
        var sameExplorer = GetWindowThreadProcessId(window, out var pid) != 0 && pid == explorerId;
        return IsWallpaperCandidate(sameExplorer, IsWindowVisible(window),
            ClassName(window) == "WorkerW",
            FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero);
    }

    // Called every frame: only inexpensive native handle checks belong here.
    // Explorer ownership is validated on discovery and every five-second probe.
    internal static bool IsAttached(Host host, IntPtr panel) =>
        IsWindow(host.Handle) && IsWindowVisible(host.Handle) &&
        IsWindow(panel) && GetParent(panel) == host.Handle;

    internal static void Attach(IntPtr panel, Host host, Point physicalLocation, Size pixels)
    {
        if (!ValidWorker(host.Handle, host.ExplorerProcessId) ||
            !IsExplorerWindow(host.Handle, out var pid) || pid != host.ExplorerProcessId)
            throw new InvalidOperationException("Explorer wallpaper host is unavailable.");

        var windowContext = GetWindowDpiAwarenessContext(panel);
        var hostContext = GetWindowDpiAwarenessContext(host.Handle);
        if (windowContext == IntPtr.Zero || hostContext == IntPtr.Zero ||
            !AreDpiAwarenessContextsEqual(windowContext, hostContext))
            throw new InvalidOperationException(
                "Explorer host DPI awareness differs from Runtime; unsafe cross-process SetParent was refused.");

        var existing = GetWindowLongPtr(panel, GwlStyle);
        if (existing == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot read Desktop window style.");
        var value = new IntPtr((existing.ToInt64() & ~WsPopup) | WsChild);
        if (existing != value && SetWindowLongPtr(panel, GwlStyle, value) == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot set Desktop child-window style.");

        Marshal.SetLastPInvokeError(0);
        var oldParent = SetParent(panel, host.Handle);
        if (oldParent == IntPtr.Zero && Marshal.GetLastPInvokeError() != 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Explorer SetParent failed.");

        var point = ParentPoint(host.Handle, physicalLocation);
        if (!SetWindowPos(panel, IntPtr.Zero, point.X, point.Y, pixels.Width, pixels.Height,
                0x0004u /* SWP_NOZORDER */ | 0x0010u /* SWP_NOACTIVATE */ | 0x0020u /* SWP_FRAMECHANGED */))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot place Explorer-hosted Desktop window.");
    }

    internal static Point ParentPoint(IntPtr parent, Point physicalLocation)
    {
        var native = new NativePoint(physicalLocation.X, physicalLocation.Y);
        Marshal.SetLastPInvokeError(0);
        var mapped = MapWindowPoints(IntPtr.Zero, parent, ref native, 1);
        if (mapped == 0 && Marshal.GetLastPInvokeError() != 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Desktop coordinate conversion failed.");
        return new Point(native.X, native.Y);
    }

    internal static Point PhysicalPoint(IntPtr panel)
    {
        if (!GetWindowRect(panel, out var rect))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot read Desktop screen coordinates.");
        return new Point(rect.Left, rect.Top);
    }

    private static bool IsExplorerWindow(IntPtr window, out uint pid)
    {
        pid = 0;
        if (window == IntPtr.Zero || !IsWindow(window) ||
            GetWindowThreadProcessId(window, out pid) == 0 || pid == 0)
            return false;
        try
        {
            using var process = Process.GetProcessById(checked((int)pid));
            return process.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or OverflowException)
        {
            return false;
        }
    }

    private static string ClassName(IntPtr window)
    {
        var name = new StringBuilder(64);
        return GetClassName(window, name, name.Capacity) == 0 ? "" : name.ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y) { public int X = x; public int Y = y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }
    private delegate bool WindowEnumeration(IntPtr hwnd, IntPtr param);

    [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string className, string? windowName);
    [DllImport("user32.dll", EntryPoint = "FindWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? windowName);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(WindowEnumeration callback, IntPtr param);
    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr window, StringBuilder buffer, int maxLength);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr window, IntPtr newParent);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AreDpiAwarenessContextsEqual(IntPtr one, IntPtr other);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int MapWindowPoints(IntPtr from, IntPtr to, ref NativePoint point, uint count);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr panel, out NativeRect rect);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wParam,
        IntPtr lParam, uint flags, uint timeoutMilliseconds, out IntPtr result);
}
