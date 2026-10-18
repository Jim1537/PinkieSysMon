using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace PinkieSysMon.RegressionTests;

internal static class Issue71TransportTests
{
    private const uint Timestamp = 12_345_678;
    private static readonly byte[] Png = [137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3, 4];
    private static readonly byte[] Jpeg = [255, 216, 255, 0, 255, 217];

    public static void PacketFixtures()
    {
        // Independently calculated with Python/PyCryptodome from the recovered
        // probe's wire facts, fixed timestamp and synthetic payload lengths.
        Digest(TurzxWireProtocol.Sync(Timestamp), "D214D3F0E60D33454C2E344A09CA077EB2C4D66D69CA2BBBCB9E4FAADC02C2DB");
        Digest(TurzxWireProtocol.Brightness(102, Timestamp), "A683A898B7BC8C278F1A90722CF36FA1BFEC349D555E6334C709F49732F87027");
        foreach (var (format, image, hash) in new[]
        {
            (TurzxImageFormat.Png, Png, "E392FAD5A7F12BDDC9C1EED535401ECB31553B2C72AB87EE615FF36FB671F7D7"),
            (TurzxImageFormat.Jpeg, Jpeg, "0497A29E53752E84FAE829E52EF9BCC81D313233CF10B103C426FAD3ABECFE78")
        })
        {
            var packet = TurzxWireProtocol.Image(format, image, Timestamp);
            Digest(packet.AsSpan(0, 512), hash);
            Check(packet.Length == 512 + image.Length && packet.AsSpan(512).SequenceEqual(image), "Image bytes must follow the exact control packet without padding or mutation.");
            Check(packet.AsSpan(504, 6).SequenceEqual(new byte[6]) && packet[510] == 0xA1 && packet[511] == 0x1A, "Control trailer and reserved bytes must retain exact positions.");
        }
    }

    public static void PacketBounds()
    {
        Throws<ArgumentOutOfRangeException>(() => TurzxWireProtocol.Sync(86_400_000));
        _ = TurzxWireProtocol.Sync(86_399_999);
        _ = TurzxWireProtocol.Brightness(0, 0);
        Throws<ArgumentOutOfRangeException>(() => TurzxWireProtocol.Brightness(-1, 0));
        Throws<ArgumentOutOfRangeException>(() => TurzxWireProtocol.Brightness(103, 0));
        Throws<ArgumentOutOfRangeException>(() => TurzxWireProtocol.Image((TurzxImageFormat)100, Png, 0));
        Throws<ArgumentOutOfRangeException>(() => TurzxWireProtocol.Image(TurzxImageFormat.Png, [], 0));
        Throws<InvalidDataException>(() => TurzxWireProtocol.Image(TurzxImageFormat.Jpeg, Png, 0));
        Throws<InvalidDataException>(() => TurzxWireProtocol.Image(TurzxImageFormat.Png, Jpeg, 0));
        var maximum = new byte[TurzxWireProtocol.MaximumImageBytes];
        Png.CopyTo(maximum, 0);
        Check(TurzxWireProtocol.Image(TurzxImageFormat.Png, maximum, 0).Length == maximum.Length + 512, "The software payload bound must be exact.");
        Throws<ArgumentOutOfRangeException>(() => TurzxWireProtocol.Image(TurzxImageFormat.Png, new byte[maximum.Length + 1], 0));
    }

