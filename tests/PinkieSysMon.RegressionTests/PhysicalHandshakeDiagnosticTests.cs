using System.ComponentModel;
using System.Text.Json;

namespace PinkieSysMon.RegressionTests;

internal static class PhysicalHandshakeDiagnosticTests
{
    public static void CommonContextAndBounds()
    {
        foreach (var kind in new[] { OutputTargetContract.LyBulkTransport, OutputTargetContract.EncryptedJpegTransport })
        {
            var trace = new PhysicalHandshakeDiagnostics();
            var bytes = Enumerable.Range(0, 600).Select(i => (byte)i).ToArray();
            trace.Begin("handshake-read", "read", 0x81, bytes.Length);
            trace.CompleteRead(bytes, bytes.Length);
            for (var i = 1; i < 10; i++) trace.Begin("pending-read", "read", 0x81, 512);
            trace.Stage("validation");
            var target = Target(kind); target.Name = "name\n\"injected\"";
            var error = new TimeoutException("timeout", new Win32Exception(121));
            var line = trace.FormatFailure(target, 1700, error);
            using var json = Parse(line);
            var root = json.RootElement;
            Check(!line.Contains('\n') && root.GetProperty("targetName").GetString() == target.Name, "User metadata must remain escaped in one log entry.");
            Check(root.GetProperty("runtime").GetString() == RuntimeVersion.Current && root.GetProperty("kind").GetString() == OutputTargetContract.PhysicalKind &&
                root.GetProperty("transport").GetString() == target.Transport && root.GetProperty("timeoutMs").GetInt32() == 1700 &&
                root.GetProperty("stage").GetString() == "validation" && root.GetProperty("elapsedMs").GetDouble() >= 0 &&
                root.GetProperty("attemptId").GetString()!.Length == 32, "Both adapters must receive the same attempt context.");
            var operations = root.GetProperty("operations");
            Check(operations.GetArrayLength() == 8 && root.GetProperty("omittedOperations").GetInt32() == 2, "Operation retention must be bounded.");
            Check(operations[0].GetProperty("ActualBytes").GetInt32() == 600 && operations[0].GetProperty("ResponseTruncated").GetBoolean() &&
                operations[0].GetProperty("ResponseHex").GetString() == Convert.ToHexString(bytes.AsSpan(0, 512)), "Log actual count and explicitly truncated returned bytes.");
            Check(operations[1].GetProperty("ActualBytes").ValueKind == JsonValueKind.Null &&
                operations[1].GetProperty("ResponseHex").ValueKind == JsonValueKind.Null, "An unfinished read must not invent a zero-length completion.");
            Check(root.GetProperty("errors")[1].GetProperty("nativeCode").GetInt32() == 121 &&
                root.GetProperty("errors")[0].GetProperty("hResult").GetString() == $"0x{error.HResult:X8}", "Preserve nested native error and HResult.");
        }
    }

    public static void ObserverPreservesErrorsAndQuietPaths()
    {
        var reports = new List<string>();
        var target = Target(OutputTargetContract.LyBulkTransport);
        Check(PhysicalHandshakeDiagnostics.Observe<object?>(target, 1000, default, _ => null, (message, _) => reports.Add(message)) is null && reports.Count == 0, "Missing binding is quiet.");
        Check(PhysicalHandshakeDiagnostics.Observe(target, 1000, default, _ => 42, (message, _) => reports.Add(message)) == 42 && reports.Count == 0, "Healthy opening is quiet.");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        var cancellation = new OperationCanceledException(canceled.Token);
        Check(ReferenceEquals(Capture(() => PhysicalHandshakeDiagnostics.Observe<object?>(target, 1000, canceled.Token, _ => throw cancellation, (message, _) => reports.Add(message))), cancellation) && reports.Count == 0, "Lifecycle cancellation propagates without failure diagnostics.");
        var error = new IOException("original I/O error");
        var calls = 0;
        Check(ReferenceEquals(Capture(() => PhysicalHandshakeDiagnostics.Observe<object?>(target, 1000, default, _ => { calls++; throw error; }, (_, _) => throw new IOException("broken sink"))), error) && calls == 1, "A broken logger must neither replace the original error nor retry acquisition.");
        var unexpectedCancellation = new OperationCanceledException();
        Check(ReferenceEquals(Capture(() => PhysicalHandshakeDiagnostics.Observe<object?>(target, 1000, default, _ => throw unexpectedCancellation, (message, _) => reports.Add(message))), unexpectedCancellation) && reports.Count == 1, "Unrequested cancellation remains a failure.");
    }

