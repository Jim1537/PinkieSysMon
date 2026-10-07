namespace PinkieSysMon;

// A frame pump owns exactly one transport at a time. Only its worker performs
// Open/Send/Dispose; lifecycle calls publish intent without disposing an in-flight handle.
internal interface ITrofeoFrameTransport : IDisposable
{
    int WireRotationDegrees { get; }
    void SendJpegWithDiagnostics(ReadOnlySpan<byte> jpeg, TrofeoFrameRenderDiagnostics diagnostics);
    void RecordLifecycle(string state, string reason);
}
