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
    public PhysicalFrameProfile Profile => PhysicalFrameProfile.Turzx;

    public static TurzxFrameTransport? TryOpen(OutputTargetConfig target, int timeoutMs,
        bool journalEnabled, FileLogger log, CancellationToken token)
    {
        ValidateOptions(target, journalEnabled);
        var transport = TurzxTransport.TryOpen(target, timeoutMs, token);
        return transport is null ? null : new TurzxFrameTransport(transport, token);
    }

    internal static void ValidateOptions(OutputTargetConfig target, bool journalEnabled) =>
        TurzxOutputConfig.ValidateFrameOptions(target.Turzx?.BrightnessPercent, journalEnabled);

    public void SendJpegWithDiagnostics(ReadOnlySpan<byte> jpeg, FrameRenderDiagnostics diagnostics) =>
        _transport.SendImage(TurzxImageFormat.Jpeg, jpeg, _ioToken);

    // Common worker connection/lifecycle events remain in the application log.
    public void RecordLifecycle(string state, string reason) { }
    public void Dispose() => _transport.Dispose();
}

internal static class PhysicalFrameTransportFactory
{
    public static IPhysicalFrameTransport? TryOpen(OutputTargetConfig target, int timeoutMs,
        bool journalEnabled, FileLogger log, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(target);
        token.ThrowIfCancellationRequested();
        if (!target.DeviceEnabled) throw new InvalidOperationException("A disabled physical Output cannot open a frame transport.");
        return (target.Kind, target.Transport) switch
        {
            (OutputTargetContract.TrofeoKind, OutputTargetContract.TrofeoTransport) =>
                TrofeoTransport.TryOpen(target, timeoutMs, journalEnabled, log, token),
            (OutputTargetContract.TurzxKind, OutputTargetContract.TurzxTransport) =>
                TurzxFrameTransport.TryOpen(target, timeoutMs, journalEnabled, log, token),
            _ => throw new InvalidOperationException("Unsupported physical frame transport.")
        };
    }
}
