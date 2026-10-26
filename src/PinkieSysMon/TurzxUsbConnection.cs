using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PinkieSysMon;

// Native seam owns handles; the connection owns pinning, OVERLAPPED and event.
internal interface ITurzxNativeIo : IDisposable
{
    bool Submit(bool read, byte pipe, IntPtr buffer, uint length, IntPtr overlapped, out int error);
    bool Complete(IntPtr overlapped, out uint transferred, out int error);
    void Cancel(IntPtr overlapped);
}

internal sealed class TurzxUsbConnection : ITurzxUsbConnection
{
    public const byte PipeOut = 0x01;
    public const byte PipeIn = 0x81;
    private readonly object _gate = new();
    private readonly ITurzxNativeIo _native;
    private readonly PhysicalIoDiagnostics? _ioDiagnostics;
    private readonly int _timeoutMs;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly EventWaitHandle _completion;
    private IntPtr _overlapped;
    private int _closing;

    public TurzxUsbConnection(ITurzxNativeIo native, int timeoutMs, PhysicalIoDiagnostics? ioDiagnostics = null)
    {
        ValidateTimeout(timeoutMs);
        _native = native ?? throw new ArgumentNullException(nameof(native));
        _ioDiagnostics = ioDiagnostics;
        _timeoutMs = timeoutMs;
        try { _completion = new(false, EventResetMode.ManualReset); }
        catch { _native.Dispose(); _shutdown.Dispose(); throw; }
    }

    internal static void ValidateTimeout(int timeoutMs)
    {
        if (timeoutMs < 500) throw new ArgumentOutOfRangeException(nameof(timeoutMs), "TURZX USB timeout must be at least 500 ms.");
    }

    public int Write(byte[] packet, CancellationToken token) => Transfer(packet, false, token);
    public int Read(byte[] buffer, CancellationToken token) => Transfer(buffer, true, token);

    private int Transfer(byte[] buffer, bool read, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.Length < 1 || buffer.Length > (read ? TurzxWireProtocol.HeaderLength :
            TurzxWireProtocol.HeaderLength + TurzxWireProtocol.MaximumImageBytes))
            throw new ArgumentOutOfRangeException(nameof(buffer));
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing != 0, this);
            using var deadline = new CancellationTokenSource(_timeoutMs);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdown.Token, deadline.Token);
            linked.Token.ThrowIfCancellationRequested();
            _overlapped = _overlapped == IntPtr.Zero ? Marshal.AllocHGlobal(Marshal.SizeOf<TurzxNativeOverlapped>()) : _overlapped;
            _completion.Reset();
            Marshal.StructureToPtr(new TurzxNativeOverlapped
            { EventHandle = _completion.SafeWaitHandle.DangerousGetHandle() }, _overlapped, false);
            var pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                _ioDiagnostics?.Stage("native.submit");
                var immediate = _native.Submit(read, read ? PipeIn : PipeOut, pinned.AddrOfPinnedObject(),
                    (uint)buffer.Length, _overlapped, out var submitError);
                if (!immediate && submitError != 997)
                {
                    _ioDiagnostics?.IoResult(false, 0, submitError, "submit-failed");
                    throw Failure(submitError, "submit");
                }
                bool completed;
                uint transferred;
                int completionError;
                try
                {
                    _ioDiagnostics?.Stage(immediate ? "native.immediate" : "native.pending");
                    if (!immediate && WaitHandle.WaitAny([_completion, linked.Token.WaitHandle]) == 1)
                    {
                        _ioDiagnostics?.Stage(deadline.IsCancellationRequested ? "native.deadline.cancel" : "native.cancel");
                        try { _native.Cancel(_overlapped); }
                        catch (Exception error) { _ioDiagnostics?.CancellationFailure(error); throw; }
                    }
                }
                finally
                {
                    // CancelIoEx is only a request. Complete MUST wait for the
                    // terminal native result, even if cancellation itself fails.
                    _ioDiagnostics?.Stage("native.terminal-completion");
                    completed = _native.Complete(_overlapped, out transferred, out completionError);
                    _ioDiagnostics?.IoResult(completed, transferred, completionError, "terminal");
                    _ioDiagnostics?.Stage("native.returned");
                }
                if (token.IsCancellationRequested) throw new OperationCanceledException(token);
                if (_shutdown.IsCancellationRequested) throw new OperationCanceledException(_shutdown.Token);
                if (deadline.IsCancellationRequested)
                    throw new TimeoutException("TURZX USB transfer deadline expired after terminal completion.");
                if (!completed) throw Failure(completionError, "completion");
                if (transferred > buffer.Length) throw new IOException("TURZX USB transfer returned an invalid byte count.");
                return (int)transferred;
            }
            finally { pinned.Free(); }
        }
    }

    private static Exception Failure(int error, string operation) => error == 121
        ? new TimeoutException($"TURZX USB {operation} timed out.", new Win32Exception(error))
        : new Win32Exception(error, $"TURZX USB {operation} failed.");

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _closing, 1) != 0) return;
        _shutdown.Cancel();
        lock (_gate)
        {
            // No method can reach here while the native side owns a buffer.
            try { _native.Dispose(); }
            finally
            {
                if (_overlapped != IntPtr.Zero) Marshal.FreeHGlobal(_overlapped);
                _overlapped = IntPtr.Zero;
                _completion.Dispose();
                _shutdown.Dispose();
            }
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct TurzxNativeOverlapped
{
    public IntPtr Internal;
    public IntPtr InternalHigh;
    public uint Offset;
    public uint OffsetHigh;
    public IntPtr EventHandle;
}
