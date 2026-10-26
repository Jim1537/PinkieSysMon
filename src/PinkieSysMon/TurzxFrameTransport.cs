namespace PinkieSysMon;

// Adapter for the shared physical worker. The inner transport owns strict I/O.
internal sealed class TurzxFrameTransport : IPhysicalFrameTransport
{
    private readonly TurzxTransport _transport;
    private readonly CancellationToken _ioToken;

    internal TurzxFrameTransport(TurzxTransport transport, CancellationToken ioToken)
    {
        ArgumentNullException.ThrowIfNull(transport);
        if (transport.ReplyMode != TurzxReplyMode.JpegStream)
            throw new ArgumentException("TURZX frame output requires the JPEG stream reply policy.", nameof(transport));
        _transport = transport;
        _ioToken = ioToken;
    }

    public int WireRotationDegrees => 90;
    public PhysicalFrameProfile Profile => PhysicalFrameProfile.EncryptedJpeg;

    public static TurzxFrameTransport? TryOpen(OutputTargetConfig target, int timeoutMs,
        FileLogger log, CancellationToken token, PhysicalHandshakeDiagnostics? diagnostics = null)
    {
        diagnostics?.Stage("options");
        ValidateOptions(target);
        var transport = TurzxTransport.TryOpen(target, timeoutMs, token, diagnostics);
        return transport is null ? null : new TurzxFrameTransport(transport, token);
    }

    internal static void ValidateOptions(OutputTargetConfig target) =>
        EncryptedJpegOutputConfig.ValidateFrameOptions(target.EncryptedJpeg?.BrightnessPercent);

    public void SendJpegWithDiagnostics(ReadOnlySpan<byte> jpeg, FrameRenderDiagnostics diagnostics) =>
        _transport.SendImage(TurzxImageFormat.Jpeg, jpeg, _ioToken);

    // Common worker connection/lifecycle events remain in the application log.
    public void RecordLifecycle(string state, string reason) { }
    public void Dispose() => _transport.Dispose();
}

internal static class PhysicalFrameTransportFactory
{
    public static IPhysicalFrameTransport? TryOpen(OutputTargetConfig target, int timeoutMs,
        FileLogger log, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(target);
        token.ThrowIfCancellationRequested();
        if (!target.DeviceEnabled) throw new InvalidOperationException("A disabled physical Output cannot open a frame transport.");
        return PhysicalHandshakeDiagnostics.Observe<IPhysicalFrameTransport?>(target, timeoutMs, token, diagnostics =>
            (target.Kind, target.Transport) switch
        {
            (OutputTargetContract.PhysicalKind, OutputTargetContract.LyBulkTransport) =>
                TrofeoTransport.TryOpen(target, timeoutMs, log, token, diagnostics),
            (OutputTargetContract.PhysicalKind, OutputTargetContract.EncryptedJpegTransport) =>
                TurzxFrameTransport.TryOpen(target, timeoutMs, log, token, diagnostics),
            _ => throw new InvalidOperationException("Unsupported physical frame transport.")
        }, (message, error) => log.Error(message, error));
    }
}
