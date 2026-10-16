using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace PinkieSysMon;

// One reusable, top-down 32-bpp DIB is selected into a memory DC. The Desktop
// renderer supplies premultiplied BGRA; UpdateLayeredWindow consumes those exact
// pixels with source alpha, without GDI+ compositing or JPEG encoding.
internal sealed class DesktopLayeredSurface : IDisposable
{
    private const uint UlwAlpha = 0x00000002;
    private const byte AcSrcOver = 0;
    private const byte AcSrcAlpha = 1;
    private readonly IntPtr _memoryDc;
    private readonly IntPtr _dib;
    private readonly IntPtr _previousBitmap;
    private readonly IntPtr _bits;
    private bool _disposed;

    public int Width { get; }
    public int Height { get; }
    private int Stride => checked(Width * 4);

    public DesktopLayeredSurface(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Desktop dimensions must be positive.");

        Width = width;
        Height = height;

        IntPtr memoryDc = IntPtr.Zero;
        IntPtr dib = IntPtr.Zero;
        IntPtr previousBitmap = IntPtr.Zero;
        try
        {
            memoryDc = CreateCompatibleDC(IntPtr.Zero);
            if (memoryDc == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateCompatibleDC failed.");

            var header = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = width,
                Height = -height, // top-down, matching Skia's BGRA row order
                Planes = 1,
                BitCount = 32,
                Compression = 0 // BI_RGB
            };
            dib = CreateDIBSection(memoryDc, ref header, 0, out var bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || bits == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateDIBSection failed.");

            previousBitmap = SelectObject(memoryDc, dib);
            if (previousBitmap == IntPtr.Zero || previousBitmap == new IntPtr(-1))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SelectObject failed.");

            _memoryDc = memoryDc;
            _dib = dib;
            _previousBitmap = previousBitmap;
            _bits = bits;
        }
        catch
        {
            if (previousBitmap != IntPtr.Zero && previousBitmap != new IntPtr(-1))
                SelectObject(memoryDc, previousBitmap);
            if (dib != IntPtr.Zero)
                DeleteObject(dib);
            if (memoryDc != IntPtr.Zero)
                DeleteDC(memoryDc);
            throw;
        }
    }

    public void Present(IntPtr windowHandle, SKBitmap bitmap)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (bitmap.Width != Width || bitmap.Height != Height)
            throw new InvalidOperationException("Desktop BGRA frame and native window dimensions disagree.");

        CopyPremultipliedPixels(bitmap, _bits, Stride);

        var origin = new NativePoint(0, 0);
        var size = new NativeSize(Width, Height);
        var blend = new BlendFunction
        {
            BlendOp = AcSrcOver,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = AcSrcAlpha
        };

        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetDC failed.");
        try
        {
            // Image refresh must not reposition a native child of Explorer WorkerW.
            // Win32 already owns placement through SetWindowPos in parent-client coordinates.
            if (!UpdateLayeredWindow(windowHandle, screenDc, IntPtr.Zero, ref size,
                    _memoryDc, ref origin, 0, ref blend, UlwAlpha))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "UpdateLayeredWindow failed.");
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    // Also tested independently of the native Win32 window: no alpha flattening,
    // channel swap, unpremultiplication or trailing-row corruption is permitted.
    internal static unsafe void CopyPremultipliedPixels(SKBitmap bitmap, IntPtr destination, int destinationStride)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        if (bitmap.ColorType != SKColorType.Bgra8888 || bitmap.AlphaType != SKAlphaType.Premul)
            throw new InvalidOperationException("Layered window requires premultiplied BGRA8888.");
        var rowLength = checked(bitmap.Width * 4);
        if (bitmap.Width <= 0 || bitmap.Height <= 0 || bitmap.RowBytes < rowLength ||
            destination == IntPtr.Zero || destinationStride < rowLength)
            throw new ArgumentException("Invalid BGRA source or destination stride.");

        var source = (byte*)bitmap.GetPixels();
        if (source == null)
            throw new ObjectDisposedException(nameof(bitmap));
        var dest = (byte*)destination;
        for (var y = 0; y < bitmap.Height; y++)
            Buffer.MemoryCopy(source + y * bitmap.RowBytes,
                dest + y * destinationStride, destinationStride, rowLength);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        SelectObject(_memoryDc, _previousBitmap);
        DeleteObject(_dib);
        DeleteDC(_memoryDc);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize(int width, int height)
    {
        public int Width = width;
        public int Height = height;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfoHeader header,
        uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr gdiObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr gdiObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr dc);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr destinationDc,
        IntPtr destination, ref NativeSize size, IntPtr sourceDc,
        ref NativePoint source, int colorKey, ref BlendFunction blend, uint flags);
}
