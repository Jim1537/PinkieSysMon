using System.ComponentModel;
using System.Drawing.Design;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using PinkieSysMon;
using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static partial class Program
{
    private static void TrofeoWireFramingContract()
    {
        var layouts = new[]
        {
            (Payload: 0, Chunks: 1, Padded: 4, Frame: 2048, Last: 0),
            (Payload: 1, Chunks: 1, Padded: 4, Frame: 2048, Last: 1),
            (Payload: 495, Chunks: 1, Padded: 4, Frame: 2048, Last: 495),
            (Payload: 496, Chunks: 2, Padded: 4, Frame: 2048, Last: 0),
            (Payload: 497, Chunks: 2, Padded: 4, Frame: 2048, Last: 1),
            (Payload: 1983, Chunks: 4, Padded: 4, Frame: 2048, Last: 495),
            (Payload: 1984, Chunks: 5, Padded: 8, Frame: 4096, Last: 0)
        };

        foreach (var expected in layouts)
        {
            var actual = TrofeoWireProtocol.GetFrameLayout(expected.Payload);
            Assert(
                actual.ChunkCount == expected.Chunks &&
                actual.PaddedChunkCount == expected.Padded &&
                actual.FrameLength == expected.Frame &&
                actual.LastDataLength == expected.Last,
                $"Trofeo layout mismatch for {expected.Payload} bytes: " +
                $"chunks={actual.ChunkCount}, padded={actual.PaddedChunkCount}, " +
                $"frame={actual.FrameLength}, last={actual.LastDataLength}.");
        }

        var payload = Enumerable.Range(0, 497)
            .Select(index => (byte)(index % 251))
            .ToArray();
        var layout = TrofeoWireProtocol.GetFrameLayout(payload.Length);
        var frame = Enumerable.Repeat((byte)0xA5, layout.FrameLength).ToArray();

        var written = TrofeoWireProtocol.WriteFrame(payload, frame);
        Assert(written == layout.FrameLength,
            "Trofeo frame writer must return the padded wire-frame length.");

        var first = frame.AsSpan(0, TrofeoWireProtocol.ChunkSize);
        Assert(first[0] == 0x01 && first[1] == 0xFF && first[8] == 0x01,
            "Trofeo first record markers must remain 01 FF / 01.");
        Assert(
            System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(first.Slice(2, 4)) == (uint)payload.Length &&
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(first.Slice(6, 2)) == TrofeoWireProtocol.DataSize &&
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(first.Slice(9, 2)) == 2 &&
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(first.Slice(11, 2)) == 0,
            "Trofeo first record header must preserve total size, data length, chunk count and index.");
        Assert(first.Slice(13, 3).ToArray().All(value => value == 0),
            "Trofeo reserved header bytes must be zeroed.");
        Assert(payload.AsSpan(0, TrofeoWireProtocol.DataSize).SequenceEqual(
                   first.Slice(TrofeoWireProtocol.HeaderSize, TrofeoWireProtocol.DataSize)),
            "Trofeo first record payload bytes must be preserved verbatim.");

        var second = frame.AsSpan(TrofeoWireProtocol.ChunkSize, TrofeoWireProtocol.ChunkSize);
        Assert(
            System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(second.Slice(2, 4)) == (uint)payload.Length &&
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(second.Slice(6, 2)) == 1 &&
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(second.Slice(9, 2)) == 2 &&
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(second.Slice(11, 2)) == 1,
            "Trofeo terminal record header must preserve total size, residual length, chunk count and index.");
        Assert(second[TrofeoWireProtocol.HeaderSize] == payload[^1],
            "Trofeo terminal record must contain the final payload byte.");
        Assert(second.Slice(TrofeoWireProtocol.HeaderSize + 1, TrofeoWireProtocol.DataSize - 1)
                .ToArray()
                .All(value => value == 0),
            "Trofeo unused bytes in the terminal record must be zero padded.");
        Assert(frame.AsSpan(TrofeoWireProtocol.ChunkSize * 2).ToArray().All(value => value == 0),
            "Trofeo padding records must be completely zeroed.");

        var exactPayload = new byte[TrofeoWireProtocol.DataSize];
        var exactLayout = TrofeoWireProtocol.GetFrameLayout(exactPayload.Length);
        var exactFrame = new byte[exactLayout.FrameLength];
        TrofeoWireProtocol.WriteFrame(exactPayload, exactFrame);
        var exactTerminal = exactFrame.AsSpan(TrofeoWireProtocol.ChunkSize, TrofeoWireProtocol.ChunkSize);
        Assert(
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(exactTerminal.Slice(6, 2)) == 0 &&
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(exactTerminal.Slice(9, 2)) == 2 &&
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(exactTerminal.Slice(11, 2)) == 1,
            "An exact 496-byte payload must retain the proven extra zero-length terminal record.");
    }

    private static void TrofeoUsbTransferSegmentationContract()
    {
        static int[] Segment(int frameLength)
        {
            var result = new List<int>();
            for (var remaining = frameLength; remaining > 0;)
            {
                var count = TrofeoWireProtocol.GetTransferLength(remaining);
                Assert(count > 0 && count <= TrofeoWireProtocol.TransferBlockSize,
                    "Trofeo transfer segmentation must always make bounded forward progress.");
                result.Add(count);
                remaining -= count;
            }

            return result.ToArray();
        }

        Assert(Segment(2048).SequenceEqual(new[] { 2048 }),
            "A 2048-byte Trofeo wire frame must be sent as one transfer.");
        Assert(Segment(4096).SequenceEqual(new[] { 4096 }),
            "A 4096-byte Trofeo wire frame must be sent as one transfer.");
        Assert(Segment(6144).SequenceEqual(new[] { 4096, 2048 }),
            "A 6144-byte Trofeo wire frame must preserve 4096 + 2048 transfer segmentation.");
        Assert(Segment(8192).SequenceEqual(new[] { 4096, 4096 }),
            "An 8192-byte Trofeo wire frame must preserve two 4096-byte transfers.");
        Assert(Segment(10240).SequenceEqual(new[] { 4096, 4096, 2048 }),
            "A 10240-byte Trofeo wire frame must preserve repeated 4096-byte transfers plus the tail.");
    }

    private static void TrofeoOverlappedCancelAwaitsCompletion()
    {
        using var eventNotYetSignaled = new ManualResetEvent(initialState: false);
        using var cts = new CancellationTokenSource();
        using var cancellationRequested = new ManualResetEventSlim();
        using var allowTerminalCompletion = new ManualResetEventSlim();
        cts.Cancel();

        var worker = Task.Run(() => TrofeoOverlappedCompletionWait.Wait(
            eventNotYetSignaled,
            cts.Token,
            () => cancellationRequested.Set(),
            () =>
            {
                if (!allowTerminalCompletion.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("Synthetic terminal completion was not signaled.");
            }));

        try
        {
            Assert(cancellationRequested.Wait(TimeSpan.FromSeconds(2)),
                "A pending native cancellation request must be issued.");
            Assert(!worker.Wait(TimeSpan.FromMilliseconds(100)),
                "Cancellation request alone must never mark pending native I/O as completed.");

            allowTerminalCompletion.Set();
            Assert(worker.Wait(TimeSpan.FromSeconds(2)) && worker.Result,
                "Cancellation must return only after the terminal native result was drained.");
        }
        finally
        {
            allowTerminalCompletion.Set();
            try { worker.GetAwaiter().GetResult(); } catch { }
        }
    }

    private static void TrofeoOverlappedX64AbiContract()
    {
        Assert(IntPtr.Size == 8,
            "Trofeo native OVERLAPPED contract must execute in the declared win-x64 process.");

        var flags = System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static;
        var native = typeof(TrofeoTransport).GetNestedType(
            "Win32Overlapped", System.Reflection.BindingFlags.NonPublic);
        Assert(native is not null &&
               System.Runtime.InteropServices.Marshal.SizeOf(native) == 32 &&
               System.Runtime.InteropServices.Marshal.OffsetOf(native, "EventHandle").ToInt32() == 24,
            "Native Windows OVERLAPPED must match the 32-byte x64 ABI with hEvent at offset 24.");

        foreach (var name in new[] { "WinUsb_ReadPipe", "WinUsb_WritePipe" })
        {
            var nativeMethod = typeof(TrofeoTransport).GetMethod(name, flags);
            var parameters = nativeMethod?.GetParameters();
            Assert(parameters is not null &&
                   parameters.Length == 6 &&
                   parameters[2].ParameterType == typeof(IntPtr) &&
                   parameters[5].ParameterType == typeof(IntPtr),
                $"{name} must use a stable pinned byte buffer and native OVERLAPPED pointer.");
        }

        Assert(typeof(TrofeoTransport).GetMethod("WinUsb_GetOverlappedResult", flags) is not null &&
               typeof(TrofeoTransport).GetMethod("CancelIoEx", flags) is not null,
            "Cancellable native WinUSB transfers require terminal-result and targeted-cancel APIs.");
    }

    private static void TrofeoOverlappedWaitCompleted()
    {
        using var completion = new ManualResetEvent(initialState: true);
        using var cts = new CancellationTokenSource();
        var cancelRequests = 0;
        var drainCalls = 0;

        var canceled = TrofeoOverlappedCompletionWait.Wait(
            completion,
            cts.Token,
            () => cancelRequests++,
            () => drainCalls++);

        Assert(!canceled && cancelRequests == 0 && drainCalls == 0,
            "A completed overlapped operation must not request cancellation or an abort drain.");
    }

    private static void TrofeoOverlappedWaitCanceledAndDrained()
    {
        using var completion = new ManualResetEvent(initialState: false);
        using var cts = new CancellationTokenSource();
        using var waiting = new ManualResetEventSlim();
        var cancelRequests = 0;
        var drainCalls = 0;

        var worker = Task.Run(() =>
        {
            waiting.Set();
            return TrofeoOverlappedCompletionWait.Wait(
                completion,
                cts.Token,
                () =>
                {
                    Interlocked.Increment(ref cancelRequests);
                    completion.Set();
                },
                () =>
                {
                    Assert(completion.WaitOne(0),
                        "Cancel callback must run before the terminal-completion drain.");
                    Interlocked.Increment(ref drainCalls);
                });
        });

        Assert(waiting.Wait(TimeSpan.FromSeconds(2)),
            "Overlapped wait regression worker did not start.");
        cts.Cancel();
        Assert(worker.Wait(TimeSpan.FromSeconds(2)) && worker.Result,
            "Cancellation must unblock the pending overlapped wait.");
        Assert(cancelRequests == 1 && drainCalls == 1,
            "Cancel must be requested once and terminal completion must be drained exactly once.");
    }

    private static void TrofeoOverlappedWaitCancellationFailureDrains()
    {
        using var completion = new ManualResetEvent(initialState: false);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var drainCalls = 0;

        AssertThrows<InvalidOperationException>(
            () => TrofeoOverlappedCompletionWait.Wait(
                completion,
                cts.Token,
                () => throw new InvalidOperationException("Synthetic cancel request failure."),
                () => drainCalls++),
            "A native cancellation request failure must remain observable.");

        Assert(drainCalls == 1,
            "Even failed cancellation requests must drain pending OVERLAPPED completion before releasing memory.");
    }

    private static void FramePumpSuspendCancelsBlockedOpen()
    {
        var root = CreateTemporaryApplicationRoot("PinkieSysMon-pump-cancel-open");
        using var entered = new ManualResetEventSlim();
        using var canceled = new ManualResetEventSlim();
        var pump = new FramePump(
            new AppConfig(),
            new OutputTargetConfig(),
            new C.DashboardDefinition { BaseDirectory = Path.Combine(root, "dashboards") },
            new MetricStore(),
            new FileLogger(Path.Combine(root, "cancel.log")),
            (target, timeout, log, ioToken) =>
            {
                entered.Set();
                try
                {
                    if (!ioToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
                        throw new TimeoutException("Synthetic handshake was not canceled.");
                    ioToken.ThrowIfCancellationRequested();
                    throw new InvalidOperationException("Synthetic handshake ended without cancellation.");
                }
                catch (OperationCanceledException)
                {
                    canceled.Set();
                    throw;
                }
            });

        try
        {
            pump.Start();
            Assert(entered.Wait(TimeSpan.FromSeconds(5)),
                "Worker must enter the synthetic pending handshake.");
            pump.Suspend();
            Assert(canceled.Wait(TimeSpan.FromSeconds(2)),
                "Suspending an output must cancel a pending worker-owned handshake.");
            Assert(pump.UsbState == "SUSPENDED" && !pump.IsConnected,
                "Canceled handshake must leave the output suspended and disconnected.");
        }
        finally
        {
            pump.Dispose();
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void FramePumpDisposeCancelsBlockedSend()
    {
        var root = CreateTemporaryApplicationRoot("PinkieSysMon-pump-cancel-send");
        using var sendEntered = new ManualResetEventSlim();
        var disposalCount = 0;
        var pump = new FramePump(
            new AppConfig { Usb = new UsbConfig { TransferTimeoutMs = 500 } },
            new OutputTargetConfig(),
            new C.DashboardDefinition { BaseDirectory = Path.Combine(root, "dashboards") },
            new MetricStore(),
            new FileLogger(Path.Combine(root, "cancel.log")),
            (target, timeout, log, ioToken) =>
                new CancellationAwareTrofeoTestTransport(
                    ioToken, sendEntered, () => Interlocked.Increment(ref disposalCount)));

        try
        {
            pump.Start();
            Assert(sendEntered.Wait(TimeSpan.FromSeconds(5)),
                "Worker must reach the synthetic pending frame transfer.");

            var disposeTask = Task.Run(pump.Dispose);
            Assert(disposeTask.Wait(TimeSpan.FromSeconds(3)),
                "Dispose must cancel a stalled frame transfer after its completion grace and drain the worker.");
            Assert(disposalCount == 1,
                "Worker-owned transport must be disposed exactly once after cancellation.");
        }
        finally
        {
            pump.Dispose();
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void FramePumpSuspendDuringBlockedOpen()
    {
        var root = CreateTemporaryApplicationRoot("PinkieSysMon-pump-open-ownership");
        using var openStarted = new ManualResetEventSlim();
        using var allowOpen = new ManualResetEventSlim();
        var fake = new BlockingTrofeoTestTransport();
        var pump = new FramePump(
            new AppConfig(),
            new OutputTargetConfig(),
            new C.DashboardDefinition { BaseDirectory = Path.Combine(root, "dashboards") },
            new MetricStore(),
            new FileLogger(Path.Combine(root, "ownership.log")),
            (target, timeout, log, ioToken) =>
            {
                openStarted.Set();
                if (!allowOpen.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("Test open was not released.");
                return fake;
            });

        try
        {
            pump.Start();
            Assert(openStarted.Wait(TimeSpan.FromSeconds(5)),
                "Worker must enter the injected blocking USB open.");

            var suspension = Task.Run(pump.Suspend);
            Assert(suspension.Wait(TimeSpan.FromSeconds(2)),
                "Suspend must not wait for in-progress USB discovery/handshake under the general session lock.");
            Assert(pump.UsbState == "SUSPENDED" && !pump.IsConnected,
                "Suspend must immediately expose SUSPENDED/unavailable even while USB open is blocked.");

            allowOpen.Set();
            Assert(SpinWait.SpinUntil(() => fake.DisposeCount == 1, TimeSpan.FromSeconds(5)),
                "A USB handle opened after suspend must be closed by the worker before any transfer.");
            Assert(fake.SendCount == 0,
                "No frame may be sent on a connection invalidated during USB open.");
        }
        finally
        {
            allowOpen.Set();
            pump.Dispose();
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void FramePumpSuspendDuringBlockedSend()
    {
        var root = CreateTemporaryApplicationRoot("PinkieSysMon-pump-send-ownership");
        using var sendStarted = new ManualResetEventSlim();
        using var allowSend = new ManualResetEventSlim();
        var fake = new BlockingTrofeoTestTransport(sendStarted, allowSend);
        var pump = new FramePump(
            new AppConfig(),
            new OutputTargetConfig(),
            new C.DashboardDefinition { BaseDirectory = Path.Combine(root, "dashboards") },
            new MetricStore(),
            new FileLogger(Path.Combine(root, "ownership.log")),
            (target, timeout, log, ioToken) => fake);

        try
        {
            pump.Start();
            Assert(sendStarted.Wait(TimeSpan.FromSeconds(5)),
                "Worker must reach the injected blocking frame transfer.");

            var suspension = Task.Run(pump.Suspend);
            Assert(suspension.Wait(TimeSpan.FromSeconds(2)),
                "Suspend must return without taking the USB sender's general state lock.");
            Assert(pump.UsbState == "SUSPENDED" && !pump.IsConnected,
                "In-flight transfer must not leave the public connection state CONNECTED after suspend.");
            Assert(fake.DisposeCount == 0,
                "Suspend must never dispose a transport while an in-flight send owns it.");

            allowSend.Set();
            Assert(SpinWait.SpinUntil(() => fake.DisposeCount == 1, TimeSpan.FromSeconds(5)),
                "Worker must release the in-flight USB handle after the blocked send completes.");
            Assert(fake.SendCount == 1 && pump.UsbState == "SUSPENDED",
                "Suspend must prevent any subsequent send or stale CONNECTED status.");
        }
        finally
        {
            allowSend.Set();
            pump.Dispose();
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void FramePumpReconfigureDuringBlockedSend()
    {
        var root = CreateTemporaryApplicationRoot("PinkieSysMon-pump-reconfig-ownership");
        using var sendStarted = new ManualResetEventSlim();
        using var allowSend = new ManualResetEventSlim();
        var original = new BlockingTrofeoTestTransport(sendStarted, allowSend);
        var replacement = new BlockingTrofeoTestTransport();
        var opens = 0;
        var config = new AppConfig();
        var target = new OutputTargetConfig { Transport = OutputTargetContract.LyBulkTransport };
        var pump = new FramePump(
            config, target, new C.DashboardDefinition { BaseDirectory = Path.Combine(root, "dashboards") }, new MetricStore(),
            new FileLogger(Path.Combine(root, "ownership.log")),
            (selectedTarget, timeout, log, ioToken) =>
                Interlocked.Increment(ref opens) == 1 ? original : replacement);

        try
        {
            pump.Start();
            Assert(sendStarted.Wait(TimeSpan.FromSeconds(5)),
                "Worker must enter the first device's blocked transfer.");

            var newConfig = new AppConfig();
            newConfig.Usb.TransferTimeoutMs = config.Usb.TransferTimeoutMs + 500;
            using var prepared = pump.PrepareReconfiguration(
                newConfig, new OutputTargetConfig(), new C.DashboardDefinition { BaseDirectory = Path.Combine(root, "dashboards") });

            var reconfigure = Task.Run(() => pump.CommitReconfiguration(prepared));
            Assert(reconfigure.Wait(TimeSpan.FromSeconds(2)),
                "Transport-affecting reconfigure must not wait for active USB I/O under the general lock.");
            Assert(original.DisposeCount == 0,
                "Reconfigure must not close a transport whose send has not finished.");

            allowSend.Set();
            Assert(SpinWait.SpinUntil(() => original.DisposeCount == 1, TimeSpan.FromSeconds(5)),
                "Old transport must be closed by its owning worker after reconfiguration.");

            Assert(SpinWait.SpinUntil(() => Volatile.Read(ref opens) >= 2, TimeSpan.FromSeconds(5)),
                "Worker must open a fresh transport after the in-flight old transfer has retired.");
        }
        finally
        {
            allowSend.Set();
            pump.Dispose();
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

}
