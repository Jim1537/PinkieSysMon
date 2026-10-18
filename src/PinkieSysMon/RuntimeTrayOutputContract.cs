using System.Globalization;

namespace PinkieSysMon;

// Read-only presentation policy shared by the native tray and deterministic checks.
internal static class RuntimeTrayOutputContract
{
    public static bool CanUseActions(bool? outputEnabled) => outputEnabled == true;

    public static (string Count, string Time, string JpegKiB) FrameValues(OutputSessionStatus output)
    {
        var ready = output.Kind switch
        {
            OutputTargetContract.DesktopKind => output.IsVisible == true &&
                output.UsbState.Equals("DISPLAYING", StringComparison.OrdinalIgnoreCase),
            OutputTargetContract.TrofeoKind or OutputTargetContract.TurzxKind => output.UsbState.Equals("CONNECTED", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
        if (!output.OutputEnabled || !output.IsActive || !output.IsConnected || !ready || output.SentFrames <= 0)
            return ("N/A", "N/A", "N/A");
        return (output.SentFrames.ToString(CultureInfo.InvariantCulture),
            double.IsFinite(output.LastFrameMs) && output.LastFrameMs >= 0
                ? output.LastFrameMs.ToString("F1", CultureInfo.InvariantCulture) : "N/A",
            OutputTargetContract.IsPhysicalKind(output.Kind) && output.LastJpegBytes > 0
                ? (output.LastJpegBytes / 1024.0).ToString("F1", CultureInfo.InvariantCulture) : "N/A");
    }

    public static string Summary(RuntimeStatusSnapshot status)
    {
        var outputs = status.Outputs;
        var visible = outputs.Count(o => o.Kind == OutputTargetContract.DesktopKind && o.IsVisible == true);
        var connectedDevices = outputs.Count(o => OutputTargetContract.IsPhysicalKind(o.Kind) && o.IsConnected);
        return $"{outputs.Length} configured · {outputs.Count(o => o.IsActive)} active · " +
            $"{visible} Desktop visible · {connectedDevices} devices connected";
    }

    public static string Tooltip(RuntimeStatusSnapshot status) =>
        $"PinkieSysMon - {status.Outputs.Count(o => o.IsActive)} active Outputs";
}
