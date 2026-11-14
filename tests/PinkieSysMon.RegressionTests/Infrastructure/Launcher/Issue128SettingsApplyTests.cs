using PinkieSysMon.Launcher;

namespace PinkieSysMon.RegressionTests;

internal static class Issue128SettingsApplyTests
{
    public static void NoOpSkipsEveryProcess()
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkie-issue128-noop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "config"));
        try
        {
            var app = Path.Combine(root, "config", "app.json");
            File.WriteAllText(app, "{\"Outputs\":{\"Targets\":[]}}");
            var draft = LauncherSettingsDraft.Open(root, _ => false);
            var oldBytes = File.ReadAllBytes(app);
            var result = LauncherSettingsCoordinator.Apply(draft);
            if (!result.Success || !result.NoChanges || result.Partial ||
                result.EditorsRestored != 0 || !File.ReadAllBytes(app).AsSpan().SequenceEqual(oldBytes) ||
                File.Exists(Path.Combine(root, "config", "editor.json")))
                throw new InvalidOperationException("A no-op Settings OK must not persist, close Editors or touch Runtime.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
