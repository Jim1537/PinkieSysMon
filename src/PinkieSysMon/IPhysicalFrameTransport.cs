namespace PinkieSysMon;

// A frame pump owns exactly one transport at a time. Only its worker performs
// Open/Send/Dispose; lifecycle calls publish intent without disposing an in-flight handle.
internal interface IPhysicalFrameTransport : IDisposable
{
    int WireRotationDegrees { get; }
    PhysicalFrameProfile Profile => new(WireRotationDegrees, TrofeoWireProtocol.MaximumImageBytes);
    void SendJpegWithDiagnostics(ReadOnlySpan<byte> jpeg, FrameRenderDiagnostics diagnostics);
    // The physical worker serializes committed settings with frame transmission.
    // Adapters without hardware brightness accept only the no-op null state.
    void ApplyBrightnessPercentIfChanged(int? percent)
    {
        if (percent is not null)
            throw new NotSupportedException("This physical transport has no verified hardware brightness control.");
    }
    void RecordLifecycle(string state, string reason);
}

internal readonly record struct FrameRenderDiagnostics(double RenderMs, double EncodeMs);

// Failed acquisition cleanup cannot be reported as released by a transient Test.
internal sealed class PhysicalTransportCleanupException(Exception operation, Exception cleanup)
    : AggregateException("Physical transport cleanup could not be confirmed.", operation, cleanup);
