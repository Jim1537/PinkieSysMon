using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using PinkieSysMon;

namespace PinkieSysMon.RegressionTests;

internal static class Issue105UsbLoggingTests
{
    private static readonly byte[] Jpeg = [255, 216, 255, 0, 255, 217];

    public static void BoundedEvidenceAndUnknownCounts() => Sandbox((root, log) =>
    {
        var clock = new Clock();
        using var trace = new PhysicalIoDiagnostics(Target(), 500, log, default, clock, monitor: false);
        trace.Begin("frame", 1234, new(2, 3));
        var reply = Enumerable.Repeat((byte)0x42, 600).ToArray();
        trace.IoBegin("primary", true, 0x81, reply.Length);
        trace.Reply(reply, 600);
        for (var i = 0; i < 30; i++)
        {
            trace.IoBegin("write", false, 0x01, 512);
            trace.IoResult(true, 512, 0, "terminal");
        }
        trace.IoBegin("follow-up", true, 0x81, 512);
        trace.IoResult(false, 0, 31, "terminal");
        var error = new Win32Exception(31, "fixture native failure");
        trace.Fail(error);
        log.Error("worker propagated", error);
        var errors = Read(log.ErrorsPath);
        var evidence = Context(errors).Single();
        Check(evidence.GetProperty("recent").GetArrayLength() == PhysicalIoDiagnostics.RecentOperationLimit &&
              evidence.GetProperty("omittedIoOperations").GetInt64() > 0,
            "Recent native-operation retention must be bounded with explicit omission accounting.");
        var last = evidence.GetProperty("recent").EnumerateArray().Last();
        Check(last.GetProperty("ActualBytes").ValueKind == JsonValueKind.Null &&
              last.GetProperty("NativeCode").GetInt32() == 31 &&
              last.GetProperty("responseHex").ValueKind == JsonValueKind.Null,
            "Failed completion must retain its native code without fabricating a zero-byte success or stale response.");
        Check(evidence.GetProperty("lastNonemptyReply").GetProperty("responseHex").GetString()!.Length == 1024 &&
              evidence.GetProperty("payloadBytes").GetInt32() == 1234,
            "Bounded last returned control bytes must survive eviction without retaining image payload.");
        Check(errors.Count(e => e.GetProperty("exception").ValueKind == JsonValueKind.String) == 1 &&
              !File.ReadAllText(log.Path).Contains("fixture native failure", StringComparison.Ordinal),
            "Worker propagation must preserve one original exception and Errors-only failure text.");
    });

    public static void HealthyStreamIsSparseAndExact() => Sandbox((root, log) =>
    {
        var clock = new Clock();
        var connection = new Replies([512, 0, 512, 0, 512, 0]);
        using (var observed = OpenStream(log, clock, connection))
        {
            observed.SendJpegWithDiagnostics(Jpeg, new(double.NaN, 3));
            observed.SendJpegWithDiagnostics(Jpeg, default);
            clock.Advance(31_000);
            connection.Trace!.Poll();
            var contexts = Context(Read(log.Path));
            Check(contexts.Count(c => c.GetProperty("operation").GetString() == "frame" &&
                    c.GetProperty("outcome").GetString() == "completed") == 1,
                "Only the first healthy frame, rather than every frame, needs a successful frame record.");
            var summary = contexts.Last();
            Check(summary.GetProperty("outcome").GetString() == "progress-snapshot" &&
                  summary.GetProperty("framesCompleted").GetInt64() == 2,
                "A sampled progress summary must retain actual completed work without inventing activity.");
            Check(connection.Writes == 3 && connection.Reads == 6 &&
                  observed.WireRotationDegrees == 90 && observed.Profile == PhysicalFrameProfile.EncryptedJpeg &&
                  !File.Exists(log.ErrorsPath),
                "Observation must preserve exact sync/two-frame calls, profile and healthy error isolation.");
        }
        Check(connection.Disposals == 1, "The observed transport must preserve single-owner disposal.");
    });

