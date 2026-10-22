using System.Reflection;
using System.Text.Json;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

// Real FramePump/manager lifetime with gated transports; no physical USB access.
internal static class FramePumpShutdownTests
{
    public static void FinalOutputFrameCompletion()
    {
        using var root = new TestRoot();
        using var writeEntered = new ManualResetEventSlim();
        using var releaseWrite = new ManualResetEventSlim();
        using var ackEntered = new ManualResetEventSlim();
        using var releaseAck = new ManualResetEventSlim();
        var target = Target(15_000);
        var config = new AppConfig { Outputs = new OutputConfig { Targets = [target] } };
        var metrics = new MetricStore();
        var pumps = new List<FramePump>();
        TransactionTransport? first = null;
        var opens = 0;
        using var manager = new OutputSessionManager(config, root.Board(), metrics, root.Log,
            (app, selected, board, store, log) =>
            {
                var pump = new FramePump(app, selected, board, store, log,
                    (_, _, _, _, token) =>
                    {
                        if (Interlocked.Increment(ref opens) != 1)
                            return new TransactionTransport(token);
                        first = new TransactionTransport(token, writeEntered, releaseWrite, ackEntered, releaseAck);
                        return first;
                    });
                pumps.Add(pump);
                return pump;
            });
        Task? disabling = null;
        try
        {
            manager.Start();
            Check(writeEntered.Wait(TimeSpan.FromSeconds(5)), "Initial worker must enter the synthetic frame write.");
            var transport = first ?? throw new InvalidOperationException("Initial transport was not created.");
            var disabled = Clone(target);
            disabled.DeviceEnabled = false;
            disabling = Task.Run(() => manager.ReconfigurePhysicalEnablement(disabled, root.Board(), _ => { }, () => { }));
            WaitForStopRequest(pumps[0]);

            Check(!transport.Token.IsCancellationRequested && transport.DisposeCount == 0 && !disabling.IsCompleted,
                "Final-Output disable must retain the in-flight frame and its transport instead of immediately cancelling writes.");
            releaseWrite.Set();
            Check(ackEntered.Wait(TimeSpan.FromSeconds(5)), "The admitted frame must advance from writes to its ACK.");
            Check(!transport.Token.IsCancellationRequested && transport.DisposeCount == 0 && !disabling.IsCompleted,
                "Ordinary disable must also let the current ACK complete before closing the transport.");
            releaseAck.Set();
            Check(disabling.Wait(TimeSpan.FromSeconds(5)), "Disable must finish after the complete frame/ACK transaction.");
            disabling.GetAwaiter().GetResult();

            Check(transport.SendCount == 1 && transport.CompletedFrames == 1 && transport.DisposeCount == 1 &&
                  !transport.DisposedDuringTransfer && opens == 1 && pumps[0].SentFrames == 0,
                "Shutdown must finish exactly the admitted transaction, release once and suppress stale frame publication.");
            Check(manager.GetStatuses().Single() is { IsActive: false, OutputEnabled: false },
                "Disabling the final Output must leave an inactive saved target and a running manager lifetime.");

            manager.ReconfigurePhysicalEnablement(Clone(target), root.Board(), _ => { }, () => { });
            Check(SpinWait.SpinUntil(() => Volatile.Read(ref opens) == 2 && pumps[1].SentFrames > 0,
                    TimeSpan.FromSeconds(5)),
                "The final physical Output must re-enable with a fresh worker/transport and deliver a completed synthetic frame.");
            Check(manager.GetStatuses().Single() is { IsActive: true, OutputEnabled: true, IsConnected: true },
                "Re-enable after an empty session set must retain the normal running lifetime and observed ready status.");
            pumps[0].Dispose();
            Check(transport.DisposeCount == 1, "Repeated disposal must not release the retired transport again.");
        }
        finally
        {
            releaseWrite.Set();
            releaseAck.Set();
            disabling?.GetAwaiter().GetResult();
        }
    }

