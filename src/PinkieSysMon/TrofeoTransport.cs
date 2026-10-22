using Microsoft.Win32.SafeHandles;
using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PinkieSysMon;

internal sealed class TrofeoTransport : IPhysicalFrameTransport
{
    private const byte PipeOut = 0x09;
    private const byte PipeIn = 0x81;
    private const uint PipeTransferTimeout = 0x03;

    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint FileFlagOverlapped = 0x40000000;
    private const int ErrorIoPending = 997;
    private const int ErrorNotFound = 1168;

    private readonly FileLogger _log;
    private readonly OutputTargetConfig _target;
    private readonly OutputDeviceDescriptor _device;
    private readonly byte[] _ackBuffer = new byte[512];
    private readonly TrofeoDiagnosticJournal? _diagnostics;
    private readonly CancellationToken _ioCancellation;
    private SafeFileHandle? _deviceHandle;
    private IntPtr _interfaceHandle;
    private byte[] _transferBuffer = Array.Empty<byte>();
    private GCHandle _transferBufferHandle;
    private bool _transferBufferPinned;
    private IntPtr _transferBufferAddress;
    private EventWaitHandle? _completionEvent;
    private IntPtr _nativeOverlapped;
    private long _frameSequence;

    private TrofeoTransport(
        FileLogger log,
        OutputTargetConfig target,
        OutputDeviceDescriptor device,
        bool diagnosticJournalEnabled,
        CancellationToken ioCancellation)
    {
        _log = log;
        _ioCancellation = ioCancellation;
        _target = target;
        _device = device;

        if (diagnosticJournalEnabled)
        {
            try
            {
                _diagnostics = new TrofeoDiagnosticJournal(
                    TrofeoDiagnosticJournal.GetPath(log.Path, target.Id));
            }
            catch (Exception ex)
            {
                _log.Warn($"Output target '{target.Name}' USB diagnostic journal could not be opened.", ex);
            }
        }
    }

    public int WireRotationDegrees { get; private set; }
    public int ProtocolPm { get; private set; }
    public int ProtocolSub { get; private set; }

    public static TrofeoTransport? TryOpen(
        OutputTargetConfig target,
        int transferTimeoutMs,
        bool diagnosticJournalEnabled,
        FileLogger log,
        CancellationToken ioCancellation,
        PhysicalHandshakeDiagnostics? diagnostics = null)
    {
        diagnostics?.Stage("discovery");
        ArgumentNullException.ThrowIfNull(target);
        ioCancellation.ThrowIfCancellationRequested();
        if (!target.DeviceEnabled || !OutputTargetContract.IsPhysicalKind(target.Kind) ||
            target.Transport != OutputTargetContract.LyBulkTransport)
            throw new InvalidOperationException("Only an enabled LY bulk Output may acquire this protocol.");

        var discoveredDevices = PhysicalDeviceDiscovery.CaptureForTarget(target);
        var resolution = OutputDeviceSelection.Resolve(target, discoveredDevices);
        if (resolution.Status == OutputDeviceResolutionStatus.Missing)
            return null;
        if (resolution.Status != OutputDeviceResolutionStatus.Found || resolution.Device is null)
            throw new OutputDeviceResolutionException(resolution.Status, resolution.Message);

        ioCancellation.ThrowIfCancellationRequested();
        var transport = new TrofeoTransport(log, target, resolution.Device, diagnosticJournalEnabled, ioCancellation);
        var openStartedAt = Stopwatch.GetTimestamp();
        try
        {
            transport.Open(resolution.Device.DevicePath, transferTimeoutMs, diagnostics);
            return transport;
        }
        catch (Exception ex)
        {
            transport.WriteFailure("OPEN_FAIL", 0, "open", openStartedAt, ex);
            try { transport.Dispose(); }
            catch (Exception cleanup) { throw new PhysicalTransportCleanupException(ex, cleanup); }
            throw;
        }
    }