    public static void MalformedFrameBoundariesRetained() => Sandbox((root, log) =>
    {
        foreach (var kind in new[] { "empty", "short", "unknown", "follow-up", "short-write", "native" })
        {
            var clock = new Clock();
            var connection = new Replies(kind == "follow-up" ? [512, 0, 512, 3] :
                kind == "empty" ? [512, 0, 0] : kind == "short" ? [512, 0, 7] : [512, 0, 512])
                { FrameFault = kind };
            using var observed = OpenStream(log, clock, connection);
            var error = Capture(() => observed.SendJpegWithDiagnostics(Jpeg, default));
            Check(error is IOException or InvalidDataException or Win32Exception, "An invalid admitted exchange must retain its established failure.");
            var calls = (connection.Writes, connection.Reads);
            _ = Capture(() => observed.SendJpegWithDiagnostics(Jpeg, default));
            Check((connection.Writes, connection.Reads) == calls,
                "Diagnostics must not retry or permit a command after a faulted reply boundary.");
            var evidence = Context(Read(log.ErrorsPath)).Last(c =>
                c.GetProperty("operation").GetString() == "frame" &&
                c.GetProperty("outcome").GetString() == "failed" &&
                c.GetProperty("recent").EnumerateArray().Any(r => r.GetProperty("responseHex").ValueKind == JsonValueKind.String));
            Check(evidence.GetProperty("protocol").GetString() == OutputTargetContract.EncryptedJpegTransport &&
                  evidence.GetProperty("configuredTimeoutMs").GetInt32() == 500 &&
                  evidence.GetProperty("connectionId").GetString()!.Length == 32,
                "Malformed exchange evidence must carry target/protocol/connection/timing context.");
            if (kind == "follow-up")
            {
                var last = evidence.GetProperty("recent").EnumerateArray().Last();
                Check(last.GetProperty("ActualBytes").GetUInt32() == 3 &&
                      last.GetProperty("responseHex").GetString() == "424242",
                    "A nonempty follow-up must retain exactly the returned bytes, excluding stale buffer tail.");
            }
        }
    });

    public static void PendingCancellationKeepsNativeOwnership()
    {
        foreach (var cancelThrows in new[] { false, true })
            CancellationCase(cancelThrows, nativeError: 995);
    }

    public static void NativeFailureDuringCancellationIsRetained() =>
        CancellationCase(cancelThrows: false, nativeError: 31);

    private static void CancellationCase(bool cancelThrows, int nativeError) => Sandbox((root, log) =>
    {
        var clock = new Clock();
        using var cancellation = new CancellationTokenSource();
        var native = new PendingNative(cancelThrows, nativeError);
        PhysicalIoDiagnostics? trace = null;
        using var observed = ObservedPhysicalFrameTransport.Open(Target(), 500, log, cancellation.Token, evidence =>
        {
            trace = evidence;
            return new NativeAdapter(new TurzxUsbConnection(native, 500, evidence), evidence, cancellation.Token);
        }, clock, monitor: false)!;
        var send = Task.Run(() => Capture(() => observed.SendJpegWithDiagnostics(Jpeg, default)));
        try
        {
            Check(native.Submitted.Wait(5000), "The fake native operation must be admitted.");
            cancellation.Cancel();
            Check(native.Completing.Wait(5000), "Cancellation must enter terminal completion draining.");
            clock.Advance(1000);
            trace!.Poll();
            var pending = Context(Read(log.ErrorsPath)).Last();
            Check(pending.GetProperty("outcome").GetString() == "pending-observed" &&
                  pending.GetProperty("activeAtObservation").GetBoolean() &&
                  pending.GetProperty("cancellationRequested").GetBoolean() &&
                  pending.GetProperty("recent").EnumerateArray().Last()
                    .GetProperty("ActualBytes").ValueKind == JsonValueKind.Null &&
                  !send.IsCompleted && native.Disposals == 0,
                "Pending evidence must be durable before terminal return without releasing the buffer/native owner.");
        }
        finally { native.Release.Set(); }
        Check(send.Wait(5000), "Releasing the fake kernel completion must finish the command.");
        var error = send.Result;
        Check(cancelThrows ? error is Win32Exception : error is OperationCanceledException,
            "Logging must preserve cancellation-error priority and the original exception type.");
        var errors = Context(Read(log.ErrorsPath));
        var terminal = errors.Last();
        Check(native.BufferReadable && native.CancelCalls == 1 && native.CompleteCalls == 1,
            "The actual production connection must drain once with its pinned buffer alive.");
        if (cancelThrows)
            Check(terminal.GetProperty("recent").EnumerateArray().Last()
                    .GetProperty("CancellationNativeCode").GetInt32() == 31,
                "A failed cancellation request must survive the subsequent native completion.");
        if (nativeError == 31)
            Check(terminal.GetProperty("unexpectedNativeFailure").GetBoolean() &&
                  terminal.GetProperty("recent").EnumerateArray().Last().GetProperty("NativeCode").GetInt32() == 31 &&
                  terminal.GetProperty("outcome").GetString() == "cancelled-after-transfer-return",
                "Caller cancellation must not hide a different observed terminal native failure.");
    });