    public static void HandshakeCompletion()
    {
        using var root = new TestRoot();
        using var entered = new ManualResetEventSlim();
        using var releaseOpen = new ManualResetEventSlim();
        TransactionTransport? transport = null;
        using var pump = new FramePump(new AppConfig(), Target(15_000), root.Board(), new MetricStore(), root.Log,
            (_, _, _, _, token) =>
            {
                transport = new TransactionTransport(token);
                entered.Set();
                if (!releaseOpen.Wait(TimeSpan.FromSeconds(10), token))
                    throw new TimeoutException("Synthetic handshake was not released.");
                return transport;
            });
        Task? disposal = null;
        try
        {
            pump.Start();
            Check(entered.Wait(TimeSpan.FromSeconds(5)), "Worker must enter the in-flight synthetic handshake.");
            var opened = transport ?? throw new InvalidOperationException("Synthetic handshake transport is missing.");
            disposal = Task.Run(pump.Dispose);
            WaitForStopRequest(pump);
            Check(!opened.Token.IsCancellationRequested && !disposal.IsCompleted,
                "Ordinary stop must allow a healthy handshake to reach its terminal result.");
            releaseOpen.Set();
            Check(disposal.Wait(TimeSpan.FromSeconds(5)), "Disposal must complete after the in-flight handshake returns.");
            disposal.GetAwaiter().GetResult();
            Check(opened.SendCount == 0 && opened.DisposeCount == 1 && !opened.DisposedDuringTransfer &&
                  !pump.IsConnected && pump.SentFrames == 0,
                "A connection completed after stop intent must be released without admitting a frame or publishing CONNECTED.");
        }
        finally
        {
            releaseOpen.Set();
            disposal?.GetAwaiter().GetResult();
        }
    }