    private void Open(string path, int transferTimeoutMs, PhysicalHandshakeDiagnostics? diagnostics)
    {
        _ioCancellation.ThrowIfCancellationRequested();
        _diagnostics?.Write(
            "OPEN_BEGIN",
            ("runtime", RuntimeVersion.Current),
            ("targetId", _target.Id),
            ("targetName", _target.Name),
            ("device", _device.ShortId),
            ("timeoutMs", transferTimeoutMs));

        diagnostics?.Stage("native-open");
        _deviceHandle = CreateFileW(
            path,
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileAttributeNormal | FileFlagOverlapped,
            IntPtr.Zero);

        if (_deviceHandle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateFileW(Trofeo) failed");

        _ioCancellation.ThrowIfCancellationRequested();
        diagnostics?.Stage("native-initialize");
        if (!WinUsb_Initialize(_deviceHandle, out _interfaceHandle))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "WinUsb_Initialize failed");

        _ioCancellation.ThrowIfCancellationRequested();
        diagnostics?.Stage("interface-validation");
        ValidateBulkInterface();
        diagnostics?.Stage("configure-out-pipe");
        ConfigurePipe(PipeOut, transferTimeoutMs);
        diagnostics?.Stage("configure-in-pipe");
        ConfigurePipe(PipeIn, transferTimeoutMs);

        _ioCancellation.ThrowIfCancellationRequested();
        var response = Handshake(message => Write(PipeOut, message), count => Read(PipeIn, count), diagnostics);
        _ioCancellation.ThrowIfCancellationRequested();
        var rawPm = response[20];
        var pmRawForCalc = rawPm <= 3 ? 1 : rawPm;
        ProtocolPm = 64 + pmRawForCalc;
        ProtocolSub = response[22] + 1;
        WireRotationDegrees = ProtocolSub is < 2 or > 4 ? 180 : 0;

        _diagnostics?.Write(
            "OPEN_COMPLETE",
            ("runtime", RuntimeVersion.Current),
            ("targetId", _target.Id),
            ("device", _device.ShortId),
            ("pm", ProtocolPm),
            ("sub", ProtocolSub),
            ("wireRotation", WireRotationDegrees));

        _log.Info(
            $"Output target '{_target.Name}' connected to {_device.DisplayName} [{_device.ShortId}]. " +
            $"PM={ProtocolPm}, SUB={ProtocolSub}, wireRotation={WireRotationDegrees}.");
    }