    public static void SlowCompletionAndOpenFailure() => Sandbox((root, log) =>
    {
        var clock = new Clock();
        using (var trace = new PhysicalIoDiagnostics(Target(), 500, log, default, clock, monitor: false))
        {
            trace.Begin("frame");
            trace.IoBegin("write", false, 0x01, 512);
            clock.Advance(800); // Completion between timer samples must still retain the slow observation.
            trace.IoResult(true, 512, 0, "terminal");
            trace.Complete("completed");
        }
        var slow = Context(Read(log.ErrorsPath)).Single();
        Check(slow.GetProperty("outcome").GetString() == "completed" &&
              slow.GetProperty("maximumPhaseMs").GetDouble() >= 800 &&
              !slow.GetProperty("activeAtObservation").GetBoolean(),
            "A slow returned transfer must be recorded without falsely declaring it still pending.");
        var original = new Win32Exception(121, "fixture open timeout");
        var calls = 0;
        var propagated = Capture(() => ObservedPhysicalFrameTransport.Open(Target(), 500, log, default, evidence =>
        {
            calls++;
            evidence.Stage("native.initialize");
            throw original;
        }, clock, monitor: false));
        Check(ReferenceEquals(propagated, original) && calls == 1 &&
              Context(Read(log.ErrorsPath)).Last().GetProperty("phase").GetString() == "native.initialize",
            "Failed acquisition must keep original identity, reached phase and single-attempt behavior.");
    });

    public static void ObserverFailurePreservesNativeCompletion() => Sandbox((root, log) =>
    {
        var clock = new FailingClock();
        using var trace = new PhysicalIoDiagnostics(Target(), 500, log, default, clock, monitor: false);
        trace.Begin("frame");
        clock.FailTimestamp = true;
        trace.IoBegin("native.write", false, 0x01, 512);
        using var completed = new ManualResetEvent(false);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var drained = 0;
        var cancelled = TrofeoOverlappedCompletionWait.Wait(completed, cancellation.Token,
            () => trace.Stage("native.cancel"),
            () => { trace.Stage("native.cancel.drain"); drained++; });
        Check(cancelled && drained == 1 && trace.CaptureFailures > 0,
            "A failing diagnostic clock must not skip the existing mandatory terminal drain.");
        var original = new Win32Exception(31, "original native failure with broken observer");
        trace.Fail(original);
        var errors = Read(log.ErrorsPath);
        Check(errors.Length == 1 &&
              errors[0].GetProperty("exception").GetString()!.Contains(original.Message) &&
              errors[0].GetProperty("message").GetString()!.Contains("CONTEXT_UNAVAILABLE"),
            "Unavailable diagnostic context must not replace or prevent retention of the original error.");
    });

    private sealed class FailingClock : TimeProvider
    {
        public bool FailTimestamp;
        public override long GetTimestamp() => FailTimestamp
            ? throw new InvalidOperationException("Injected observer clock failure.") : 0;
    }

    private static IPhysicalFrameTransport OpenStream(FileLogger log, Clock clock, Replies connection) =>
        ObservedPhysicalFrameTransport.Open(Target(), 500, log, default, trace =>
        {
            connection.Trace = trace;
            var transport = new TurzxTransport(connection, () => 0, _ => { }, ioDiagnostics: trace);
            transport.Synchronize();
            return new StreamAdapter(transport);
        }, clock, monitor: false)!;

    private sealed class StreamAdapter(TurzxTransport transport) : IPhysicalFrameTransport
    {
        public int WireRotationDegrees => 90;
        public PhysicalFrameProfile Profile => PhysicalFrameProfile.EncryptedJpeg;
        public void SendJpegWithDiagnostics(ReadOnlySpan<byte> jpeg, FrameRenderDiagnostics render) =>
            transport.SendImage(TurzxImageFormat.Jpeg, jpeg);
        public void RecordLifecycle(string state, string reason) { }
        public void Dispose() => transport.Dispose();
    }