    public static void StalledHandshakeCancellation()
    {
        using var root = new TestRoot();
        using var entered = new ManualResetEventSlim();
        using var canceled = new ManualResetEventSlim();
        using var releaseNativeCompletion = new ManualResetEventSlim();
        var config = new AppConfig { Usb = new UsbConfig { TransferTimeoutMs = 15_000 } };
        var nativeCompleted = 0;
        using var pump = new FramePump(config, Target(500), root.Board(), new MetricStore(), root.Log,
            (_, _, _, _, token) =>
            {
                entered.Set();
                if (!token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("Stalled handshake did not receive fallback cancellation.");
                canceled.Set();
                if (!releaseNativeCompletion.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("Synthetic terminal native completion was not released.");
                Interlocked.Exchange(ref nativeCompleted, 1);
                token.ThrowIfCancellationRequested();
                throw new InvalidOperationException("Stalled handshake completed without cancellation.");
            });
        Task? disposal = null;
        try
        {
            pump.Start();
            Check(entered.Wait(TimeSpan.FromSeconds(5)), "Worker must enter the stalled synthetic handshake.");
            disposal = Task.Run(pump.Dispose);
            Check(canceled.Wait(TimeSpan.FromSeconds(3)),
                "Fallback cancellation must use the selected 500 ms timeout rather than the 15 s global default.");
            Check(!disposal.IsCompleted && Volatile.Read(ref nativeCompleted) == 0,
                "The grace timeout must not release ownership before canceled native I/O actually completes.");
            releaseNativeCompletion.Set();
            Check(disposal.Wait(TimeSpan.FromSeconds(3)), "Disposal must finish once canceled native completion is drained.");
            disposal.GetAwaiter().GetResult();
            Check(nativeCompleted == 1 && pump.SentFrames == 0 && !pump.IsConnected,
                "A stalled open must end without a frame or stale connected state after fallback cancellation.");
        }
        finally
        {
            releaseNativeCompletion.Set();
            disposal?.GetAwaiter().GetResult();
        }
    }

    public static void SelectedPhysicalRemovalRelease()
    {
        using var root = new TestRoot();
        using var writeEntered = new ManualResetEventSlim();
        using var releaseWrite = new ManualResetEventSlim();
        var selected = Target(15_000);
        var sibling = Clone(selected);
        sibling.Id = "synthetic-sibling";
        sibling.DeviceId = "usb-interface:" + new string('a', 64);
        var config = new AppConfig { Outputs = new OutputConfig { Targets = [selected, sibling] } };
        TransactionTransport? selectedTransport = null;
        TransactionTransport? siblingTransport = null;
        var selectedCreated = 0;
        using var manager = new OutputSessionManager(config, root.Board(), new MetricStore(), root.Log,
            (app, target, board, store, log) => new FramePump(app, target, board, store, log,
                (_, _, _, _, token) =>
                {
                    if (target.Id == selected.Id)
                    {
                        Interlocked.Increment(ref selectedCreated);
                        return selectedTransport ??= new TransactionTransport(token, writeEntered, releaseWrite);
                    }
                    return siblingTransport ??= new TransactionTransport(token);
                }));
        Task? removal = null;
        try
        {
            manager.Start();
            Check(writeEntered.Wait(TimeSpan.FromSeconds(5)), "Selected frame must enter its native transaction.");
            Check(SpinWait.SpinUntil(() => siblingTransport?.CompletedFrames > 0, TimeSpan.FromSeconds(5)),
                "Sibling Output must run before selected removal.");
            removal = Task.Run(() => manager.PreparePhysicalRemoval(selected.Id));
            Check(SpinWait.SpinUntil(() => manager.GetStatuses().Single(s => s.TargetId == selected.Id).IsActive == false,
                TimeSpan.FromSeconds(5)) && !removal.IsCompleted && selectedTransport!.DisposeCount == 0,
                "Removal reserves the selected Output without claiming release during an in-flight frame.");
            releaseWrite.Set();
            Check(removal.Wait(TimeSpan.FromSeconds(5)), "Selected native completion must allow terminal removal.");
            removal.GetAwaiter().GetResult();
            Check(selectedTransport!.DisposeCount == 1 && !selectedTransport.DisposedDuringTransfer &&
                  siblingTransport!.DisposeCount == 0 && manager.GetStatuses().Single(s => s.TargetId == sibling.Id).IsActive,
                "Only the selected native transport is released; the sibling remains active.");
            var blocked = false;
            try { manager.Reconfigure(config, root.Board()); }
            catch (InvalidOperationException) { blocked = true; }
            Check(blocked, "Pending Remove must prevent the old enabled Output from reopening.");
            var after = new AppConfig { Outputs = new OutputConfig { Targets = [sibling] } };
            manager.Reconfigure(after, root.Board());
            Check(manager.GetStatuses().Single().TargetId == sibling.Id && selectedCreated == 1,
                "After persisted deletion, the sibling continues without recreating the removed target.");
        }
        finally { releaseWrite.Set(); removal?.GetAwaiter().GetResult(); }

        // A native disposal error must not be misreported as terminal release.
        var failedTransport = new TransactionTransport(CancellationToken.None, failDispose: true);
        using var failed = new OutputSessionManager(
            new AppConfig { Outputs = new OutputConfig { Targets = [selected] } }, root.Board(),
            new MetricStore(), root.Log,
            (app, target, board, store, log) => new FramePump(app, target, board, store, log,
                (_, _, _, _, _) => failedTransport));
        failed.Start();
        Check(SpinWait.SpinUntil(() => failedTransport.CompletedFrames > 0, TimeSpan.FromSeconds(5)),
            "Synthetic failed-release transport must be active first.");
        try { failed.PreparePhysicalRemoval(selected.Id); }
        catch (InvalidOperationException)
        {
            Check(failedTransport.DisposeCount == 1 && !failed.GetStatuses().Single().IsActive,
                "A failed native Dispose leaves an inactive reserved Output and refuses terminal success.");
            return;
        }
        throw new InvalidOperationException("A failed native release must reject selected Remove.");
    }

    private static OutputTargetConfig Target(int timeoutMs) => new()
    { Transport = OutputTargetContract.LyBulkTransport, Width = 1920, Height = 480, DeviceId = "usb-interface:e912cbded0e8644fea777832da3443ec7f5a9ed35fa4a52b2eda150d97218dae",
        Id = "synthetic-physical", Name = "Synthetic physical", Dashboard = "Synthetic",
        DeviceEnabled = true, ProviderEnabled = true, RefreshIntervalMs = 50,
        Usb = new UsbConfig { TransferTimeoutMs = timeoutMs }
    };

    private static OutputTargetConfig Clone(OutputTargetConfig target) =>
        JsonSerializer.Deserialize<OutputTargetConfig>(JsonSerializer.Serialize(target))!;

    private static void WaitForStopRequest(FramePump pump)
    {
        // Observe only the stop-intent boundary to synchronize the race. Behavioral
        // assertions above concern frame/ACK completion, cancellation and ownership.
        var field = typeof(FramePump).GetField("_disposeState", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("FramePump stop-intent field is missing.");
        Check(SpinWait.SpinUntil(() => (int)field.GetValue(pump)! != 0, TimeSpan.FromSeconds(5)),
            "The disposal task must reach its stop-intent boundary.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class TestRoot : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "PinkieSysMon-shutdown-" + Guid.NewGuid().ToString("N"));
        public FileLogger Log { get; }
        public TestRoot()
        {
            Directory.CreateDirectory(Path.Combine(_path, "config"));
            Directory.CreateDirectory(Path.Combine(_path, "dashboards"));
            File.WriteAllText(Path.Combine(_path, "config", "app.json"), "{}");
            Log = new FileLogger(Path.Combine(_path, "test.log"));
        }
        public C.DashboardDefinition Board() => new()
        {
            BaseDirectory = Path.Combine(_path, "dashboards"),
            Canvas = new C.CanvasDefinition { Width = 64, Height = 32 },
            Widgets = []
        };
        public void Dispose() => Directory.Delete(_path, recursive: true);
    }

    private sealed class TransactionTransport(CancellationToken token,
        ManualResetEventSlim? writeEntered = null, ManualResetEventSlim? releaseWrite = null,
        ManualResetEventSlim? ackEntered = null, ManualResetEventSlim? releaseAck = null,
        bool failDispose = false) : IPhysicalFrameTransport
    {
        private int _sends, _completed, _disposals, _inFlight, _disposedInFlight;
        public CancellationToken Token => token;
        public int WireRotationDegrees => 0;
        public int SendCount => Volatile.Read(ref _sends);
        public int CompletedFrames => Volatile.Read(ref _completed);
        public int DisposeCount => Volatile.Read(ref _disposals);
        public bool DisposedDuringTransfer => Volatile.Read(ref _disposedInFlight) != 0;
        public void SendJpegWithDiagnostics(ReadOnlySpan<byte> jpeg, FrameRenderDiagnostics diagnostics)
        {
            Interlocked.Increment(ref _sends);
            Interlocked.Exchange(ref _inFlight, 1);
            try
            {
                writeEntered?.Set();
                if (releaseWrite is not null && !releaseWrite.Wait(TimeSpan.FromSeconds(10), token))
                    throw new TimeoutException("Synthetic frame write was not released.");
                token.ThrowIfCancellationRequested();
                ackEntered?.Set();
                if (releaseAck is not null && !releaseAck.Wait(TimeSpan.FromSeconds(10), token))
                    throw new TimeoutException("Synthetic frame ACK was not released.");
                token.ThrowIfCancellationRequested();
                Interlocked.Increment(ref _completed);
            }
            finally { Interlocked.Exchange(ref _inFlight, 0); }
        }
        public void RecordLifecycle(string state, string reason) { }
        public void Dispose()
        {
            if (Volatile.Read(ref _inFlight) != 0) Interlocked.Exchange(ref _disposedInFlight, 1);
            Interlocked.Increment(ref _disposals);
            if (failDispose) throw new IOException("Injected native release failure.");
        }
    }
}
