using System.Diagnostics;
using System.Text.Json;
using PinkieSysMon.Editor;
using SkiaSharp;

namespace PinkieSysMon.RegressionTests;

internal static class Issue95LiveTestTests
{
    public static void WorkerCompletionAndVirtualBoard()
    {
        WithRoot(root =>
        {
            foreach (var protocol in new[] { OutputTargetContract.LyBulkTransport, OutputTargetContract.EncryptedJpegTransport })
            {
                var request = Request(protocol);
                var frames = new Frames(protocol);
                var opens = 0;
                var service = Service(root, request, frames, () => opens++);
                Check(service.Execute(Begin(request)).Success, "Valid exact candidate must start a transient worker.");
                var result = Wait(service, request);
                Check(result.TechnicalOutcome == CatalogUsbTestTechnicalOutcome.SucceededAndReleased &&
                    opens == 1 && frames.Images.Count == RuntimeUsbTestService.FrameCount && frames.Disposals == 1 && !service.HasActive,
                    "Success requires every bounded frame and completed native release on exactly one acquisition.");
                Check(!frames.Images.First().AsSpan().SequenceEqual(frames.Images.Last()), "The canonical clock must produce changing real raster payloads.");
                using var decoded = SKBitmap.Decode(frames.Images[0]);
                Check(decoded.Width == (protocol == OutputTargetContract.EncryptedJpegTransport ? 240 : 320), "Virtual geometry follows user input and ordinary conversion.");
                Check(!service.Execute(Begin(request)).Success, "A completed attempt ID cannot acquire again.");
                Check(File.ReadAllText(Path.Combine(root, "config", "app.json")) == "untouched" &&
                    Directory.GetFiles(root, "dashboard.json", SearchOption.AllDirectories).Length == 0,
                    "Test must never persist the generated document or application configuration.");
            }
        });
    }

    public static void StrictWorkerFailureAndCleanup()
    {
        WithRoot(root =>
        {
            foreach (var mode in new[] { "open", "open-cleanup", "send", "encode", "cleanup" })
            {
                var request = Request(); var opens = 0;
                var frames = new Frames(request.Intent.Protocol) { FailAt = mode == "send" ? 2 : 0, FailCleanup = mode == "cleanup", FailEncode = mode == "encode" };
                var service = Service(root, request, frames, () => { opens++; if (mode == "open") throw new IOException("Synthetic open failure.");
                    if (mode == "open-cleanup") throw new PhysicalTransportCleanupException(new IOException("Open"), new IOException("Cleanup")); });
                Check(service.Execute(Begin(request)).Success, "The worker, not the IPC parser, owns execution failures.");
                var result = Wait(service, request);
                Check(opens == 1 && result.Phase == (mode is "cleanup" or "open-cleanup" ? RuntimeUsbTestPhase.CleanupPending : RuntimeUsbTestPhase.FailedAndReleased),
                    "No first error may become successful or trigger reacquisition, including a failure after a visible frame.");
                Check(mode is "open" or "open-cleanup" || frames.Disposals == 1, "Failed worker must drain and dispose its transport once.");
                if (mode == "send") Check(frames.Images.Count == 1, "A delivered frame before failure does not authorize Save.");
                if (mode is "cleanup" or "open-cleanup") Check(service.HasActive && !service.Execute(Begin(Request())).Success,
                    "Unconfirmed cleanup retains the reservation and forbids another Test.");
            }
        });
    }

    public static void CancellationWaitsForNativeRelease()
    {
        WithRoot(root =>
        {
            var request = Request();
            using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            var frames = new Frames(request.Intent.Protocol) { Entered = entered, Release = release };
            var service = Service(root, request, frames, () => { });
            service.Execute(Begin(request));
            try
            {
                Check(entered.Wait(5000), "Worker must reach its injected native send.");
                Check(!service.Execute(Begin(Request())).Success, "A second client cannot acquire during a send.");
                service.Execute(new(RuntimeIpcCommandKind.UsbTestCancel, UsbTestHandle: new(Guid.NewGuid(), request.Intent.AttemptId)));
                Check(Status(service, request).Phase == RuntimeUsbTestPhase.Running, "A foreign client cannot cancel this attempt.");
                service.CancelActive();
                Check(Status(service, request).Phase == RuntimeUsbTestPhase.CancelRequested && service.HasActive && frames.Disposals == 0,
                    "Cancel is intent only; admission must remain reserved while native completion is blocked.");
            }
            finally { release.Set(); }
            Check(Wait(service, request).TechnicalOutcome == CatalogUsbTestTechnicalOutcome.CanceledAndReleased && frames.Disposals == 1,
                "Only completed disposal publishes a canceled-and-released result.");
        });
    }