    public static void CommandReplies()
    {
        foreach (var command in new byte[] { 10, 14, 101, 102 })
        {
            foreach (var offset in new[] { 1, 8 })
            {
                var reply = new byte[512]; reply[offset] = 0xC8;
                Check(TurzxWireProtocol.Interpret(command, reply) == new TurzxCommandCompletion(command, 512, TurzxReplyKind.StatusMarker), "Full observed marker shapes must remain distinguishable from physical display evidence.");
            }
            foreach (var size in new[] { 1, 2, 8, 9, 511, 513 })
            {
                var shortReply = new byte[size]; if (size > 1) shortReply[1] = 0xC8;
                Throws<InvalidDataException>(() => TurzxWireProtocol.Interpret(command, shortReply));
            }
            Throws<InvalidDataException>(() => TurzxWireProtocol.Interpret(command, new byte[512]));
            if (command != 14) Throws<InvalidDataException>(() => TurzxWireProtocol.Interpret(command, []));
        }
        Check(TurzxWireProtocol.Interpret(14, []).Reply == TurzxReplyKind.Empty, "Only brightness exposes a completed empty read, without inventing an ACK.");
        Throws<ArgumentOutOfRangeException>(() => TurzxWireProtocol.Interpret(125, Reply()));
    }

    public static void SequenceAndBrightnessBoundary()
    {
        var usb = new FakeConnection(Reply(), Reply(), []);
        var delayCalls = 0;
        using var transport = new TurzxTransport(usb, () => Timestamp, _ => delayCalls++, TurzxReplyMode.PrimaryOnly);
        transport.Synchronize();
        transport.SendImage(TurzxImageFormat.Png, Png);
        var result = transport.SetBrightnessPercent(100);
        Check(delayCalls == 1 && usb.Writes.Count == 3 && usb.Reads == 3, "Sync delay occurs once, followed by one write/read per admitted command.");
        Check(usb.Writes[0].SequenceEqual(TurzxWireProtocol.Sync(Timestamp)) &&
            usb.Writes[1].SequenceEqual(TurzxWireProtocol.Image(TurzxImageFormat.Png, Png, Timestamp)) &&
            usb.Writes[2].SequenceEqual(TurzxWireProtocol.Brightness(102, Timestamp)), "The transport must use its own image command and explicit brightness conversion.");
        Check(result == new TurzxCommandCompletion(14, 0, TurzxReplyKind.Empty), "An empty brightness read must be returned honestly.");
        Throws<InvalidOperationException>(() => transport.SendImage(TurzxImageFormat.Jpeg, Jpeg));
        Throws<InvalidOperationException>(() => transport.Synchronize());
        Check(usb.Writes.Count == 3, "An uncertain reply boundary forbids later frames, sync or automatic state-command replay.");
    }

    public static void AdmissionAndNoRetry()
    {
        using (var usb = new FakeConnection(Reply(), Reply(), Reply()))
        using (var transport = New(usb))
        {
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            Throws<OperationCanceledException>(() => transport.Synchronize(canceled.Token));
            Check(usb.Writes.Count == 0, "Canceled admission must not write.");
            transport.Synchronize();
            Throws<ArgumentOutOfRangeException>(() => transport.SetBrightnessPercent(101));
            Throws<InvalidDataException>(() => transport.SendImage(TurzxImageFormat.Jpeg, Png));
            transport.SendImage(TurzxImageFormat.Jpeg, Jpeg);
            Check(transport.SetBrightnessPercent(25).Reply == TurzxReplyKind.StatusMarker && usb.Writes.Count == 3, "Rejected arguments must not dirty a healthy connection; recognized brightness replies permit continued use.");
        }
        using (var usb = new FakeConnection(Reply()))
        using (var transport = New(usb))
        {
            Throws<InvalidOperationException>(() => transport.SendImage(TurzxImageFormat.Png, Png));
            Check(usb.Writes.Count == 0, "No image is admitted before sync.");
        }
        foreach (var failure in new Exception[] { new TimeoutException("synthetic timeout"), new Win32Exception(1167), new OperationCanceledException() })
        {
            using var usb = new FakeConnection(Reply()) { WriteFailure = failure };
            using var transport = New(usb);
            Check(ReferenceEquals(Capture(() => transport.Synchronize()), failure), "Write errors must propagate without reinterpretation.");
            Throws<InvalidOperationException>(() => transport.Synchronize());
            Check(usb.Writes.Count == 1 && usb.Reads == 0, "A failed write is never retried or followed by a read.");
        }
        using (var usb = new FakeConnection(Reply()) { ShortWrite = true })
        using (var transport = New(usb))
        {
            Throws<IOException>(() => transport.Synchronize());
            Check(usb.Writes.Count == 1 && usb.Reads == 0, "Short writes fail before any response read.");
        }
    }

