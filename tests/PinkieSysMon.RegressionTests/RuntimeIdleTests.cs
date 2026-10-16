using System.Drawing;
using System.Text.Json;
using PinkieSysMon;
using SkiaSharp;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class RuntimeIdleTests
{
    public static void PolicyAndConfig()
    {
        Equal(null, RuntimeIdlePolicy.EffectiveTimeoutMs(0, 15), "Default disables idle suspension.");
        Equal(300_000L, RuntimeIdlePolicy.EffectiveTimeoutMs(5, 900), "Runtime setting is shorter.");
        Equal(900_000L, RuntimeIdlePolicy.EffectiveTimeoutMs(30, 900), "Windows setting is shorter.");
        Equal(300_000L, RuntimeIdlePolicy.EffectiveTimeoutMs(5, 300), "Equal settings are stable.");
        Equal(1_800_000L, RuntimeIdlePolicy.EffectiveTimeoutMs(30, 0), "Windows Never does not cap.");
        Equal(1_800_000L, RuntimeIdlePolicy.EffectiveTimeoutMs(30, null), "Unavailable Windows ceiling does not replace user setting.");
        Check(!RuntimeIdlePolicy.IsIdle(5, 300, 299_999) &&
              RuntimeIdlePolicy.IsIdle(5, 300, 300_000) &&
              !RuntimeIdlePolicy.IsIdle(0, 1, uint.MaxValue),
            "Idle transition must use the effective threshold and disabled semantics.");

        Sandbox((root, log) =>
        {
            var path = Path.Combine(root, "app.json");
            File.WriteAllText(path, "{}");
            var config = AppConfig.Load(path, log);
            Check(config.Runtime.IdleTimeoutMinutes == 0, "Missing field must default to disabled.");
            config.Runtime.IdleTimeoutMinutes = 30;
            config.Save(path);
            var loaded = AppConfig.Load(path, log);
            Check(loaded.Runtime.IdleTimeoutMinutes == 30 &&
                  JsonDocument.Parse(File.ReadAllText(path)).RootElement
                    .GetProperty("Runtime").GetProperty("IdleTimeoutMinutes").GetInt32() == 30,
                "Configured value must survive save/load independently from Windows ceiling.");
            loaded.Runtime.IdleTimeoutMinutes = -1;
            Throws<InvalidDataException>(() => loaded.Save(path));
            loaded.Runtime.IdleTimeoutMinutes = 1441;
            Throws<InvalidDataException>(() => loaded.Save(path));
        });
    }

    public static void PowerPolicyRefresh()
    {
        uint idleMs = 90_000;
        uint? windowsSeconds = 60;
        long clock = 0;
        var reads = 0;
        var monitor = new RuntimeIdleMonitor(
            () => idleMs,
            () => { reads++; return windowsSeconds; },
            () => clock);

        Check(monitor.ShouldPause(5), "Shorter active Windows display timeout must pause.");
        windowsSeconds = 600;
        Check(monitor.ShouldPause(5) && reads == 1, "Power queries must be low frequency.");
        clock = 15_000;
        Check(!monitor.ShouldPause(5) && reads == 2, "Active policy changes must be observed.");
        idleMs = 400_000;
        Check(monitor.ShouldPause(5), "Configured threshold must remain authoritative.");
        windowsSeconds = 0;
        idleMs = 90_000;
        monitor.InvalidatePowerSetting();
        Check(!monitor.ShouldPause(5), "Windows Never must impose no ceiling.");
        windowsSeconds = 20;
        monitor.InvalidatePowerSetting();
        Check(monitor.ShouldPause(5), "Power source or scheme invalidation must refresh the ceiling.");
        Check(!monitor.ShouldPause(0), "Disabling idle must resume independently of cached Windows power settings.");
    }

    public static void TelemetryDoesNotCatchUp()
    {
        Sandbox((root, log) =>
        {
            var source = new CountingSource();
            using var engine = new TelemetryEngine(new MetricStore(), [source],
                TelemetryConfig.CreateDefaults([source]), [source.ProviderId], [CountingSource.Id], log);
            engine.Start();
            Check(SpinWait.SpinUntil(() => source.Captures >= 2, TimeSpan.FromSeconds(5)),
                "Active telemetry must capture its requested metric.");
            engine.SetIdlePaused(true);
            Thread.Sleep(120);
            var settled = source.Captures;
            Thread.Sleep(200);
            Check(source.Captures == settled, "Idle telemetry must not poll scheduled sources.");
            engine.SetIdlePaused(true);
            engine.SetIdlePaused(false);
            Check(SpinWait.SpinUntil(() => source.Captures > settled, TimeSpan.FromSeconds(5)),
                "Wake must take a fresh sample without restarting a worker.");
            var resumed = source.Captures;
            Thread.Sleep(100);
            Check(source.Captures - resumed <= 4, "Wake must not replay missed samples as a burst.");
        });
    }

    public static void OutputsPreserveStateAndTransport()
    {
        Sandbox((root, log) =>
        {
            var boardDirectory = Path.Combine(root, "board");
            Directory.CreateDirectory(boardDirectory);
            C.DashboardDefinition Board() => new()
            {
                BaseDirectory = boardDirectory,
                Canvas = new C.CanvasDefinition { Width = 64, Height = 32, BackgroundColor = "#FF123456" },
                Widgets = []
            };

            var config = new AppConfig
            {
                Runtime = new RuntimeConfig { IdleTimeoutMinutes = 5 },
                Outputs = new OutputConfig
                {
                    Targets =
                    [
                        new OutputTargetConfig
                        {
                            Id = "desktop-1", Name = "Idle Desktop",
                            Kind = OutputTargetContract.DesktopKind,
                            Transport = OutputTargetContract.DesktopTransport,
                            Dashboard = "Default", RefreshIntervalMs = 50,
                            Desktop = new DesktopOutputConfig { BoardEnabled = true, Lock = true, X = 42, Y = 91 }
                        },
                        new OutputTargetConfig
                        {
                            Id = "usb-1", Name = "Idle USB",
                            Kind = OutputTargetContract.TrofeoKind,
                            Transport = OutputTargetContract.TrofeoTransport,
                            DeviceEnabled = true, Dashboard = "Default", RefreshIntervalMs = 50
                        }
                    ]
                }
            };

            CountingWindow? window = null;
            var transports = new List<CountingTransport>();
            using var manager = new OutputSessionManager(config, Board(), new MetricStore(), log,
                (app, target, dashboard, metrics, logger) =>
                    target.Kind == OutputTargetContract.DesktopKind
                    ? new DesktopOutputSession(app, target, dashboard, metrics, logger,
                        render => window = new CountingWindow(render))
                    : new FramePump(app, target, dashboard, metrics, logger,
                        (device, timeout, journal, l, token) =>
                        {
                            var transport = new CountingTransport();
                            lock (transports) transports.Add(transport);
                            return transport;
                        }));

            manager.Start();
            Check(SpinWait.SpinUntil(() =>
            {
                lock (transports)
                    return transports.Count == 1 && transports[0].Sends >= 2;
            }, TimeSpan.FromSeconds(5)), "Physical worker must start normal frame transfers.");

            var desktop = window ?? throw new InvalidOperationException("No Desktop window.");
            desktop.Tick();
            Check(desktop.IsVisible && desktop.Presents == 1,
                "The enabled Desktop must render before idle.");

            manager.SetIdlePaused(true);
            Check(!desktop.IsVisible && manager.GetRequiredMetrics().Count == 0,
                "Idle must hide Desktop and remove dashboard polling demand.");
            desktop.Tick();
            Check(desktop.Presents == 1, "Delayed Desktop callbacks must not render during idle.");
            Thread.Sleep(150);
            int pausedSends;
            lock (transports) pausedSends = transports[0].Sends;
            Thread.Sleep(180);
            lock (transports)
                Check(transports.Count == 1 && transports[0].Sends == pausedSends &&
                      transports[0].Disposals == 0,
                    "Idle must stop USB traffic without retiring the live transport.");

            manager.SetIdlePaused(true);
            manager.SetIdlePaused(false);
            Check(desktop.IsVisible && desktop.Disposals == 0 &&
                  desktop.Location == new Point(42, 91),
                "Wake must restore the same enabled Desktop window and placement.");
            desktop.Tick();
            Check(desktop.Presents == 2, "Wake must restore Desktop frame production.");
            Check(SpinWait.SpinUntil(() =>
            {
                lock (transports)
                    return transports[0].Sends > pausedSends;
            }, TimeSpan.FromSeconds(5)), "Wake must resume physical transfers.");
            lock (transports)
                Check(transports.Count == 1, "Routine idle wake must reuse the transport.");

            manager.SetIdlePaused(true);
            manager.Suspend();
            Check(SpinWait.SpinUntil(() =>
            {
                lock (transports) return transports[0].Disposals == 1;
            }, TimeSpan.FromSeconds(5)), "Actual OS suspend must still retire the native transport.");
            manager.Resume();
            Thread.Sleep(150);
            lock (transports)
                Check(transports.Count == 1,
                    "OS resume while still idle must not reopen the USB transport.");
            Check(!desktop.IsVisible, "OS resume must not bypass the independent idle gate.");
            manager.SetIdlePaused(false);
            Check(SpinWait.SpinUntil(() =>
            {
                lock (transports) return transports.Count >= 2 && transports[1].Sends > 0;
            }, TimeSpan.FromSeconds(5)), "Input wake after OS resume must reconnect safely.");
        });
    }

    public static void InFlightUsbPause()
    {
        Sandbox((root, log) =>
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var board = new C.DashboardDefinition
            {
                BaseDirectory = root,
                Canvas = new C.CanvasDefinition { Width = 64, Height = 32 },
                Widgets = []
            };
            var target = new OutputTargetConfig
            {
                Id = "blocked-idle", Name = "Blocked idle",
                Kind = OutputTargetContract.TrofeoKind,
                Transport = OutputTargetContract.TrofeoTransport,
                DeviceEnabled = true, Dashboard = "Default", RefreshIntervalMs = 40
            };
            var config = new AppConfig { Outputs = new OutputConfig { Targets = [target] } };
            var transport = new BlockingFirstTransport(entered, release);
            using var pump = new FramePump(config, target, board, new MetricStore(), log,
                (device, timeout, journal, l, token) => transport);
            try
            {
                pump.Start();
                Check(entered.Wait(TimeSpan.FromSeconds(5)), "USB test must enter the first transfer.");
                pump.SetIdlePaused(true); // Must not wait for an outstanding native transfer.
                Check(transport.Sends == 1 && transport.Disposals == 0,
                    "Idle does not cancel or close an in-flight native transport.");
                release.Set();
                Thread.Sleep(150);
                Check(transport.Sends == 1, "No second USB frame is allowed while paused.");
                pump.SetIdlePaused(false);
                Check(SpinWait.SpinUntil(() => transport.Sends > 1, TimeSpan.FromSeconds(5)) &&
                      transport.Disposals == 0,
                    "Resume must send fresh frames through the same transport.");
            }
            finally { release.Set(); }
        });
    }

    private static void Sandbox(Action<string, FileLogger> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkiesysmon-idle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "config"));
        Directory.CreateDirectory(Path.Combine(root, "dashboards"));
        File.WriteAllText(Path.Combine(root, "config", "app.json"), "{}");
        try { action(root, new FileLogger(Path.Combine(root, "idle.log"))); }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void Check(bool valid, string explanation)
    {
        if (!valid) throw new InvalidOperationException(explanation);
    }

    private static void Equal(long? expected, long? actual, string description) =>
        Check(expected == actual, description);

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed class CountingSource : IMetricSource
    {
        public const string Id = "test.idle.counter";
        private int _count;
        public int Captures => Volatile.Read(ref _count);
        public string ProviderId => "test.idle";
        public string Name => "Idle test";
        public int DefaultIntervalMs => 50;
        public IReadOnlyCollection<string> MetricNames { get; } = [Id];
        public IReadOnlyDictionary<string, object?> Capture() => Capture(MetricNames);
        public IReadOnlyDictionary<string, object?> Capture(IReadOnlyCollection<string> requested)
        {
            Interlocked.Increment(ref _count);
            return requested.ToDictionary(x => x, x => (object?)42d);
        }
    }

    private sealed class CountingWindow(Action draw) : IDesktopOutputWindow
    {
        public bool IsVisible { get; private set; }
        public Point Location { get; private set; }
        public int Presents { get; private set; }
        public int Disposals { get; private set; }
        public void Configure(string title, Size size, Point point, int intervalMs, bool locked) =>
            Location = point;
        public void ShowWindow() => IsVisible = true;
        public void HideWindow() => IsVisible = false;
        public void Tick() => draw();
        public void RequestFrame() { }
        public void Present(SKBitmap bitmap) => Presents++;
        public void Dispose() { Disposals++; IsVisible = false; }
    }

    private sealed class CountingTransport : ITrofeoFrameTransport
    {
        private int _sends;
        private int _disposals;
        public int Sends => Volatile.Read(ref _sends);
        public int Disposals => Volatile.Read(ref _disposals);
        public int WireRotationDegrees => 0;
        public void SendJpegWithDiagnostics(ReadOnlySpan<byte> jpeg, TrofeoFrameRenderDiagnostics diagnostics) =>
            Interlocked.Increment(ref _sends);
        public void RecordLifecycle(string state, string reason) { }
        public void Dispose() => Interlocked.Increment(ref _disposals);
    }
    private sealed class BlockingFirstTransport(
        ManualResetEventSlim entered, ManualResetEventSlim release) : ITrofeoFrameTransport
    {
        private int _sends;
        private int _disposals;
        public int Sends => Volatile.Read(ref _sends);
        public int Disposals => Volatile.Read(ref _disposals);
        public int WireRotationDegrees => 0;
        public void SendJpegWithDiagnostics(ReadOnlySpan<byte> jpeg, TrofeoFrameRenderDiagnostics diagnostics)
        {
            if (Interlocked.Increment(ref _sends) == 1)
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("Idle test USB transfer timed out.");
            }
        }
        public void RecordLifecycle(string state, string reason) { }
        public void Dispose() => Interlocked.Increment(ref _disposals);
    }
}
