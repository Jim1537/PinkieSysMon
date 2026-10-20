using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

// Bounded use of the real shared FramePump/renderer, independent of adoption.
internal static class TurzxRuntimeDiagnostic
{
    public const string Argument = "--turzx-runtime-check";
    internal const string CheckpointDashboardName = "turzx-runtime-check";

    public static int? TryRun(string[] args, Action<string> write,
        Func<IReadOnlyList<OutputDeviceDescriptor>>? capture = null,
        Func<OutputTargetConfig, int, bool, FileLogger, CancellationToken, IPhysicalFrameTransport?>? open = null,
        Func<C.DashboardDefinition>? loadBoard = null, Func<FileLogger>? createLog = null,
        int frameLimit = 20, int refreshMs = 500, Func<DateTime>? now = null)
    {
        if (args.Length == 0 || args[0] != Argument) return null;
        if (args.Length != 3 || args[1] != "--consent" || args[2] != "YES" || frameLimit is < 2 or > 20 || refreshMs < 10)
        {
            write("Usage: --turzx-runtime-check --consent YES. No hardware is opened without explicit consent.");
            return 2;
        }
        if (capture is null && !OperatingSystem.IsWindows())
        {
            write("RUNTIME CHECK: NOT RUN (Windows required)."); return 2;
        }
        write($"TURZX RUNTIME CHECK - Runtime v{RuntimeVersion.Current}");
        var invocation = Guid.NewGuid().ToString("N");
        write($"Invocation: {invocation}; UTC {DateTimeOffset.UtcNow:O}.");
        write($"Real FramePump and canonical renderer; at most {frameLimit} completed JPEG frames, {refreshMs} ms refresh, one native open attempt.");
        write("An adaptive 1920x480 diagnostic dashboard is generated in memory; no stock/saved board is loaded. No dashboard/configuration save, brightness, codec switch or firmware command.");
        FileLogger? log = null;
        var stage = "load/preflight";
        try
        {
            log = (createLog ?? (() => new FileLogger(Path.Combine(Environment.CurrentDirectory, "artifacts", "turzx-runtime-worker.log"))))();
            log.Info($"TURZX runtime checkpoint invocation {invocation}.");
            var board = loadBoard is null
                ? CreateCheckpointBoard(ApplicationRootLocator.Find(AppContext.BaseDirectory))
                : loadBoard();
            var metrics = new MetricStore();
            var clock = now ?? (() => DateTime.Now);
            metrics.Publish(SystemMetricContract.DateTime, clock());
            // Resolve assets/fonts and validate the encoded bound before any USB open.
            // Use the same explicit clock for bounded diagnostics and their real worker.
            using (var renderer = new DashboardRenderer(board, clock))
            using (var preflight = renderer.RenderJpeg(metrics.Snapshot(), PhysicalFrameProfile.Turzx, 90))
                write($"Preflight JPEG: {preflight.Length} bytes; wire raster 720x1920; aspect-preserving fit.");
            stage = "capture";
            var devices = (capture ?? TurzxDeviceDiscovery.Capture)();
            write($"Usable interfaces: {devices.Count}.");
            if (devices.Count != 1) { write("RUNTIME CHECK: INCOMPLETE (one exact interface required; no USB open)."); return 1; }
            var target = new OutputTargetConfig
            {
                Id = "turzx-runtime-check", Name = "TURZX runtime checkpoint", Kind = "turzx", Transport = "turzx",
                DeviceId = devices[0].DeviceId, DeviceEnabled = true, Dashboard = CheckpointDashboardName, RefreshIntervalMs = refreshMs,
                JpegQuality = 90, Usb = new UsbConfig { TransferTimeoutMs = 3000, RetryIntervalMs = 3000 }, Turzx = new()
            };
            var config = new AppConfig { Outputs = new OutputConfig { Targets = [target] } };
            var state = new CheckpointState();
            IPhysicalFrameTransport? OpenOnce(OutputTargetConfig selected, int timeout, bool journal, FileLogger logger, CancellationToken token)
            {
                if (Interlocked.CompareExchange(ref state.OpenAttempts, 1, 0) != 0)
                    throw new InvalidOperationException("The checkpoint does not reopen a failed connection.");
                try
                {
                    var transport = (open ?? PhysicalFrameTransportFactory.TryOpen)(selected, timeout, journal, logger, token);
                    if (transport is null) throw new IOException("The selected binding disappeared before open.");
                    return new ObservedTransport(transport, state);
                }
                catch (Exception error) { Interlocked.CompareExchange(ref state.Failure, error, null); throw; }
            }
            stage = "shared worker";
            var completed = false;
            using (var stop = new CancellationTokenSource())
            {
                ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stop.Cancel(); };
                Console.CancelKeyPress += cancel;
                try
                {
                    using var pump = new FramePump(config, target, board, metrics, log, OpenOnce,
                        maximumFrames: frameLimit, frameClockNow: clock);
                    pump.Start();
                    var timer = Stopwatch.StartNew();
                    long lastReported = 0;
                    while (!stop.IsCancellationRequested && timer.Elapsed < TimeSpan.FromSeconds(20))
                    {
                        metrics.Publish(SystemMetricContract.DateTime, clock());
                        var sent = pump.SentFrames;
                        if (sent != lastReported)
                        {
                            write($"Completed frames: {sent}/{frameLimit}; distinct consecutive payloads: {Volatile.Read(ref state.ChangedFrames)}.");
                            lastReported = sent;
                        }
                        if (Volatile.Read(ref state.Failure) is { } failure) throw failure;
                        if (sent >= frameLimit)
                        {
                            completed = Volatile.Read(ref state.ChangedFrames) >= 2;
                            break;
                        }
                        // Wait is interruptible; it is not a firmware timing rule.
                        stop.Token.WaitHandle.WaitOne(50);
                    }
                    write($"Worker result: completed={pump.SentFrames}; distinct consecutive payloads={Volatile.Read(ref state.ChangedFrames)}; open attempts={Volatile.Read(ref state.OpenAttempts)}.");
                    if (!completed) write("RUNTIME CHECK: INCOMPLETE (frame budget/changed payloads not established, deadline or user cancellation).");
                }
                finally { Console.CancelKeyPress -= cancel; }
            }
            if (!completed) return 1;
            write("RUNTIME CHECK: FRAMES COMPLETE (shared worker disposed after I/O completion; physical animation/orientation require owner observation).");
            return 0;
        }
        catch (Exception error)
        {
            log?.Error($"TURZX runtime checkpoint failed at {stage}.", error);
            write($"RUNTIME CHECK: ERROR at {stage} ({error.GetType().Name}; HRESULT 0x{error.HResult:X8}).");
            if (error is Win32Exception native) write($"Native error code: {native.NativeErrorCode}.");
            return 1;
        }
        finally { write("No automatic native reopen/retry, brightness or firmware-save/reset. Preserve this log and artifacts/turzx-runtime-worker.log."); }
    }

    internal static C.DashboardDefinition CreateCheckpointBoard(string applicationRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationRoot);

        // This logical directory provides the application asset context; it is
        // never created or read as a persisted dashboard.
        var board = AdaptiveDashboardFactory.Create(1920, 480, 0,
            Path.Combine(applicationRoot, "dashboards", CheckpointDashboardName));
        var title = board.Widgets.OfType<C.ValueWidgetDefinition>().Single(widget => widget.Id == "brand");
        title.Text = "PINKIESYSMON / TURZX LIVE";
        board.Validate();
        return board;
    }

    private sealed class CheckpointState
    {
        public int OpenAttempts, ChangedFrames;
        public Exception? Failure;
        public byte[]? PreviousHash;
    }

    private sealed class ObservedTransport(IPhysicalFrameTransport inner, CheckpointState state) : IPhysicalFrameTransport
    {
        public int WireRotationDegrees => inner.WireRotationDegrees;
        public PhysicalFrameProfile Profile => inner.Profile;
        public void SendJpegWithDiagnostics(ReadOnlySpan<byte> jpeg, FrameRenderDiagnostics diagnostics)
        {
            try
            {
                inner.SendJpegWithDiagnostics(jpeg, diagnostics);
                var hash = SHA256.HashData(jpeg);
                if (state.PreviousHash is null || !hash.AsSpan().SequenceEqual(state.PreviousHash))
                    Interlocked.Increment(ref state.ChangedFrames);
                state.PreviousHash = hash;
            }
            catch (Exception error) { Interlocked.CompareExchange(ref state.Failure, error, null); throw; }
        }
        public void RecordLifecycle(string status, string reason) => inner.RecordLifecycle(status, reason);
        public void Dispose() => inner.Dispose();
    }
}