    public static void ReadFailuresAndStaleBytes()
    {
        foreach (var reply in new[] { Array.Empty<byte>(), new byte[2], new byte[512] })
        {
            using var usb = new FakeConnection(Reply(), reply);
            using var transport = New(usb);
            transport.Synchronize();
            Throws<InvalidDataException>(() => transport.SendImage(TurzxImageFormat.Png, Png));
            Throws<InvalidOperationException>(() => transport.SendImage(TurzxImageFormat.Png, Png));
            Check(usb.Writes.Count == 2 && usb.Reads == 2, "A short/new response cannot inherit a stale marker or trigger an automatic resend.");
        }
        using var connection = new FakeConnection(Reply()) { ReadFailure = new Win32Exception(121) };
        using var failed = New(connection);
        Throws<Win32Exception>(() => failed.Synchronize());
        Check(connection.Writes.Count == 1 && connection.Reads == 1, "Native read failures must not become completed empty replies.");
    }

    public static void ExactOpenAndFailureCleanup()
    {
        var path = @"\\?\usb#vid_1cbe&pid_0123#SYNTHETIC#{11111111-2222-3333-4444-555555555555}";
        var device = new OutputDeviceDescriptor(TurzxDeviceDiscovery.CreateStableDeviceId(path), "turzx", "Synthetic", path);
        var target = new OutputTargetConfig { Id = "turzx-test", Name = "Synthetic", Kind = "turzx", Transport = "turzx", DeviceId = device.DeviceId, DeviceEnabled = true };
        var opens = 0;
        ITurzxUsbConnection Open(OutputDeviceDescriptor selected, int timeout) { opens++; Check(selected == device && timeout == 3000, "Only the exact selected device and effective timeout reach open."); return new FakeConnection(Reply(), []); }
        using (var transport = TurzxTransport.TryOpen(target, 3000, default, () => [device], Open, _ => { })) Check(transport is not null && opens == 1, "Exact binding must synchronize before returning an open transport.");
        Check(TurzxTransport.TryOpen(target, 3000, default, () => [], Open) is null && opens == 1, "Missing binding returns no transport without opening.");
        Throws<OutputDeviceResolutionException>(() => TurzxTransport.TryOpen(target, 3000, default, () => [device, device], Open));
        target.DeviceEnabled = false;
        Throws<InvalidOperationException>(() => TurzxTransport.TryOpen(target, 3000, default, () => [device], Open));
        target.DeviceEnabled = true; target.Kind = "trofeo";
        Throws<InvalidOperationException>(() => TurzxTransport.TryOpen(target, 3000, default, () => [device], Open));
        target.Kind = "turzx";
        Throws<InvalidDataException>(() => TurzxTransport.TryOpen(target, 3000, default, () => [device with { DevicePath = path.Replace("pid_0123", "pid_0124") }], Open));
        var failed = new FakeConnection(Array.Empty<byte>());
        Throws<InvalidDataException>(() => TurzxTransport.TryOpen(target, 3000, default, () => [device], (_, _) => failed));
        Check(opens == 1 && failed.Disposed, "Wrong/ambiguous/disabled bindings never open; failed sync releases the acquired connection.");
        var boundaryFailed = new FakeConnection(Reply(), Reply());
        Throws<InvalidDataException>(() => TurzxTransport.TryOpen(target, 3000, default, () => [device], (_, _) => boundaryFailed, _ => { }));
        Check(boundaryFailed.Disposed && boundaryFailed.Writes.Count == 1 && boundaryFailed.Reads == 2,
            "A failed sync follow-up must close the acquired native owner before any image is admitted.");
        Throws<ArgumentOutOfRangeException>(() => TurzxTransport.TryOpen(target, 3000, default, () => [device], Open,
            replyMode: (TurzxReplyMode)7));
        Check(opens == 1, "Invalid reply policy must fail before USB acquisition.");
    }

