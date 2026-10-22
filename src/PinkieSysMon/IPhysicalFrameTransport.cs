namespace PinkieSysMon;

// A frame pump owns exactly one transport at a time. Only its worker performs
// Open/Send/Dispose; lifecycle calls publish intent without disposing an in-flight handle.
internal interface IPhysicalFrameTransport : IDisposable
{
    int WireRotationDegrees { get; }
    PhysicalFrameProfile Profile => new(WireRotationDegrees, TrofeoWireProtocol.MaximumImageBytes);
    void SendJpegWithDiagnostics(ReadOnlySpan<byte> jpeg, FrameRenderDiagnostics diagnostics);
    void RecordLifecycle(string state, string reason);
}

internal readonly record struct FrameRenderDiagnostics(double RenderMs, double EncodeMs);