    private sealed class NativeAdapter(TurzxUsbConnection connection, PhysicalIoDiagnostics trace, CancellationToken token) : IPhysicalFrameTransport
    {
        public int WireRotationDegrees => 90;
        public void SendJpegWithDiagnostics(ReadOnlySpan<byte> jpeg, FrameRenderDiagnostics render)
        {
            trace.IoBegin("write", false, 0x01, jpeg.Length);
            connection.Write(jpeg.ToArray(), token);
        }
        public void RecordLifecycle(string state, string reason) { }
        public void Dispose() => connection.Dispose();
    }

    private sealed class Replies(int[] lengths) : ITurzxUsbConnection
    {
        private readonly Queue<int> _lengths = new(lengths);
        public PhysicalIoDiagnostics? Trace;
        public string? FrameFault;
        public int Writes, Reads, Disposals;
        public int Write(byte[] packet, CancellationToken token)
        {
            Writes++;
            return FrameFault == "short-write" && Writes > 1 ? packet.Length - 1 : packet.Length;
        }
        public int Read(byte[] buffer, CancellationToken token)
        {
            Reads++;
            if (FrameFault == "native" && Writes > 1) throw new Win32Exception(31, "fixture frame read failed");
            var length = _lengths.Dequeue();
            Array.Fill(buffer, (byte)0x42);
            if (length == 512 && !(FrameFault == "unknown" && Writes > 1)) buffer[1] = 0xC8;
            return length;
        }
        public void Dispose() => Disposals++;
    }

    private sealed class PendingNative(bool cancelThrows, int nativeError) : ITurzxNativeIo
    {
        public readonly ManualResetEventSlim Submitted = new(), Completing = new(), Release = new();
        private IntPtr _buffer;
        public int CancelCalls, CompleteCalls, Disposals;
        public bool BufferReadable;
        public bool Submit(bool read, byte pipe, IntPtr buffer, uint length, IntPtr overlapped, out int error)
        { _buffer = buffer; error = 997; Submitted.Set(); return false; }
        public void Cancel(IntPtr overlapped)
        {
            CancelCalls++;
            if (cancelThrows) throw new Win32Exception(31, "fixture cancel failed");
        }
        public bool Complete(IntPtr overlapped, out uint actual, out int error)
        {
            CompleteCalls++; Completing.Set();
            if (!Release.Wait(5000)) throw new TimeoutException("Fake completion was not released.");
            BufferReadable = Marshal.ReadByte(_buffer) == Jpeg[0] &&
                Marshal.PtrToStructure<TurzxNativeOverlapped>(overlapped).EventHandle != IntPtr.Zero;
            actual = 0; error = nativeError; return false;
        }
        public void Dispose()
        {
            Disposals++;
            Submitted.Dispose(); Completing.Dispose(); Release.Dispose();
        }
    }

    private sealed class Clock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(GetTimestamp());
        public void Advance(int milliseconds) => Interlocked.Add(ref _ticks, TimeSpan.FromMilliseconds(milliseconds).Ticks);
    }

    private static OutputTargetConfig Target() => new()
    { Id = "fixture-output", Name = "Synthetic display", Kind = OutputTargetContract.PhysicalKind,
      Transport = OutputTargetContract.EncryptedJpegTransport, DeviceEnabled = true };

    private static Exception Capture(Action action)
    {
        try { action(); }
        catch (Exception error) { return error; }
        throw new InvalidOperationException("Expected the existing operation to fail.");
    }

    private static JsonElement[] Read(string path) => File.ReadAllLines(path).Select(line =>
    {
        using var json = JsonDocument.Parse(line[line.IndexOf('{')..]);
        return json.RootElement.Clone();
    }).ToArray();

    private static JsonElement[] Context(IEnumerable<JsonElement> records) => records
        .Select(record => record.GetProperty("message").GetString()!)
        .Where(message => message.StartsWith("PHYSICAL_IO ", StringComparison.Ordinal))
        .Select(message =>
        {
            using var json = JsonDocument.Parse(message["PHYSICAL_IO ".Length..]);
            return json.RootElement.Clone();
        }).ToArray();

    private static void Sandbox(Action<string, FileLogger> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkie-usb-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { test(root, new FileLogger(Path.Combine(root, "PinkieSysMon.Runtime.log"), "Runtime")); }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
