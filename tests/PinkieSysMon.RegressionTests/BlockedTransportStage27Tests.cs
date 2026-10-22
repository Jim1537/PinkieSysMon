using System.Drawing;
using PinkieSysMon;
using SkiaSharp;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

// Exercises the real physical worker, Desktop renderer and shared telemetry scheduler
// against a blocked fake USB sender. No native HWND or physical USB I/O is attempted.
internal static class BlockedTransportStage27Tests
{
    private const string Metric = "test.stage27.available";
    private const string Provider = "test.stage27";

    public static void DesktopAndTelemetryContinueDuringBlockedPhysicalSend()
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkiesysmon-stage27-" + Guid.NewGuid().ToString("N"));
        var boardDirectory = Path.Combine(root, "dashboards");
        Directory.CreateDirectory(boardDirectory);
        Directory.CreateDirectory(Path.Combine(root, "config"));
        File.WriteAllText(Path.Combine(root, "config", "app.json"), "{}");

        using var sendEntered = new ManualResetEventSlim();
        using var releaseSend = new ManualResetEventSlim();
        var transport = new BlockingTransport(sendEntered, releaseSend);

        try
        {
            var log = new FileLogger(Path.Combine(root, "stage27.log"));
            var metrics = new MetricStore();
            var config = new AppConfig
            {
                Outputs = new OutputConfig
                {
                    Targets =
                    [
                        new OutputTargetConfig
                        { Width = 1920, Height = 480, DeviceId = "usb-interface:23e0da38aeb26d7245ad443bd1d49c63e272c642e848bd058a7297d41ee780df",
                            Id = "trofeo-1", Name = "Blocked physical",
                            Kind = OutputTargetContract.PhysicalKind,
                            Transport = OutputTargetContract.LyBulkTransport,
                            Dashboard = "Physical", DeviceEnabled = true,
                            ProviderEnabled = true, RefreshIntervalMs = 50
                        },
                        new OutputTargetConfig
                        {
                            Id = "desktop-1", Name = "Independent desktop",
                            Kind = OutputTargetContract.DesktopKind,
                            Transport = OutputTargetContract.DesktopTransport,
                            Dashboard = "Desktop", ProviderEnabled = true,
                            RefreshIntervalMs = 120,
                            Desktop = new DesktopOutputConfig { BoardEnabled = true }
                        }
                    ]
                }
            };

            C.DashboardDefinition Board(string color) => new()
            {
                BaseDirectory = boardDirectory,
                Canvas = new C.CanvasDefinition
                {
                    Width = 64, Height = 32, BackgroundColor = color
                },
                Widgets = []
            };

            var boards = new Dictionary<string, C.DashboardDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["trofeo-1"] = Board("#FF225588"),
                ["desktop-1"] = Board("#FF449922")
            };

            FakeWindow? panel = null;
            using var manager = new OutputSessionManager(config, boards, metrics, log,
                (app, target, board, store, logger) =>
                    target.Kind == OutputTargetContract.PhysicalKind
                        ? new FramePump(app, target, board, store, logger,
                            (device, timeout, journal, transportLog, token) => transport)
                        : new DesktopOutputSession(app, target, board, store, logger,
                            draw => panel = new FakeWindow(draw)));

            try
            {
                manager.Start();
                Check(sendEntered.Wait(TimeSpan.FromSeconds(5)),
                    "Physical worker must reach the injected blocking send.");

                var desktop = panel ?? throw new InvalidOperationException("Desktop window was not created.");
                Check(desktop.IsVisible && transport.SendCount == 1 &&
                      manager.GetStatuses().Single(s => s.TargetId == "trofeo-1").SentFrames == 0,
                    "Physical transfer must remain incomplete while its sibling Desktop is visible.");

                // Both use the same MetricStore, but neither waits for physical USB completion.
                var source = new CountingSource();
                var telemetry = TelemetryConfig.CreateDefaults([source]);
                using var engine = new TelemetryEngine(metrics, [source], telemetry,
                    [Provider], [Metric], log);
                engine.Start();

                Check(SpinWait.SpinUntil(() =>
                        source.Captures >= 2 &&
                        metrics.Snapshot().TryGetValue(Metric, out var value) && value is not null,
                        TimeSpan.FromSeconds(5)),
                    "Shared telemetry scheduling must continue while a physical send is blocked.");

                var draw = Task.Run(desktop.Tick);
                Check(draw.Wait(TimeSpan.FromSeconds(5)),
                    "Desktop frame rendering must not wait for the blocked physical send.");
                draw.GetAwaiter().GetResult();

                var statuses = manager.GetStatuses();
                Check(desktop.PresentCount == 1 &&
                      statuses.Single(s => s.TargetId == "desktop-1").SentFrames == 1 &&
                      statuses.Single(s => s.TargetId == "trofeo-1").SentFrames == 0 &&
                      transport.SendCount == 1,
                    "Desktop must present independently without completing the blocked physical frame.");

                // Do not release the fake USB sender until the independent work is verified.
                Check(!releaseSend.IsSet, "The physical send must still be blocked at the assertion point.");
            }
            finally
            {
                // Release fake USB I/O before OutputSessionManager disposes FramePump.
                // FramePump.Dispose legitimately waits for an in-flight synchronous send.
                releaseSend.Set();
            }
        }
        finally
        {
            releaseSend.Set();
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class CountingSource : IMetricSource
    {
        private int _captures;
        public int Captures => Volatile.Read(ref _captures);
        public string ProviderId => Provider;
        public string Name => "Stage27 shared source";
        public int DefaultIntervalMs => 100;
        public IReadOnlyCollection<string> MetricNames { get; } = [Metric];
        public IReadOnlyDictionary<string, object?> Capture() => Capture(MetricNames);
        public IReadOnlyDictionary<string, object?> Capture(IReadOnlyCollection<string> requestedMetrics)
        {
            Interlocked.Increment(ref _captures);
            return requestedMetrics.ToDictionary(x => x, x => (object?)42d,
                StringComparer.OrdinalIgnoreCase);
        }
    }

    private sealed class FakeWindow(Action render) : IDesktopOutputWindow
    {
        private int _presentCount;
        public bool IsVisible { get; private set; }
        public int PresentCount => Volatile.Read(ref _presentCount);
        public void Configure(string title, Size pixels, Point point, int refreshMs, bool locked) { }
        public void ShowWindow() => IsVisible = true;
        public void HideWindow() => IsVisible = false;
        public void RequestFrame() { }
        public void Tick() => render();
        public void Present(SKBitmap bitmap) => Interlocked.Increment(ref _presentCount);
        public void Dispose() => IsVisible = false;
    }

    private sealed class BlockingTransport(
        ManualResetEventSlim entered, ManualResetEventSlim release) : IPhysicalFrameTransport
    {
        private int _sendCount;
        public int SendCount => Volatile.Read(ref _sendCount);
        public int WireRotationDegrees => 0;
        public void SendJpegWithDiagnostics(
            ReadOnlySpan<byte> jpeg, FrameRenderDiagnostics diagnostics)
        {
            Interlocked.Increment(ref _sendCount);
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(15)))
                throw new TimeoutException("Injected USB send was not released.");
        }
        public void RecordLifecycle(string state, string reason) { }
        public void Dispose() { }
    }
}