    public static void NativeCompletionAndErrors()
    {
        var native = new FakeNative { Bytes = 0 };
        using (var connection = new TurzxUsbConnection(native, 3000))
        {
            Check(connection.Read(new byte[512], default) == 0 && native.LastPipe == 0x81 && native.Completions == 1, "Successful zero-byte completion remains distinct from failure.");
            native.Bytes = 512;
            Check(connection.Write(new byte[512], default) == 512 && native.LastPipe == 0x01, "Completed writes use OUT 01 and the final native byte count.");
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            Throws<OperationCanceledException>(() => connection.Read(new byte[512], canceled.Token));
            Check(native.Submissions == 2, "Canceled native admission must not submit.");
        }
        Check(native.Disposed, "Native ownership ends with connection disposal.");
        foreach (var error in new[] { 121, 1167, 995 })
        {
            foreach (var immediate in new[] { true, false })
            {
                using var io = new FakeNative { SubmitError = immediate ? error : 0, CompletionError = immediate ? 0 : error };
                using var connection = new TurzxUsbConnection(io, 3000);
                var failure = Capture(() => connection.Read(new byte[512], default));
                Check(error == 121 ? failure is TimeoutException : failure is Win32Exception w && w.NativeErrorCode == error, "Native error identity must survive submit/completion and timeout mapping.");
                Check(io.Completions == (immediate ? 0 : 1), "Only accepted submissions require completion draining.");
            }
        }
        using var invalid = new TurzxUsbConnection(new FakeNative { Bytes = 513 }, 3000);
        Throws<IOException>(() => invalid.Read(new byte[512], default));
    }

    public static void NativeCancellationLifetime()
    {
        foreach (var cancellationFails in new[] { false, true })
        {
            var native = new FakeNative { Pending = true, BlockCompletion = true, CancelFailure = cancellationFails };
            using var connection = new TurzxUsbConnection(native, 3000);
            using var cancel = new CancellationTokenSource();
            var operation = Task.Run(() => Capture(() => connection.Write(new byte[512], cancel.Token)));
            Await(native.Submitted); cancel.Cancel(); Await(native.Completing);
            var disposal = Task.Run(connection.Dispose);
            try
            {
                GC.Collect(); GC.WaitForPendingFinalizers();
                Check(native.Cancellations == 1 && !native.Disposed && !operation.IsCompleted && !disposal.IsCompleted, "Cancellation and Dispose cannot release or complete while native completion is blocked.");
                Check(Marshal.ReadByte(native.Buffer) == 0 && Marshal.PtrToStructure<TurzxNativeOverlapped>(native.Overlapped).EventHandle != IntPtr.Zero, "Pinned buffer and native OVERLAPPED remain owned through terminal drain.");
            }
            finally { native.Terminal.Set(); }
            Check(operation.Wait(5000) && disposal.Wait(5000) && native.Disposed && native.Completions == 1, "Release follows exactly one terminal drain.");
            Check(cancellationFails ? operation.Result is Win32Exception { NativeErrorCode: 5 } : operation.Result is OperationCanceledException, "Cancellation failures are preserved after draining; normal cancellation remains cancellation.");
            Throws<ObjectDisposedException>(() => connection.Read(new byte[512], default));
        }
    }

    public static void NativeDeadlineAndDispose()
    {
        using (var native = new FakeNative { Pending = true })
        using (var connection = new TurzxUsbConnection(native, 500))
        {
            Throws<TimeoutException>(() => connection.Read(new byte[512], default));
            Check(native.Cancellations == 1 && native.Completions == 1, "The managed deadline requests cancellation and drains before reporting timeout.");
        }
        var blocked = new FakeNative { Pending = true, BlockCompletion = true };
        using var usb = new TurzxUsbConnection(blocked, 3000);
        var read = Task.Run(() => Capture(() => usb.Read(new byte[512], default)));
        Await(blocked.Submitted);
        var dispose = Task.Run(usb.Dispose);
        Await(blocked.Completing);
        try { Check(blocked.Cancellations == 1 && !blocked.Disposed && !dispose.IsCompleted, "Dispose requests cancellation but retains resources until completion."); }
        finally { blocked.Terminal.Set(); }
        Check(read.Wait(5000) && dispose.Wait(5000) && read.Result is OperationCanceledException && blocked.Disposed, "Dispose finishes only after native cancellation completes.");
    }