    public static void TrofeoHandshakeReplies()
    {
        foreach (var size in new[] { 0, 12, 22, 23, 512 })
        {
            var trace = new PhysicalHandshakeDiagnostics();
            var reply = new byte[size];
            var writes = 0; var reads = 0;
            var error = Capture(() => TrofeoTransport.Handshake(packet =>
            {
                writes++;
                Check(packet.Length == 2048 && packet[0] == 2 && packet[1] == 255 && packet[8] == 1 &&
                    packet.Where((_, i) => i is not (0 or 1 or 8)).All(value => value == 0), "Trofeo handshake packet remains exact.");
            }, requested => { reads++; Check(requested == 512, "Trofeo keeps its read size."); return reply; }, trace));
            Check(error is InvalidDataException && writes == 1 && reads == 1, "Malformed Trofeo replies retain validation and single exchange.");
            using var json = Parse(trace.FormatFailure(Target(OutputTargetContract.LyBulkTransport), 1000, error));
            var operation = json.RootElement.GetProperty("operations")[1];
            Check(operation.GetProperty("ActualBytes").GetInt32() == size && operation.GetProperty("ResponseHex").GetString() == Convert.ToHexString(reply) &&
                json.RootElement.GetProperty("stage").GetString() == "handshake-validation", "Capture only actual Trofeo reply bytes, including completed empty reads.");
        }
        var valid = new byte[23]; valid[0] = 3; valid[1] = 255; valid[8] = 1;
        Check(ReferenceEquals(TrofeoTransport.Handshake(_ => { }, _ => valid), valid), "Existing minimum accepted Trofeo reply remains accepted.");
    }

    public static void TrofeoHandshakeIoErrors()
    {
        foreach (var failWrite in new[] { true, false })
        {
            var trace = new PhysicalHandshakeDiagnostics(); var reads = 0;
            var error = new Win32Exception(31);
            Check(ReferenceEquals(Capture(() => TrofeoTransport.Handshake(_ => { if (failWrite) throw error; }, _ => { reads++; throw error; }, trace)), error) && reads == (failWrite ? 0 : 1), "Trofeo I/O errors retain identity and ordering.");
            using var json = Parse(trace.FormatFailure(Target(OutputTargetContract.LyBulkTransport), 1000, error));
            var operations = json.RootElement.GetProperty("operations");
            var pending = operations[operations.GetArrayLength() - 1];
            Check(pending.GetProperty("ActualBytes").ValueKind == JsonValueKind.Null && pending.GetProperty("ResponseHex").ValueKind == JsonValueKind.Null, "No Trofeo completion means unknown count and no fabricated bytes.");
        }
    }

    public static void TurzxShortReplies()
    {
        foreach (var size in new[] { 0, 12, 24, 41, 511, 512 })
        {
            var trace = new PhysicalHandshakeDiagnostics();
            var reply = Enumerable.Repeat((byte)0x42, size).ToArray();
            using var usb = new Connection(reply);
            using var transport = New(usb);
            var error = Capture(() => transport.Synchronize(diagnostics: trace));
            Check(error is InvalidDataException && usb.Writes == 1 && usb.Reads == 1, "Malformed TURZX primary replies keep strict validation without a follow-up or retry.");
            using var json = Parse(trace.FormatFailure(Target(OutputTargetContract.EncryptedJpegTransport), 1000, error));
            var operation = json.RootElement.GetProperty("operations")[1];
            Check(operation.GetProperty("ActualBytes").GetInt32() == size && operation.GetProperty("ResponseHex").GetString() == Convert.ToHexString(reply) &&
                json.RootElement.GetProperty("stage").GetString() == "sync-primary-validation", "Short primary replies retain exact bytes and stage.");
        }
    }

    public static void TurzxFollowUpAndUnknownCounts()
    {
        foreach (var failure in new[] { "nonempty", "timeout", "invalid-count", "short-write" })
        {
            var trace = new PhysicalHandshakeDiagnostics();
            using var usb = new Connection(Reply(), [0x77, 0x88]) { Mode = failure };
            using var transport = New(usb);
            var error = Capture(() => transport.Synchronize(diagnostics: trace));
            Check(usb.Writes == 1 && usb.Reads == (failure == "short-write" ? 0 : 2), "Diagnostics must not add I/O or retry uncertain boundaries.");
            using var json = Parse(trace.FormatFailure(Target(OutputTargetContract.EncryptedJpegTransport), 1000, error));
            var operations = json.RootElement.GetProperty("operations");
            if (failure == "short-write")
            {
                Check(operations[0].GetProperty("ActualBytes").GetInt32() == 511, "Short write retains its returned count.");
                continue;
            }
            Check(operations[1].GetProperty("ResponseHex").GetString() == Convert.ToHexString(Reply()), "Primary reply remains separate from follow-up evidence.");
            var followUp = operations[2];
            Check(followUp.GetProperty("ResponseHex").GetString() == (failure == "nonempty" ? "7788" : null), "Failed follow-up must not copy stale primary buffer bytes.");
            Check(failure == "timeout" ? followUp.GetProperty("ActualBytes").ValueKind == JsonValueKind.Null :
                followUp.GetProperty("ActualBytes").GetInt32() == (failure == "invalid-count" ? 513 : 2), "Unknown and invalid completion counts stay distinguishable.");
        }
    }