    // Before any protocol command, verify the selected interface actually
    // exposes the established bulk endpoints. No alternate interface probing.
    private void ValidateBulkInterface()
    {
        if (!WinUsb_GetCurrentAlternateSetting(_interfaceHandle, out var alternate) ||
            !WinUsb_QueryInterfaceSettings(_interfaceHandle, 0, out var descriptor))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "LY interface metadata failed.");
        if (alternate != 0 || descriptor.Length != 9 || descriptor.DescriptorType != 4 ||
            descriptor.Number != 0 || descriptor.AlternateSetting != 0 || descriptor.EndpointCount is < 2 or > 32)
            throw new IOException("LY bulk protocol requires interface 0 / alternate setting 0.");
        var pipes = new List<TurzxNativePipe>();
        for (byte index = 0; index < descriptor.EndpointCount; index++)
        {
            if (!WinUsb_QueryPipe(_interfaceHandle, 0, index, out var pipe))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "LY pipe metadata failed.");
            pipes.Add(pipe);
        }
        ValidateBulkPipes(pipes);
    }

    internal static void ValidateBulkPipes(IReadOnlyList<TurzxNativePipe> pipes)
    {
        foreach (var endpoint in new[] { PipeOut, PipeIn })
        {
            var matches = pipes.Where(pipe => pipe.Id == endpoint).ToArray();
            if (matches.Length != 1 || matches[0].Type != 2 || matches[0].MaximumPacketSize == 0)
                throw new IOException("LY requires one bulk OUT 09 and one bulk IN 81 endpoint.");
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct InterfaceDescriptor
    {
        public byte Length, DescriptorType, Number, AlternateSetting, EndpointCount,
            InterfaceClass, InterfaceSubClass, InterfaceProtocol, InterfaceString;
    }

    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_GetCurrentAlternateSetting(IntPtr handle, out byte alternate);
    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_QueryInterfaceSettings(IntPtr handle, byte index, out InterfaceDescriptor descriptor);
    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_QueryPipe(IntPtr handle, byte alternate, byte index, out TurzxNativePipe pipe);

    private void ConfigurePipe(byte pipeId, int timeoutMs)
    {
        var timeout = checked((uint)timeoutMs);
        if (!WinUsb_SetPipePolicy(_interfaceHandle, pipeId, PipeTransferTimeout, sizeof(uint), ref timeout))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"WinUsb_SetPipePolicy(timeout, 0x{pipeId:X2}) failed");

    }

    internal static byte[] Handshake(Action<byte[]> write, Func<int, byte[]> read,
        PhysicalHandshakeDiagnostics? diagnostics = null)
    {
        var message = new byte[2048];
        message[0] = 0x02;
        message[1] = 0xFF;
        message[8] = 0x01;
        diagnostics?.Begin("handshake-write", "write", PipeOut, message.Length);
        write(message);
        diagnostics?.CompleteWrite(message.Length);

        diagnostics?.Begin("handshake-read", "read", PipeIn, 512);
        var response = read(512);
        diagnostics?.CompleteRead(response, response.Length);
        diagnostics?.Stage("handshake-validation");
        if (response.Length < 23 || response[0] != 0x03 || response[1] != 0xFF || response[8] != 0x01)
            throw new InvalidDataException($"Unexpected Trofeo handshake response ({response.Length} bytes).");

        return response;
    }

    public void SendJpeg(ReadOnlySpan<byte> jpeg) =>
        SendJpegWithDiagnostics(jpeg, default);

    public void SendJpegWithDiagnostics(ReadOnlySpan<byte> jpeg, FrameRenderDiagnostics renderDiagnostics)
    {
        if (_interfaceHandle == IntPtr.Zero)
            throw new ObjectDisposedException(nameof(TrofeoTransport));

        var sequence = Interlocked.Increment(ref _frameSequence);
        var startedAt = Stopwatch.GetTimestamp();
        var stage = "prepare";
        var layout = TrofeoWireProtocol.GetFrameLayout(jpeg.Length);
        var writeCount = checked((layout.FrameLength + TrofeoWireProtocol.TransferBlockSize - 1) /
                                 TrofeoWireProtocol.TransferBlockSize);

        _diagnostics?.Write(
            "FRAME_BEGIN",
            ("seq", sequence),
            ("jpegBytes", jpeg.Length),
            ("renderMs", renderDiagnostics.RenderMs),
            ("encodeMs", renderDiagnostics.EncodeMs),
            ("chunks", layout.ChunkCount),
            ("paddedChunks", layout.PaddedChunkCount),
            ("wireBytes", layout.FrameLength),
            ("writes", writeCount));

        try
        {
            var frameLength = PrepareLyFrame(jpeg);
            stage = "write";

            for (var offset = 0; offset < frameLength;)
            {
                var count = TrofeoWireProtocol.GetTransferLength(frameLength - offset);
                WritePinned(PipeOut, IntPtr.Add(_transferBufferAddress, offset), count);
                offset += count;
            }

            _diagnostics?.Write(
                "FRAME_SENT",
                ("seq", sequence),
                ("wireBytes", frameLength),
                ("writes", writeCount),
                ("elapsedMs", Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds));

            stage = "ack";
            var ackLength = ReadInto(PipeIn, _ackBuffer);
            if (ackLength == 0)
                throw new IOException("Trofeo returned an empty frame ACK.");

            _diagnostics?.Write(
                "FRAME_COMPLETE",
                ("seq", sequence),
                ("ackBytes", ackLength),
                ("ackPrefix", AckPrefixHex(ackLength)),
                ("usbMs", Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds));
        }
        catch (Exception ex)
        {
            WriteFailure("FRAME_FAIL", sequence, stage, startedAt, ex);
            throw;
        }
    }

    public void RecordLifecycle(string state, string reason)
    {
        _diagnostics?.Write(
            "LIFECYCLE",
            ("state", state),
            ("reason", reason));
    }

    private string AckPrefixHex(int ackLength)
    {
        var count = Math.Min(16, ackLength);
        return count <= 0 ? string.Empty : Convert.ToHexString(_ackBuffer.AsSpan(0, count));
    }

    private void WriteFailure(string eventName, long sequence, string stage, long startedAt, Exception ex)
    {
        _diagnostics?.Write(
            eventName,
            ("seq", sequence),
            ("stage", stage),
            ("elapsedMs", Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds),
            ("exception", ex.GetType().Name),
            ("win32", ex is Win32Exception win32 ? win32.NativeErrorCode : null),
            ("message", ex.Message));
    }

    private int PrepareLyFrame(ReadOnlySpan<byte> payload)
    {
        var layout = TrofeoWireProtocol.GetFrameLayout(payload.Length);
        EnsureTransferBuffer(layout.FrameLength);
        return TrofeoWireProtocol.WriteFrame(payload, _transferBuffer.AsSpan(0, layout.FrameLength));
    }

    private void EnsureTransferBuffer(int requiredLength)
    {
        if (_transferBuffer.Length >= requiredLength)
            return;

        var capacity = RoundUp(requiredLength, 64 * 1024);
        ReleaseTransferBuffer();

        _transferBuffer = new byte[capacity];
        _transferBufferHandle = GCHandle.Alloc(_transferBuffer, GCHandleType.Pinned);
        _transferBufferPinned = true;
        _transferBufferAddress = _transferBufferHandle.AddrOfPinnedObject();
    }

    private static int RoundUp(int value, int blockSize)
    {
        var blocks = checked((value + blockSize - 1) / blockSize);
        return checked(blocks * blockSize);
    }

    private void ReleaseTransferBuffer()
    {
        if (_transferBufferPinned)
        {
            _transferBufferHandle.Free();
            _transferBufferPinned = false;
        }

        _transferBufferAddress = IntPtr.Zero;
        _transferBuffer = Array.Empty<byte>();
    }

    private void Write(byte pipeId, byte[] data)
    {
        var pinned = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            WritePinned(pipeId, pinned.AddrOfPinnedObject(), data.Length);
        }
        finally
        {
            pinned.Free();
        }
    }

    private void WritePinned(byte pipeId, IntPtr buffer, int length)
    {
        var transferred = TransferPipe(pipeId, buffer, length, isRead: false);
        if (transferred != (uint)length)
            throw new IOException($"Short USB write: {transferred}/{length} bytes.");
    }

    private int ReadInto(byte pipeId, byte[] buffer)
    {
        // WinUSB owns this memory until overlapped completion is observed,
        // including after a cancellation request.
        var pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            return checked((int)TransferPipe(pipeId, pinned.AddrOfPinnedObject(), buffer.Length, isRead: true));
        }
        finally
        {
            pinned.Free();
        }
    }

    private uint TransferPipe(byte pipeId, IntPtr buffer, int length, bool isRead)
    {
        _ioCancellation.ThrowIfCancellationRequested();

        // Open/Send/Dispose share one worker and never overlap two USB requests.
        // Reuse the event and OVERLAPPED only AFTER final completion has been
        // observed; this avoids per-4096-byte native/event allocations.
        _completionEvent ??= new EventWaitHandle(false, EventResetMode.ManualReset);
        if (_nativeOverlapped == IntPtr.Zero)
            _nativeOverlapped = Marshal.AllocHGlobal(Marshal.SizeOf<Win32Overlapped>());

        var completion = _completionEvent;
        var overlapped = _nativeOverlapped;
        completion.Reset();
        Marshal.StructureToPtr(
            new Win32Overlapped { EventHandle = completion.SafeWaitHandle.DangerousGetHandle() },
            overlapped,
            fDeleteOld: false);

        var started = isRead
            ? WinUsb_ReadPipe(
                _interfaceHandle, pipeId, buffer, checked((uint)length), out _, overlapped)
            : WinUsb_WritePipe(
                _interfaceHandle, pipeId, buffer, checked((uint)length), out _, overlapped);

        if (!started)
        {
            var immediateError = Marshal.GetLastWin32Error();
            if (immediateError != ErrorIoPending)
                throw new Win32Exception(immediateError,
                    $"WinUsb_{(isRead ? "Read" : "Write")}Pipe(0x{pipeId:X2}) failed");

            var cancelled = TrofeoOverlappedCompletionWait.Wait(
                completion,
                _ioCancellation,
                () =>
                {
                    // ERROR_NOT_FOUND means completion may have beaten the request.
                    // Either way, the terminal result MUST still be drained.
                    if (!CancelIoEx(_deviceHandle!, overlapped))
                    {
                        var error = Marshal.GetLastWin32Error();
                        if (error != ErrorNotFound)
                            throw new Win32Exception(error, "CancelIoEx(Trofeo) failed");
                    }
                },
                () =>
                {
                    // This waits for actual kernel completion, not just for
                    // CancelIoEx to acknowledge its cancellation request.
                    _ = WinUsb_GetOverlappedResult(_interfaceHandle, overlapped, out _, true);
                });

            if (cancelled)
                throw new OperationCanceledException(_ioCancellation);
        }

        if (!WinUsb_GetOverlappedResult(_interfaceHandle, overlapped, out var transferred, true))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                $"WinUsb_GetOverlappedResult(0x{pipeId:X2}) failed");

        _ioCancellation.ThrowIfCancellationRequested();
        return transferred;
    }

    private byte[] Read(byte pipeId, int length)
    {
        var buffer = new byte[length];
        var transferred = ReadInto(pipeId, buffer);
        if (transferred == buffer.Length)
            return buffer;

        Array.Resize(ref buffer, transferred);
        return buffer;
    }

    public void Dispose()
    {
        _diagnostics?.Write("TRANSPORT_DISPOSE", ("targetId", _target.Id));
        ReleaseTransferBuffer();

        if (_interfaceHandle != IntPtr.Zero)
        {
            WinUsb_Free(_interfaceHandle);
            _interfaceHandle = IntPtr.Zero;
        }

        _deviceHandle?.Dispose();
        _deviceHandle = null;

        // The worker cannot reach Dispose before all submitted native requests
        // have reported terminal completion (including cancellation).
        if (_nativeOverlapped != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_nativeOverlapped);
            _nativeOverlapped = IntPtr.Zero;
        }
        _completionEvent?.Dispose();
        _completionEvent = null;

        _diagnostics?.Dispose();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_Initialize(SafeFileHandle deviceHandle, out IntPtr interfaceHandle);

    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_Free(IntPtr interfaceHandle);

    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_WritePipe(
        IntPtr interfaceHandle,
        byte pipeId,
        IntPtr buffer,
        uint bufferLength,
        out uint lengthTransferred,
        IntPtr overlapped);

    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_ReadPipe(
        IntPtr interfaceHandle,
        byte pipeId,
        IntPtr buffer,
        uint bufferLength,
        out uint lengthTransferred,
        IntPtr overlapped);

    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_GetOverlappedResult(
        IntPtr interfaceHandle,
        IntPtr overlapped,
        out uint lengthTransferred,
        [MarshalAs(UnmanagedType.Bool)] bool wait);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CancelIoEx(SafeFileHandle fileHandle, IntPtr overlapped);

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Overlapped
    {
        public IntPtr Internal;
        public IntPtr InternalHigh;
        public uint Offset;
        public uint OffsetHigh;
        public IntPtr EventHandle;
    }

    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_SetPipePolicy(
        IntPtr interfaceHandle,
        byte pipeId,
        uint policyType,
        uint valueLength,
        ref uint value);
}