    public static void NativeAbiAndEndpoints()
    {
        Check(IntPtr.Size == 8 && Marshal.SizeOf<TurzxNativeOverlapped>() == 32 &&
            Marshal.OffsetOf<TurzxNativeOverlapped>(nameof(TurzxNativeOverlapped.EventHandle)).ToInt32() == 24,
            "TURZX OVERLAPPED must match the supported x64 ABI.");
        Check(Marshal.SizeOf<TurzxNativePipe>() == 12 && Marshal.OffsetOf<TurzxNativePipe>(nameof(TurzxNativePipe.MaximumPacketSize)).ToInt32() == 6,
            "WINUSB_PIPE_INFORMATION must preserve enum/byte/ushort alignment.");
        var outPipe = new TurzxNativePipe { Type = 2, Id = 1, MaximumPacketSize = 512 };
        var inPipe = new TurzxNativePipe { Type = 2, Id = 0x81, MaximumPacketSize = 512 };
        WindowsTurzxNativeIo.ValidatePipes([outPipe, inPipe]);
        Throws<IOException>(() => WindowsTurzxNativeIo.ValidatePipes([outPipe]));
        Throws<IOException>(() => WindowsTurzxNativeIo.ValidatePipes([outPipe, inPipe, inPipe]));
        inPipe.Type = 3; Throws<IOException>(() => WindowsTurzxNativeIo.ValidatePipes([outPipe, inPipe]));
        inPipe.Type = 2; inPipe.MaximumPacketSize = 0;
        Throws<IOException>(() => WindowsTurzxNativeIo.ValidatePipes([outPipe, inPipe]));
        Throws<ArgumentOutOfRangeException>(() => TurzxUsbConnection.ValidateTimeout(499));
    }

    public static void TransportDisposalDrainsCommand()
    {
        var native = new FakeNative { Pending = true, BlockCompletion = true };
        var connection = new TurzxUsbConnection(native, 3000);
        using var transport = new TurzxTransport(connection, () => Timestamp, _ => { }, TurzxReplyMode.PrimaryOnly);
        var sync = Task.Run(() => Capture(() => transport.Synchronize()));
        Await(native.Submitted);
        var dispose = Task.Run(transport.Dispose);
        Await(native.Completing);
        try { Check(!native.Disposed && !dispose.IsCompleted, "Transport shutdown must keep the command's connection alive through cancellation draining."); }
        finally { native.Terminal.Set(); }
        Check(sync.Wait(5000) && dispose.Wait(5000) && sync.Result is OperationCanceledException && native.Disposed,
            "Transport cancellation reaches the native operation and finishes before disposing its connection.");
        Throws<ObjectDisposedException>(() => transport.Synchronize());
    }

    public static void PendingNativeSuccess()
    {
        using var native = new FakeNative { Pending = true, SignalCompletion = true };
        using var connection = new TurzxUsbConnection(native, 3000);
        Check(connection.Read(new byte[512], default) == 512 && native.Completions == 1 && native.Cancellations == 0,
            "A pending operation signaled by its completion event must drain without requesting cancellation.");
    }