    public static void ExistingOwnerAndResponseCorrelation()
    {
        var request = Request(); var snapshot = Snapshot(request.Intent);
        var own = new OutputTargetConfig { Kind = "physical", DeviceId = request.Intent.InterfaceId };
        Check(RuntimeUsbTestService.IsOwned(request.Intent, snapshot, [own]), "Exact ordinary binding is busy.");
        var first = snapshot.Candidates[0];
        var other = new CatalogUsbInterfaceCandidate("usb-interface:" + new string('c', 64), first.Family, "other-private", first.PnpNodeKey, "Other");
        own.DeviceId = other.Id;
        Check(RuntimeUsbTestService.IsOwned(request.Intent, new([.. snapshot.Candidates, other], []), [own]), "Another function on the same parent is busy.");
        other = new(other.Id, other.Family, other.InterfacePath, "usb-pnp:" + new string('d', 64), other.DisplayName);
        Check(!RuntimeUsbTestService.IsOwned(request.Intent, new([.. snapshot.Candidates, other], []), [own]), "A proven different physical owner may continue running.");
        Check(RuntimeUsbTestService.IsOwned(request.Intent, snapshot, [own]), "Unresolved existing ownership is not permission to open.");
        var handle = new CatalogUsbTestHandle(request.ClientId, request.Intent.AttemptId);
        RuntimeIpcResponse Reply(CatalogUsbTestHandle h, RuntimeUsbTestStatus status) => new(true, JsonSerializer.Serialize(new CatalogUsbTestReply(h, status)));
        Check(!EditorUsbTestClient.TryRead(Reply(handle with { ClientId = Guid.NewGuid() }, new(RuntimeUsbTestPhase.Running)), handle, out _), "Wrong client cannot supply a result.");
        Check(!EditorUsbTestClient.TryRead(Reply(handle, new(RuntimeUsbTestPhase.Running, CatalogUsbTestTechnicalOutcome.SucceededAndReleased)), handle, out _), "Phase and terminal outcome must agree.");
        RuntimeStartupOptions.Validate(["--usb-test-only"]);
        foreach (var args in new[] { new[] { "--usb-test-only", "extra" }, new[] { "--USB-TEST-ONLY" } })
        {
            try { RuntimeStartupOptions.Validate(args); throw new InvalidOperationException("Unexpected startup admission."); }
            catch (ArgumentException) { }
        }
    }

    public static async Task EditorLaunchAndStrictResults()
    {
        foreach (var mode in new[] { "alive", "absent", "unreachable", "lost-begin", "lost-status", "cancel", "rejected" })
        {
            var request = Request(); var snapshot = Snapshot(request.Intent);
            var draft = new CatalogUsbCandidateDraft(snapshot.Candidates[0], "User name", [request.Intent.Protocol])
            { Width = 320, Height = 240, Protocol = request.Intent.Protocol };
            var launches = 0; var begins = 0; var pings = 0; var statuses = 0; var messages = new List<string>();
            using var cancellation = new CancellationTokenSource();
            Task<RuntimeIpcResponse> Send(string line)
            {
                var command = RuntimeIpcProtocol.ParseCommand(line);
                if (command.Kind == RuntimeIpcCommandKind.Ping)
                { pings++; return Task.FromResult(new RuntimeIpcResponse(mode != "unreachable" && (mode != "absent" || pings > 1), "ping")); }
                if (command.Kind == RuntimeIpcCommandKind.UsbTestBegin)
                {
                    begins++;
                    if (mode == "cancel") cancellation.Cancel();
                    if (mode is "lost-begin" or "rejected")
                        return Task.FromResult(new RuntimeIpcResponse(false, "Test rejected") { ResponseReceived = mode == "rejected" });
                    var begun = command.UsbTestBegin!;
                    return Task.FromResult(new RuntimeIpcResponse(true, JsonSerializer.Serialize(new CatalogUsbTestReply(
                        new(begun.ClientId, begun.Intent.AttemptId), new(RuntimeUsbTestPhase.Running)))));
                }
                if (command.Kind == RuntimeIpcCommandKind.UsbTestStatus && ++statuses == 1 && mode == "lost-status")
                    return Task.FromResult(new RuntimeIpcResponse(false, "Synthetic timeout"));
                return Task.FromResult(new RuntimeIpcResponse(true, JsonSerializer.Serialize(new CatalogUsbTestReply(command.UsbTestHandle!,
                    new(RuntimeUsbTestPhase.SucceededAndReleased, CatalogUsbTestTechnicalOutcome.SucceededAndReleased)))));
            }
            var client = new EditorUsbTestClient(Send, () => mode == "unreachable", () => launches++, () => Task.CompletedTask);
            var intent = await client.TestAsync(draft, snapshot, messages.Add, cancellation.Token);
            Check(launches == (mode == "absent" ? 1 : 0) && begins == (mode == "unreachable" ? 0 : 1),
                "Only absent Runtime may launch, and Begin is never automatically resent.");
            Check((intent is not null) == (mode is "alive" or "absent") && !draft.CanSaveConfiguration && !draft.IsTesting,
                "Technical result alone cannot save; lost replies, rejection or cancellation cannot pass.");
            if (intent is not null)
            {
                Check(draft.ConfirmVisibleChangingSeconds(intent, true) && draft.CanSaveConfiguration, "Separate visual Yes unlocks only local eligibility.");
                draft.Name = "Renamed"; Check(draft.CanSaveConfiguration, "Rename preserves approval.");
                draft.Width++; Check(!draft.CanSaveConfiguration, "Geometry edit invalidates approval.");
            }
        }
    }

