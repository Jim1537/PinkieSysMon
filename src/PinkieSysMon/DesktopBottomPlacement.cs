using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PinkieSysMon;

// Standalone Desktop panels are placed in the normal top-level window stack.
// Literal HWND_BOTTOM can hide a panel below Progman's wallpaper. When the
// desktop shell is present, insert only our panel directly above that window.
internal static class DesktopBottomPlacement
{
    internal static readonly IntPtr HwndBottom = new(1);
    internal const int WsExToolWindow = 0x00000080;
    internal const int WsExAppWindow = 0x00040000;
    internal const uint Flags =
        0x0001u /* SWP_NOSIZE */ |
        0x0002u /* SWP_NOMOVE */ |
        0x0010u /* SWP_NOACTIVATE */ |
        0x0200u /* SWP_NOOWNERZORDER */;

    internal static IntPtr InsertAfter(IntPtr precedingShellWindow) =>
        precedingShellWindow == IntPtr.Zero ? HwndBottom : precedingShellWindow;

    internal static void PlaceAtDesktopBack(IntPtr panel)
    {
        var shell = FindWindow("Progman", null);
        var preceding = shell == IntPtr.Zero ? IntPtr.Zero :
            GetWindow(shell, 3u /* GW_HWNDPREV */);
        if (preceding == panel)
            return;
        if (!SetWindowPos(panel, InsertAfter(preceding), 0, 0, 0, 0, Flags))
            throw new Win32Exception(Marshal.GetLastPInvokeError(),
                "Could not place Desktop panel at the back of normal windows.");
    }

    [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? windowName);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter,
        int x, int y, int width, int height, uint flags);
}