    public static void CommandsRemainSerialized()
    {
        using var entered = new ManualResetEvent(false);
        using var release = new ManualResetEvent(false);
        using var secondStarted = new ManualResetEvent(false);
        using var usb = new FakeConnection(Reply(), Reply(), Reply());
        using var transport = New(usb);
        transport.Synchronize();
        usb.BeforeRead = (index, _) => { if (index == 1) { entered.Set(); Await(release); } };
        var first = Task.Run(() => transport.SendImage(TurzxImageFormat.Png, Png));
        Await(entered);
        var second = Task.Run(() => { secondStarted.Set(); return transport.SetBrightnessPercent(50); });
        Await(secondStarted);
        try { Check(!second.Wait(100) && usb.Writes.Count == 2, "A second command cannot write before the first command's response completes."); }
        finally { release.Set(); }
        Check(first.Wait(5000) && second.Wait(5000) && usb.Writes.Count == 3 && usb.Reads == 3,
            "Serialized commands resume in order after the admitted response boundary.");
    }

    public static void JpegStreamOwnsReplyBoundaries()
    {
        using var usb = new FakeConnection(Reply(), [], Reply(), [], Reply(), []);
        var delays = 0;
        using var transport = new TurzxTransport(usb, () => Timestamp, _ =>
        {
            delays++;
            Check(usb.Reads == 1, "Sync settles after its primary reply and before its one follow-up.");
        });
        Check(transport.ReplyMode == TurzxReplyMode.JpegStream &&
            transport.Synchronize() == new TurzxCommandCompletion(10, 512, TurzxReplyKind.StatusMarker, 0),
            "The default stream must finish sync's primary and successful empty follow-up before returning.");
        Throws<NotSupportedException>(() => transport.SendImage(TurzxImageFormat.Png, Png));
        Throws<NotSupportedException>(() => transport.SetBrightnessPercent(50));
        Throws<InvalidDataException>(() => transport.SendImage(TurzxImageFormat.Jpeg, Png));
        Check(usb.Writes.Count == 1 && usb.Reads == 2, "Unverified codecs/brightness and malformed JPEG must not issue I/O or poison the stream.");
        for (var frame = 0; frame < 2; frame++)
            Check(transport.SendImage(TurzxImageFormat.Jpeg, Jpeg) == new TurzxCommandCompletion(101, 512, TurzxReplyKind.StatusMarker, 0),
                "Each JPEG must complete its own full primary reply and single empty follow-up.");
        Check(delays == 1 && usb.Writes.Count == 3 && usb.Reads == 6,
            "Sync and two frames require exactly three writes and six reads, without another sync or retry.");
        Throws<ArgumentOutOfRangeException>(() => new TurzxTransport(usb, replyMode: (TurzxReplyMode)7));
    }

    public static void JpegStreamRejectsUncertainBoundaries()
    {
        for (var phase = 0; phase < 3; phase++)
        {
            foreach (var count in new[] { -1, 1, 7, 512, 513 })
            {
                using var usb = new FakeConnection(Reply(), [], Reply(), [], Reply(), [])
                {
                    ReportedReadCount = (index, actual) => index == phase * 2 + 1 ? count : actual
                };
                using var transport = new TurzxTransport(usb, () => Timestamp, _ => { });
                var error = Capture(() =>
                {
                    transport.Synchronize();
                    transport.SendImage(TurzxImageFormat.Jpeg, Jpeg);
                    transport.SendImage(TurzxImageFormat.Jpeg, Jpeg);
                });
                Check(count is < 0 or > 512 ? error is IOException : error is InvalidDataException,
                    "Invalid lengths and nonempty follow-ups remain distinct failures.");
                Throws<InvalidOperationException>(() => transport.SendImage(TurzxImageFormat.Jpeg, Jpeg));
                Check(usb.Writes.Count == phase + 1 && usb.Reads == (phase + 1) * 2,
                    "An uncertain sync/A/B boundary must stop before the next command without another read or resend.");
            }
            foreach (var failure in new Exception[] { new TimeoutException(), new Win32Exception(1167), new OperationCanceledException(), new IOException() })
            {
                using var usb = new FakeConnection(Reply(), [], Reply(), [], Reply(), []);
                usb.BeforeRead = (index, _) => { if (index == phase * 2 + 1) throw failure; };
                using var transport = new TurzxTransport(usb, () => Timestamp, _ => { });
                Check(ReferenceEquals(Capture(() =>
                {
                    transport.Synchronize();
                    transport.SendImage(TurzxImageFormat.Jpeg, Jpeg);
                    transport.SendImage(TurzxImageFormat.Jpeg, Jpeg);
                }), failure), "Follow-up timeout, disconnect, cancellation and I/O error must retain their identity.");
                Throws<InvalidOperationException>(() => transport.SendImage(TurzxImageFormat.Jpeg, Jpeg));
                Check(usb.Writes.Count == phase + 1 && usb.Reads == (phase + 1) * 2,
                    "A failed follow-up must fault the stream without admitting another command.");
            }
            using var primaryUsb = new FakeConnection(Reply(), [], Reply(), [], Reply(), [])
            {
                ReportedReadCount = (index, actual) => index == phase * 2 ? 0 : actual
            };
            using var primaryTransport = new TurzxTransport(primaryUsb, () => Timestamp, _ => { });
            Throws<InvalidDataException>(() =>
            {
                primaryTransport.Synchronize();
                primaryTransport.SendImage(TurzxImageFormat.Jpeg, Jpeg);
                primaryTransport.SendImage(TurzxImageFormat.Jpeg, Jpeg);
            });
            Check(primaryUsb.Writes.Count == phase + 1 && primaryUsb.Reads == phase * 2 + 1,
                "An empty primary reply must still fail before a follow-up; no empty-skipping is permitted.");
        }
    }