    private static CatalogUsbTestBeginRequest Request(string protocol = OutputTargetContract.LyBulkTransport) => new(Guid.NewGuid(),
        new(Guid.NewGuid(), "usb-interface:" + new string('a', 64), "usb-pnp:" + new string('b', 64), protocol, 320, 240, 0));
    private static CatalogUsbCandidateSnapshot Snapshot(CatalogUsbTrialIntent intent) => new([
        new(intent.InterfaceId, UsbDisplayCatalog.Find(new(0x0416, 0x5408))!, "synthetic-private", intent.PnpNodeKey, "Synthetic")], []);
    private static RuntimeIpcCommand Begin(CatalogUsbTestBeginRequest request) => new(RuntimeIpcCommandKind.UsbTestBegin, UsbTestBegin: request);
    private static RuntimeUsbTestStatus Status(RuntimeUsbTestService service, CatalogUsbTestBeginRequest request) =>
        JsonSerializer.Deserialize<CatalogUsbTestReply>(service.Execute(new(RuntimeIpcCommandKind.UsbTestStatus,
            UsbTestHandle: new(request.ClientId, request.Intent.AttemptId))).Message)!.Status;
    private static RuntimeUsbTestStatus Wait(RuntimeUsbTestService service, CatalogUsbTestBeginRequest request)
    {
        RuntimeUsbTestStatus? result = null;
        Check(SpinWait.SpinUntil(() => { result = Status(service, request); return result.TechnicalOutcome is not null || result.Phase == RuntimeUsbTestPhase.CleanupPending; }, 8000),
            "Transient worker must reach a terminal or explicitly retained-cleanup state.");
        return result!;
    }
    private static RuntimeUsbTestService Service(string root, CatalogUsbTestBeginRequest request, Frames frames, Action opened) => new(
        () => Snapshot(request.Intent), (_, _) => false, intent =>
        {
            var board = AdaptiveDashboardFactory.Create(intent.Width, intent.Height, 0, root);
            var metrics = new MetricStore(); metrics.Publish(SystemMetricContract.DateTime, new DateTime(2026, 1, 1));
            var clock = 0;
            return new FramePump(new AppConfig(), new OutputTargetConfig
            { Id = "test", Name = "Test", Kind = "physical", Transport = intent.Protocol, DeviceId = intent.InterfaceId,
                Width = intent.Width, Height = intent.Height, DeviceEnabled = true, RefreshIntervalMs = 10 }, board, metrics,
                new FileLogger(Path.Combine(root, "worker.log")), (_, _, _, _, _) => { opened(); return frames; },
                maximumFrames: RuntimeUsbTestService.FrameCount, frameClockNow: () => new DateTime(2026, 1, 1).AddSeconds(Interlocked.Increment(ref clock)), stopOnFailure: true);
        }, new FileLogger(Path.Combine(root, "service.log")));
    private sealed class Frames(string protocol) : IPhysicalFrameTransport
    {
        internal List<byte[]> Images { get; } = [];
        internal int Disposals, FailAt;
        internal bool FailCleanup, FailEncode;
        internal ManualResetEventSlim? Entered, Release;
        public int WireRotationDegrees => protocol == OutputTargetContract.LyBulkTransport ? 180 : 90;
        public PhysicalFrameProfile Profile => new(WireRotationDegrees, FailEncode ? 1 : 1_048_576);
        public void SendJpegWithDiagnostics(ReadOnlySpan<byte> jpeg, FrameRenderDiagnostics diagnostics)
        {
            Entered?.Set(); Release?.Wait();
            if (FailAt == Images.Count + 1) throw new IOException("Synthetic send failure.");
            Images.Add(jpeg.ToArray());
        }
        public void RecordLifecycle(string state, string reason) { }
        public void Dispose() { Disposals++; if (FailCleanup) throw new IOException("Synthetic cleanup failure."); }
    }
    private static void WithRoot(Action<string> action)
    {
        var root = Program.CreateTemporaryApplicationRoot("pinkie-live-test");
        File.WriteAllText(Path.Combine(root, "config", "app.json"), "untouched");
        try
        {
            action(root);
            Check(File.ReadAllText(Path.Combine(root, "config", "app.json")) == "untouched" &&
                Directory.GetFiles(root, "dashboard.json", SearchOption.AllDirectories).Length == 0,
                "Neither success, failure, cancellation nor uncertain cleanup may persist configuration or a dashboard.");
        }
        catch (Exception error)
        {
            var diagnostics = string.Join("\n", Directory.GetFiles(root, "*.log").Select(File.ReadAllText));
            throw new InvalidOperationException(error.Message + "\n" + diagnostics, error);
        }
        finally { Directory.Delete(root, true); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
