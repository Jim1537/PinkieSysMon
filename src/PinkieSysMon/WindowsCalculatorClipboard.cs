using System.Runtime.InteropServices;
using System.Text;

namespace PinkieSysMon;

// Explicit-click-only Win32 clipboard bridge. One attempt; never wait/retry on a busy owner.
internal static class WindowsCalculatorClipboard
{
    private const uint UnicodeText = 13;
    private const uint Movable = 0x0002;

    internal static string? ReadText()
    {
        if (!OpenClipboard(IntPtr.Zero)) return null;
        try
        {
            if (!IsClipboardFormatAvailable(UnicodeText)) return null;
            var handle = GetClipboardData(UnicodeText);
            if (handle == IntPtr.Zero) return null;
            var bytes = GlobalSize(handle).ToUInt64();
            if (bytes < 2 || bytes > 8192 || (bytes & 1) != 0) return null;
            var pointer = GlobalLock(handle);
            if (pointer == IntPtr.Zero) return null;
            try
            {
                var chars = checked((int)(bytes / 2));
                var text = Marshal.PtrToStringUni(pointer, chars);
                var end = text?.IndexOf('\0') ?? -1;
                return end >= 0 ? text![..end] : null;
            }
            finally { GlobalUnlock(handle); }
        }
        finally { CloseClipboard(); }
    }

    internal static void WriteText(IntPtr owner, string value)
    {
        if (owner == IntPtr.Zero || !OpenClipboard(owner))
            throw new InvalidOperationException("Clipboard is unavailable.");
        IntPtr memory = IntPtr.Zero;
        try
        {
            var bytes = Encoding.Unicode.GetBytes(value + "\0");
            memory = GlobalAlloc(Movable, (UIntPtr)bytes.Length);
            if (memory == IntPtr.Zero)
                throw new InvalidOperationException("Clipboard allocation failed.");
            var pointer = GlobalLock(memory);
            if (pointer == IntPtr.Zero)
                throw new InvalidOperationException("Clipboard memory is unavailable.");
            try { Marshal.Copy(bytes, 0, pointer, bytes.Length); }
            finally { GlobalUnlock(memory); }
            if (!EmptyClipboard() || SetClipboardData(UnicodeText, memory) == IntPtr.Zero)
                throw new InvalidOperationException("Clipboard transfer failed.");
            memory = IntPtr.Zero; // Clipboard takes ownership only after SetClipboardData succeeds.
        }
        finally
        {
            if (memory != IntPtr.Zero) GlobalFree(memory);
            CloseClipboard();
        }
    }

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint format, IntPtr data);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalUnlock(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr memory);
}