    public static void JpegStreamSerializesFollowUp()
    {
        using var entered = new ManualResetEvent(false);
        using var release = new ManualResetEvent(false);
        using var secondStarted = new ManualResetEvent(false);
        using var usb = new FakeConnection(Reply(), [], Reply(), [], Reply(), []);
        using var transport = new TurzxTransport(usb, () => Timestamp, _ => { });
        transport.Synchronize();
        usb.BeforeRead = (index, _) => { if (index == 3) { entered.Set(); Await(release); } };
        var first = Task.Run(() => transport.SendImage(TurzxImageFormat.Jpeg, Jpeg));
        Await(entered);
        var second = Task.Run(() => { secondStarted.Set(); return transport.SendImage(TurzxImageFormat.Jpeg, Jpeg); });
        Await(secondStarted);
        try { Check(!second.Wait(100) && usb.Writes.Count == 2, "A later frame cannot write while the previous follow-up is in progress."); }
        finally { release.Set(); }
        Check(first.Wait(5000) && second.Wait(5000) && usb.Writes.Count == 3 && usb.Reads == 6,
            "The next frame resumes only after the complete primary/follow-up boundary.");
    }

    public static void JpegStreamFollowUpNativeLifetime()
    {
        using var followUpSubmitted = new ManualResetEvent(false);
        var native = new FakeNative();
        native.BeforeSubmit = (read, index) =>
        {
            if (read) Marshal.WriteByte(native.Buffer, 1, 0xC8);
            if (index == 3)
            {
                native.Pending = true;
                native.BlockCompletion = true;
                native.Completing.Reset();
                followUpSubmitted.Set();
            }
        };
        var connection = new TurzxUsbConnection(native, 3000);
        using var transport = new TurzxTransport(connection, () => Timestamp, _ => { });
        var sync = Task.Run(() => Capture(() => transport.Synchronize()));
        Await(followUpSubmitted);
        var disposal = Task.Run(transport.Dispose);
        Await(native.Completing);
        try
        {
            Check(native.Submissions == 3 && native.Cancellations == 1 && !native.Disposed && !sync.IsCompleted && !disposal.IsCompleted,
                "Shutdown during sync's follow-up must cancel and retain native ownership through terminal draining.");
            Check(Marshal.PtrToStructure<TurzxNativeOverlapped>(native.Overlapped).EventHandle != IntPtr.Zero &&
                Marshal.ReadByte(native.Buffer, 1) == 0xC8, "Follow-up buffer, event and OVERLAPPED must remain alive until terminal completion.");
        }
        finally { native.Terminal.Set(); }
        Check(sync.Wait(5000) && disposal.Wait(5000) && sync.Result is OperationCanceledException && native.Disposed && native.Completions == 3,
            "A canceled follow-up drains exactly once before connection release and admits no frame.");
    }

