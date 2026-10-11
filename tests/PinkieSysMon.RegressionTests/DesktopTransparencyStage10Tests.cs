using System.Drawing;
using System.Runtime.InteropServices;
using PinkieSysMon;
using SkiaSharp;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class DesktopTransparencyStage10Tests
{
    public static void PremultipliedTransfer()
    {
        using var image = new SKBitmap(
            new SKImageInfo(3, 2, SKColorType.Bgra8888, SKAlphaType.Premul));
        image.Erase(SKColors.Transparent);
        image.SetPixel(1, 0, new SKColor(255, 0, 0, 128));
        image.SetPixel(2, 0, new SKColor(0, 255, 0, 255));
        image.SetPixel(0, 1, new SKColor(0, 0, 255, 64));

        const int destinationStride = 16; // 12 pixels bytes + 4 sentinel padding
        var length = destinationStride * image.Height;
        var bytes = Enumerable.Repeat((byte)0xA7, length).ToArray();
        var destination = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.Copy(bytes, 0, destination, length);
            DesktopLayeredSurface.CopyPremultipliedPixels(image, destination, destinationStride);
            Marshal.Copy(destination, bytes, 0, length);

            for (var row = 0; row < image.Height; row++)
            {
                var fromSkia = new byte[image.Width * 4];
                Marshal.Copy(IntPtr.Add(image.GetPixels(), row * image.RowBytes),
                    fromSkia, 0, fromSkia.Length);
                for (var i = 0; i < fromSkia.Length; i++)
                    Check(bytes[row * destinationStride + i] == fromSkia[i],
                        "Layered DIB transfer must preserve every raw premultiplied BGRA byte.");
                Check(bytes[row * destinationStride + 12] == 0xA7 &&
                      bytes[row * destinationStride + 15] == 0xA7,
                    "Layered DIB transfer must not overwrite destination stride padding.");
            }

            Check(bytes[3] == 0, "Fully transparent pixels must remain zero alpha.");
            var half = 4;
            Check(bytes[half] == 0 && bytes[half + 1] == 0 &&
                  bytes[half + 2] is >= 126 and <= 129 && bytes[half + 3] == 128,
                "Translucent red must remain premultiplied BGRA, not straight RGBA.");
            Check(bytes[8] == 0 && bytes[9] == 255 && bytes[10] == 0 && bytes[11] == 255,
                "Opaque green must preserve channel order.");

            var overWhite = Blend(bytes[half + 2], bytes[half + 3], 255);
            var overBlack = Blend(bytes[half + 2], bytes[half + 3], 0);
            Check(overWhite >= 253 && overBlack is >= 126 and <= 129,
                "Premultiplied source-over must blend correctly over both bright and dark windows.");
        }
        finally
        {
            Marshal.FreeHGlobal(destination);
        }

        using var wrongFormat = new SKBitmap(new SKImageInfo(3, 2,
            SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var buffer = Marshal.AllocHGlobal(32);
        try
        {
            var rejected = false;
            try { DesktopLayeredSurface.CopyPremultipliedPixels(wrongFormat, buffer, 16); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Layered presentation must reject unpremultiplied/RGBA buffers.");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static void SessionCanvasAlpha()
    {
        var directory = Path.Combine(Path.GetTempPath(),
            "pinkiesysmon-desktop10-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            var log = new FileLogger(Path.Combine(directory, "stage10.log"));
            var config = new AppConfig
            {
                Outputs = new OutputConfig
                {
                    Targets =
                    [
                        new OutputTargetConfig
                        {
                            Id = "alpha-desktop", Name = "Alpha Desktop",
                            Kind = OutputTargetContract.DesktopKind,
                            Transport = OutputTargetContract.DesktopTransport,
                            Dashboard = "Default",
                            Desktop = new DesktopOutputConfig { BoardEnabled = true }
                        }
                    ]
                }
            };
            var target = config.Outputs.Targets.Single();
            var metrics = new MetricStore();
            FakeWindow? window = null;
            using var session = new DesktopOutputSession(config, target,
                Board(directory, "#00000000"), metrics, log,
                render => window = new FakeWindow(render));
            session.Start();
            var native = window ?? throw new InvalidOperationException("Fake Desktop was not created.");
            Check(native.IsVisible, "Stage10 Desktop session must be visible.");

            var transparent = native.Draw();
            Check(transparent.Alpha == 0 && transparent.Red == 0,
                "Transparent Canvas must reach Desktop presentation without a black opaque backing.");

            using (var next = session.PrepareReconfiguration(
                       config, target, Board(directory, "#80FF0000")))
                session.CommitReconfiguration(next);
            var translucent = native.Draw();
            Check(translucent.Alpha is >= 127 and <= 129 &&
                  translucent.Red is >= 250 and <= 255 &&
                  translucent.Green < 10,
                "Translucent Canvas must retain intended straight visual color and alpha.");

            using (var next = session.PrepareReconfiguration(
                       config, target, Board(directory, "#FF224466")))
                session.CommitReconfiguration(next);
            var opaque = native.Draw();
            Check(opaque.Alpha == 255 &&
                  opaque.Red == 0x22 && opaque.Green == 0x44 && opaque.Blue == 0x66,
                "Fully opaque Canvas must remain opaque and preserve RGB values.");

            session.Suspend();
            Check(!native.IsVisible, "Suspending an alpha Desktop must hide the native window.");
            session.Resume();
            Check(native.IsVisible && native.Draw().Alpha == 255,
                "Resuming an alpha Desktop must present the same canonical dashboard.");
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    // Source-over of premultiplied color channel over opaque destination.
    private static int Blend(byte sourcePremul, byte alpha, byte background) =>
        Math.Min(255, sourcePremul + (background * (255 - alpha) + 127) / 255);

    private static C.DashboardDefinition Board(string root, string background) => new()
    {
        BaseDirectory = root,
        Canvas = new C.CanvasDefinition
        {
            Width = 32, Height = 16, BackgroundColor = background
        },
        Widgets = []
    };

    private sealed class FakeWindow(Action render) : IDesktopOutputWindow
    {
        private readonly Action _render = render;
        private SKColor _center;
        public bool IsVisible { get; private set; }

        public void Configure(string title, Size pixels, Point location, int refreshMs)
        {
            _ = title;
            _ = pixels;
            _ = location;
            _ = refreshMs;
        }
        public void ShowWindow() => IsVisible = true;
        public void HideWindow() => IsVisible = false;
        public void RequestFrame() { }
        public void Present(SKBitmap bitmap) => _center = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
        public SKColor Draw()
        {
            if (!IsVisible)
                throw new InvalidOperationException("Cannot render into a hidden window.");
            _render();
            return _center;
        }
        public void Dispose() => IsVisible = false;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
