namespace PinkieSysMon;

internal static class RuntimeMetricContract
{
    public const string Version = "system.runtime.version";
    public const string FrameCount = "system.runtime.frame";
    public const string RenderDuration = "system.runtime.render.duration";
    public const string EncodeDuration = "system.runtime.encode.duration";
    public const string UsbDuration = "system.runtime.usb.duration";
    public const string FrameDuration = "system.runtime.frame.duration";
    public const string Fps = "system.runtime.fps";
    public const string JpegSize = "system.runtime.jpeg.size";
    public const string UsbState = "system.runtime.usb";

    public static readonly MetricDescriptor[] Descriptors =
    [
        new(Version, MetricValueKind.Text, MetricUnit.None),
        new(FrameCount, MetricValueKind.Number, MetricUnit.None),
        new(RenderDuration, MetricValueKind.Duration, MetricUnit.Milliseconds),
        new(EncodeDuration, MetricValueKind.Duration, MetricUnit.Milliseconds),
        new(UsbDuration, MetricValueKind.Duration, MetricUnit.Milliseconds),
        new(FrameDuration, MetricValueKind.Duration, MetricUnit.Milliseconds),
        new(Fps, MetricValueKind.Number, MetricUnit.FramesPerSecond),
        new(JpegSize, MetricValueKind.DataSize, MetricUnit.Bytes),
        new(UsbState, MetricValueKind.Text, MetricUnit.None)
    ];
}