    public static void HealthySyncDoesNotCaptureFrames()
    {
        var trace = new PhysicalHandshakeDiagnostics();
        using var usb = new Connection(Reply(), [], Reply(), []);
        using var transport = New(usb);
        transport.Synchronize(diagnostics: trace);
        var target = Target(OutputTargetContract.EncryptedJpegTransport);
        var marker = new IOException("fixture");
        using var before = Parse(trace.FormatFailure(target, 1000, marker));
        transport.SendImage(TurzxImageFormat.Jpeg, new byte[] { 255, 216, 255, 0, 255, 217 });
        using var after = Parse(trace.FormatFailure(target, 1000, marker));
        Check(usb.Writes == 2 && usb.Reads == 4 && before.RootElement.GetProperty("operations").GetRawText() == after.RootElement.GetProperty("operations").GetRawText(), "Frame traffic cannot enter the handshake recorder.");
        Check(after.RootElement.GetProperty("operations")[2].GetProperty("ActualBytes").GetInt32() == 0 &&
            after.RootElement.GetProperty("operations")[2].GetProperty("ResponseHex").GetString() == "", "A genuinely completed empty follow-up is explicit.");
    }

    public static void FactoryLogsBeforeHardwareAdmission()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pinkie-handshake-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "runtime.log");
        var log = new FileLogger(path);
        var target = Target(OutputTargetContract.EncryptedJpegTransport);
        target.DeviceEnabled = false;
        Check(Capture(() => PhysicalFrameTransportFactory.TryOpen(target, 1000, log, default)) is InvalidOperationException && !File.Exists(path), "Disabled Output is rejected before diagnostics and I/O.");
        target.DeviceEnabled = true;
        target.EncryptedJpeg = new() { BrightnessPercent = 0 };
        Check(Capture(() => PhysicalFrameTransportFactory.TryOpen(target, 1000, log, default)) is NotSupportedException, "Unsupported brightness sequencing remains rejected before native discovery.");
        var lines = File.ReadAllLines(path);
        var errors = File.ReadAllLines(log.ErrorsPath);
        // The production observer now adds one acquisition milestone and a
        // correlated PHYSICAL_IO context, without another full exception dump.
        var session = lines.Select(line =>
        {
            using var value = JsonDocument.Parse(line[line.IndexOf('{')..]);
            return value.RootElement.Clone();
        }).ToArray();
        var failures = errors.Select(line =>
        {
            using var value = JsonDocument.Parse(line[line.IndexOf('{')..]);
            return value.RootElement.Clone();
        }).ToArray();
        var original = failures.Single(r => r.GetProperty("exception").ValueKind == JsonValueKind.String);
        var id = original.GetProperty("eventId").GetString();
        Check(lines.Length >= 3 && errors.Length >= 2 &&
              session.Count(r => r.GetProperty("eventId").GetString() == id) == 2 &&
              failures.Count(r => r.GetProperty("eventId").GetString() == id) == 2 &&
              failures.All(r => r.GetProperty("message").GetString()!.StartsWith("PHYSICAL_IO ", StringComparison.Ordinal) ||
                  r.GetProperty("message").GetString()!.StartsWith("PHYSICAL_OPEN_FAILURE ", StringComparison.Ordinal)) &&
              !File.ReadAllText(path).Contains("PHYSICAL_OPEN_FAILURE", StringComparison.Ordinal) &&
              !File.ReadAllText(path).Contains("Unsupported", StringComparison.Ordinal),
              "Acquisition context and references must preserve one full exception only in Errors.");
        using var json = Parse(original.GetProperty("message").GetString()!);
        Check(json.RootElement.GetProperty("stage").GetString() == "options" && json.RootElement.GetProperty("operations").GetArrayLength() == 0, "A pre-I/O failure must not fabricate a handshake.");
        // Leave fixture logs available if this test fails; never touch application logs.
    }

    private static OutputTargetConfig Target(string kind) => new()
    { Width = 1920, Height = 720, DeviceId = "usb-interface:29036e174e6a0c5fa63fbb611a35df503a42793c6f82c1e28d2bfc0b82f5801d",
        Id = "fixture-output", Name = "Fixture", Kind = OutputTargetContract.PhysicalKind, DeviceEnabled = true,
        Transport = kind
    };
    private static byte[] Reply() { var bytes = new byte[512]; bytes[1] = 0xC8; return bytes; }
    private static TurzxTransport New(Connection usb) => new(usb, () => 1, _ => { });
    private static JsonDocument Parse(string line) => JsonDocument.Parse(line["PHYSICAL_OPEN_FAILURE ".Length..]);
    private static Exception Capture(Action action) { try { action(); } catch (Exception error) { return error; } throw new InvalidOperationException("Expected failure."); }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Connection(params byte[][] replies) : ITurzxUsbConnection
    {
        public int Writes { get; private set; }
        public int Reads { get; private set; }
        public string Mode { get; init; } = "";
        public int Write(byte[] packet, CancellationToken token) { Writes++; return packet.Length - (Mode == "short-write" ? 1 : 0); }
        public int Read(byte[] buffer, CancellationToken token)
        {
            Reads++;
            if (Reads == 2 && Mode == "timeout") throw new TimeoutException("synthetic", new Win32Exception(121));
            if (Reads == 2 && Mode == "invalid-count") return 513;
            var reply = replies[Reads - 1]; reply.CopyTo(buffer, 0); return reply.Length;
        }
        public void Dispose() { }
    }
}
