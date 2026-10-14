namespace PinkieSysMon;

// Read-only presentation policy shared by the native tray and deterministic checks.
internal static class RuntimeTrayOutputContract
{
    public static bool CanStartOrReload(RuntimeOutputStatus output) => output.OutputEnabled == true;

    public static string Summary(RuntimeStatusSnapshot status)
    {
        var outputs = status.Outputs;
        var visible = outputs.Count(o => o.Kind == OutputTargetContract.DesktopKind && o.IsVisible == true);
        var connectedDevices = outputs.Count(o => o.Kind == OutputTargetContract.TrofeoKind && o.IsConnected);
        return $"{outputs.Length} configured · {outputs.Count(o => o.IsActive)} active · " +
            $"{visible} Desktop visible · {connectedDevices} devices connected";
    }

    public static string Tooltip(RuntimeStatusSnapshot status) =>
        $"PinkieSysMon - {status.Outputs.Count(o => o.IsActive)} active Outputs";
}
