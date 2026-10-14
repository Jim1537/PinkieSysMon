using System.Text.Json;

namespace PinkieSysMon.RegressionTests;

// Detached configuration transitions for lifecycle fixtures. They replace the
// retired manual target Stop without mutating the manager's committed objects.
internal static class OutputLifecycleTestConfiguration
{
    public static AppConfig WithEnabled(AppConfig config, string targetId, bool enabled)
    {
        var next = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(config))
            ?? throw new InvalidOperationException("Could not copy lifecycle fixture configuration.");
        var target = next.Outputs.Targets.Single(t => t.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase));
        if (target.Kind == OutputTargetContract.DesktopKind)
        {
            target.Desktop ??= new DesktopOutputConfig();
            target.Desktop.BoardEnabled = enabled;
        }
        else if (target.Kind == OutputTargetContract.TrofeoKind)
            target.DeviceEnabled = enabled;
        else
            throw new InvalidOperationException("Unsupported lifecycle fixture Output kind.");
        return next;
    }
}
