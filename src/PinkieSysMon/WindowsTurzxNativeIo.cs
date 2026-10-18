using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PinkieSysMon;

// Sole owner of TURZX native handles. Only TurzxUsbConnection calls Submit,
// Complete, Cancel and Dispose, with one outstanding operation at most.
internal sealed class WindowsTurzxNativeIo : ITurzxNativeIo
{
    private SafeFileHandle? _file;
    private IntPtr _usb;
    private WindowsTurzxNativeIo() { }

    public static WindowsTurzxNativeIo Open(string path, int timeoutMs)
    {
        TurzxUsbConnection.ValidateTimeout(timeoutMs);
        var io = new WindowsTurzxNativeIo();
        try
        {
            io._file = CreateFileW(path, 0xC0000000, 3, IntPtr.Zero, 3, 0x40000080, IntPtr.Zero);
            if (io._file.IsInvalid) throw Error("open");
            if (!WinUsb_Initialize(io._file, out io._usb)) throw Error("initialize");
            if (!WinUsb_GetCurrentAlternateSetting(io._usb, out var currentSetting)) throw Error("alternate setting");
            if (currentSetting != 0) throw new IOException("TURZX current alternate setting is unsupported.");
            if (!WinUsb_QueryInterfaceSettings(io._usb, 0, out var descriptor)) throw Error("interface descriptor");
            if (descriptor.Length != 9 || descriptor.DescriptorType != 4 || descriptor.Number != 0 ||
                descriptor.AlternateSetting != 0 || descriptor.EndpointCount is < 2 or > 32)
                throw new IOException("TURZX interface layout is unsupported.");
            var pipes = new List<TurzxNativePipe>();
            for (byte i = 0; i < descriptor.EndpointCount; i++)
            {
                if (!WinUsb_QueryPipe(io._usb, 0, i, out var pipe)) throw Error("pipe descriptor");
                pipes.Add(pipe);
            }
            ValidatePipes(pipes);
            var timeout = (uint)timeoutMs;
            foreach (var pipe in new[] { TurzxUsbConnection.PipeOut, TurzxUsbConnection.PipeIn })
                if (!WinUsb_SetPipePolicy(io._usb, pipe, 3, sizeof(uint), ref timeout)) throw Error("pipe timeout");
            return io;
        }
        catch { io.Dispose(); throw; }
    }

    internal static void ValidatePipes(IReadOnlyList<TurzxNativePipe> pipes)
    {
        foreach (var endpoint in new[] { TurzxUsbConnection.PipeOut, TurzxUsbConnection.PipeIn })
        {
            var matches = pipes.Where(p => p.Id == endpoint).ToArray();
            if (matches.Length != 1 || matches[0].Type != 2 || matches[0].MaximumPacketSize == 0)
                throw new IOException("TURZX requires one bulk OUT 01 and one bulk IN 81 endpoint.");
        }
    }

    public bool Submit(bool read, byte pipe, IntPtr buffer, uint length, IntPtr overlapped, out int error)
    {
        var result = read ? WinUsb_ReadPipe(_usb, pipe, buffer, length, out _, overlapped)
            : WinUsb_WritePipe(_usb, pipe, buffer, length, out _, overlapped);
        error = result ? 0 : Marshal.GetLastWin32Error();
        return result;
    }

    public bool Complete(IntPtr overlapped, out uint transferred, out int error)
    {
        var result = WinUsb_GetOverlappedResult(_usb, overlapped, out transferred, true);
        error = result ? 0 : Marshal.GetLastWin32Error();
        return result;
    }

    public void Cancel(IntPtr overlapped)
    {
        if (CancelIoEx(_file!, overlapped)) return;
        var error = Marshal.GetLastWin32Error();
        if (error != 1168) throw new Win32Exception(error, "TURZX cancellation request failed.");
    }

    public void Dispose()
    {
        if (_usb != IntPtr.Zero) { WinUsb_Free(_usb); _usb = IntPtr.Zero; }
        _file?.Dispose();
        _file = null;
    }

    private static Win32Exception Error(string operation) => new(Marshal.GetLastWin32Error(), $"TURZX USB {operation} failed.");

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct InterfaceDescriptor
    {
        public byte Length, DescriptorType, Number, AlternateSetting, EndpointCount, Class, SubClass, Protocol, StringIndex;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);
    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_Initialize(SafeFileHandle file, out IntPtr handle);
    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_Free(IntPtr handle);
    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_GetCurrentAlternateSetting(IntPtr handle, out byte setting);
    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_QueryInterfaceSettings(IntPtr handle, byte alternateIndex, out InterfaceDescriptor descriptor);
    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_QueryPipe(IntPtr handle, byte alternateSetting, byte index, out TurzxNativePipe pipe);
    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_SetPipePolicy(IntPtr handle, byte pipe, uint policy, uint length, ref uint value);
    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_ReadPipe(IntPtr handle, byte pipe, IntPtr buffer, uint length, out uint transferred, IntPtr overlapped);
    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_WritePipe(IntPtr handle, byte pipe, IntPtr buffer, uint length, out uint transferred, IntPtr overlapped);
    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_GetOverlappedResult(IntPtr handle, IntPtr overlapped, out uint transferred,
        [MarshalAs(UnmanagedType.Bool)] bool wait);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CancelIoEx(SafeFileHandle file, IntPtr overlapped);
}

[StructLayout(LayoutKind.Sequential)]
internal struct TurzxNativePipe
{
    public int Type;
    public byte Id;
    public ushort MaximumPacketSize;
    public byte Interval;
}
