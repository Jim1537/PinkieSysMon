using Microsoft.Win32.SafeHandles;
using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PinkieSysMon;

internal sealed class TrofeoTransport : IDisposable
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

    private readonly FileLogger _log;
    private readonly OutputTargetConfig _target;
    private readonly OutputDeviceDescriptor _device;
    private readonly byte[] _ackBuffer = new byte[512];
    private SafeFileHandle? _deviceHandle;
    private IntPtr _interfaceHandle;
    private byte[] _transferBuffer = Array.Empty<byte>();
    private GCHandle _transferBufferHandle;
    private bool _transferBufferPinned;
    private IntPtr _transferBufferAddress;

    private TrofeoTransport(FileLogger log, OutputTargetConfig target, OutputDeviceDescriptor device)
    {
        _log = log;
        _target = target;
        _device = device;
    }

    public int WireRotationDegrees { get; private set; }
    public int ProtocolPm { get; private set; }
    public int ProtocolSub { get; private set; }

    public static TrofeoTransport? TryOpen(OutputTargetConfig target, int transferTimeoutMs, FileLogger log)
    {
        ArgumentNullException.ThrowIfNull(target);

        var discoveredDevices = DeviceDiscovery.EnumerateTrofeoDevices();
        var resolution = OutputDeviceSelection.Resolve(target, discoveredDevices);
        if (resolution.Status == OutputDeviceResolutionStatus.Missing)
            return null;
        if (resolution.Status != OutputDeviceResolutionStatus.Found || resolution.Device is null)
            throw new OutputDeviceResolutionException(resolution.Status, resolution.Message);

        var transport = new TrofeoTransport(log, target, resolution.Device);
        try
        {
            transport.Open(resolution.Device.DevicePath, transferTimeoutMs);
            return transport;
        }
        catch
        {
            transport.Dispose();
            throw;
        }
    }

    private void Open(string path, int transferTimeoutMs)
    {
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

        if (!WinUsb_Initialize(_deviceHandle, out _interfaceHandle))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "WinUsb_Initialize failed");

        ConfigurePipe(PipeOut, transferTimeoutMs);
        ConfigurePipe(PipeIn, transferTimeoutMs);

        var response = Handshake();
        var rawPm = response[20];
        var pmRawForCalc = rawPm <= 3 ? 1 : rawPm;
        ProtocolPm = 64 + pmRawForCalc;
        ProtocolSub = response[22] + 1;
        WireRotationDegrees = ProtocolSub is < 2 or > 4 ? 180 : 0;

        _log.Info(
            $"Output target '{_target.Name}' connected to {_device.DisplayName} [{_device.ShortId}]. " +
            $"PM={ProtocolPm}, SUB={ProtocolSub}, wireRotation={WireRotationDegrees}.");
    }

    private void ConfigurePipe(byte pipeId, int timeoutMs)
    {
        var timeout = checked((uint)timeoutMs);
        if (!WinUsb_SetPipePolicy(_interfaceHandle, pipeId, PipeTransferTimeout, sizeof(uint), ref timeout))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"WinUsb_SetPipePolicy(timeout, 0x{pipeId:X2}) failed");

    }

    private byte[] Handshake()
    {
        var message = new byte[2048];
        message[0] = 0x02;
        message[1] = 0xFF;
        message[8] = 0x01;
        Write(PipeOut, message);

        var response = Read(PipeIn, 512);
        if (response.Length < 23 || response[0] != 0x03 || response[1] != 0xFF || response[8] != 0x01)
            throw new InvalidDataException($"Unexpected Trofeo handshake response ({response.Length} bytes).");

        return response;
    }

    public void SendJpeg(ReadOnlySpan<byte> jpeg)
    {
        if (_interfaceHandle == IntPtr.Zero)
            throw new ObjectDisposedException(nameof(TrofeoTransport));

        var frameLength = PrepareLyFrame(jpeg);
        for (var offset = 0; offset < frameLength;)
        {
            var count = Math.Min(4096, frameLength - offset);
            WritePinned(PipeOut, IntPtr.Add(_transferBufferAddress, offset), count);
            offset += count;
        }

        var ackLength = ReadInto(PipeIn, _ackBuffer);
        if (ackLength == 0)
            throw new IOException("Trofeo returned an empty frame ACK.");
    }

    private int PrepareLyFrame(ReadOnlySpan<byte> payload)
    {
        const int chunkSize = 512;
        const int headerSize = 16;
        const int dataSize = 496;

        var totalSize = payload.Length;
        var numChunks = totalSize / dataSize + 1;
        var lastData = totalSize % dataSize;

        var paddedChunks = numChunks;
        var remainder = paddedChunks % 4;
        if (remainder != 0)
            paddedChunks += 4 - remainder;

        var frameLength = checked(paddedChunks * chunkSize);
        EnsureTransferBuffer(frameLength);
        var output = _transferBuffer.AsSpan(0, frameLength);

        for (var i = 0; i < numChunks; i++)
        {
            var offset = i * chunkSize;
            var isLast = i == numChunks - 1;
            var dataLength = isLast ? lastData : dataSize;
            var chunk = output.Slice(offset, chunkSize);

            // Header has three reserved bytes that must not retain data from the previous frame.
            chunk.Slice(0, headerSize).Clear();
            chunk[0] = 0x01;
            chunk[1] = 0xFF;
            BinaryPrimitives.WriteUInt32LittleEndian(chunk.Slice(2, 4), checked((uint)totalSize));
            BinaryPrimitives.WriteUInt16LittleEndian(chunk.Slice(6, 2), checked((ushort)dataLength));
            chunk[8] = 0x01;
            BinaryPrimitives.WriteUInt16LittleEndian(chunk.Slice(9, 2), checked((ushort)numChunks));
            BinaryPrimitives.WriteUInt16LittleEndian(chunk.Slice(11, 2), checked((ushort)i));

            if (dataLength > 0)
                payload.Slice(i * dataSize, dataLength).CopyTo(chunk.Slice(headerSize, dataLength));

            if (dataLength < dataSize)
                chunk.Slice(headerSize + dataLength, dataSize - dataLength).Clear();
        }

        if (paddedChunks > numChunks)
            output.Slice(numChunks * chunkSize, (paddedChunks - numChunks) * chunkSize).Clear();

        return frameLength;
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
        if (!WinUsb_WritePipe(
                _interfaceHandle,
                pipeId,
                buffer,
                checked((uint)length),
                out var transferred,
                IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"WinUsb_WritePipe(0x{pipeId:X2}) failed");
        }

        if (transferred != (uint)length)
            throw new IOException($"Short USB write: {transferred}/{length} bytes.");
    }

    private int ReadInto(byte pipeId, byte[] buffer)
    {
        if (!WinUsb_ReadPipe(
                _interfaceHandle,
                pipeId,
                buffer,
                checked((uint)buffer.Length),
                out var transferred,
                IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"WinUsb_ReadPipe(0x{pipeId:X2}) failed");
        }

        return checked((int)transferred);
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
        ReleaseTransferBuffer();

        if (_interfaceHandle != IntPtr.Zero)
        {
            WinUsb_Free(_interfaceHandle);
            _interfaceHandle = IntPtr.Zero;
        }

        _deviceHandle?.Dispose();
        _deviceHandle = null;
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
        byte[] buffer,
        uint bufferLength,
        out uint lengthTransferred,
        IntPtr overlapped);

    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_SetPipePolicy(
        IntPtr interfaceHandle,
        byte pipeId,
        uint policyType,
        uint valueLength,
        ref uint value);
}