    private static TurzxTransport New(FakeConnection usb) => new(usb, () => Timestamp, _ => { }, TurzxReplyMode.PrimaryOnly);
    private static byte[] Reply() { var bytes = new byte[512]; bytes[1] = 0xC8; return bytes; }
    private static void Digest(ReadOnlySpan<byte> bytes, string expected) => Check(Convert.ToHexString(SHA256.HashData(bytes)) == expected, "Encoded header must match an independent full-packet fixture.");
    private static void Await(WaitHandle signal) => Check(signal.WaitOne(5000), "Timed out waiting for a deterministic test boundary.");
    private static Exception Capture(Action action) { try { action(); } catch (Exception e) { return e; } throw new InvalidOperationException("Expected a failure."); }
    private static void Throws<T>(Action action) where T : Exception => Check(Capture(action) is T, $"Expected {typeof(T).Name}.");
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class FakeConnection(params byte[][] replies) : ITurzxUsbConnection
    {
        public List<byte[]> Writes { get; } = [];
        public int Reads { get; private set; }
        public bool Disposed { get; private set; }
        public bool ShortWrite { get; init; }
        public Exception? WriteFailure { get; init; }
        public Exception? ReadFailure { get; init; }
        public Action<int, CancellationToken>? BeforeRead { get; set; }
        public Func<int, int, int>? ReportedReadCount { get; init; }
        public int Write(byte[] packet, CancellationToken token) { token.ThrowIfCancellationRequested(); Writes.Add(packet.ToArray()); if (WriteFailure is not null) throw WriteFailure; return packet.Length - (ShortWrite ? 1 : 0); }
        public int Read(byte[] buffer, CancellationToken token) { token.ThrowIfCancellationRequested(); var index = Reads++; BeforeRead?.Invoke(index, token); if (ReadFailure is not null) throw ReadFailure; var reply = replies[index]; reply.CopyTo(buffer, 0); return ReportedReadCount?.Invoke(index, reply.Length) ?? reply.Length; }
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeNative : ITurzxNativeIo
    {
        public bool Pending, BlockCompletion, CancelFailure, SignalCompletion, Disposed;
        public uint Bytes = 512;
        public int SubmitError, CompletionError, Submissions, Completions, Cancellations;
        public byte LastPipe;
        public Action<bool, int>? BeforeSubmit;
        public IntPtr Buffer, Overlapped;
        public readonly ManualResetEvent Submitted = new(false), Completing = new(false), Terminal = new(false);
        public bool Submit(bool read, byte pipe, IntPtr buffer, uint length, IntPtr overlapped, out int error)
        {
            Submissions++; LastPipe = pipe; Buffer = buffer; Overlapped = overlapped;
            BeforeSubmit?.Invoke(read, Submissions);
            error = Pending ? 997 : SubmitError;
            if (SignalCompletion)
            {
                using var completion = new EventWaitHandle(false, EventResetMode.ManualReset);
                completion.SafeWaitHandle = new Microsoft.Win32.SafeHandles.SafeWaitHandle(
                    Marshal.PtrToStructure<TurzxNativeOverlapped>(overlapped).EventHandle, ownsHandle: false);
                completion.Set();
            }
            Submitted.Set(); return error == 0;
        }
        public bool Complete(IntPtr overlapped, out uint transferred, out int error)
        { Completions++; Completing.Set(); if (BlockCompletion && !Terminal.WaitOne(5000)) throw new TimeoutException("Synthetic native completion not released."); transferred = Bytes; error = CompletionError; return error == 0; }
        public void Cancel(IntPtr overlapped) { Cancellations++; if (CancelFailure) throw new Win32Exception(5); }
        public void Dispose() { if (Disposed) return; Disposed = true; Submitted.Dispose(); Completing.Dispose(); Terminal.Dispose(); }
    }
}
