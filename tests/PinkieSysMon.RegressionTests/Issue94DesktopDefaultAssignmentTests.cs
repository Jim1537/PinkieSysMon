using PinkieSysMon;
using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class Issue94DesktopDefaultAssignmentTests
{
    public static void StagingAndReuse()
    {
        Sandbox((root, path, config, original) =>
        {
            var ids = Ids(config);
            var draft = new DesktopSettingsModel(config);
            draft.Create(InitialDesktopDashboardProvisioning.DashboardName);
            Check(config.Outputs.Targets.Count == 1 && !File.Exists(DefaultFile(root)),
                "A new Desktop draft must not mutate saved Outputs or create files before OK.");
            draft.ApplyTo(config);
            using (var provision = InitialDesktopDashboardProvisioning.Prepare(root, path, original, ids, config))
            {
                var board = C.DashboardDefinition.Load(DefaultFile(root));
                Check(board.Canvas.Width == 640 && board.Canvas.Height == 640 &&
                      board.Canvas.Orientation == 0 &&
                      board.Widgets.OfType<C.ValueWidgetDefinition>().Count() >= 3,
                    "The initial Desktop must use the generated canonical 640x640 board.");
                Check(RuntimeConfigurationValidator.LoadAssignments(config, root).Count == 2 &&
                      config.Outputs.Targets[0].Dashboard == "Default",
                    "Existing Output references must remain independent.");
                provision.Commit();
            }
            File.AppendAllText(DefaultFile(root), Environment.NewLine);
            var edited = File.ReadAllBytes(DefaultFile(root));
            var nextIds = Ids(config);
            var next = new DesktopSettingsModel(config);
            next.Create(InitialDesktopDashboardProvisioning.DashboardName);
            next.ApplyTo(config);
            using (var provision = InitialDesktopDashboardProvisioning.Prepare(root, path, original, nextIds, config))
                provision.Commit();
            Check(File.ReadAllBytes(DefaultFile(root)).SequenceEqual(edited) &&
                  config.Outputs.Targets.Count == 3,
                "A second Desktop must reuse the edited default verbatim.");

            var manualIds = Ids(config);
            var manual = new DesktopSettingsModel(config);
            manual.Create("Default");
            manual.ApplyTo(config);
            using (var provision = InitialDesktopDashboardProvisioning.Prepare(root, path, original, manualIds, config))
                provision.Commit();
            Check(File.ReadAllBytes(DefaultFile(root)).SequenceEqual(edited) &&
                  config.Outputs.Targets[^1].Dashboard == "Default",
                "An explicitly chosen dashboard must not trigger default generation.");
        });
    }

    public static void RollbackAndCollisions()
    {
        Sandbox((root, path, config, original) =>
        {
            var ids = Ids(config);
            var draft = new DesktopSettingsModel(config);
            draft.Create(InitialDesktopDashboardProvisioning.DashboardName);
            Check(!Directory.Exists(DefaultDirectory(root)), "Cancel must not create a folder.");
            draft.ApplyTo(config);
            using (InitialDesktopDashboardProvisioning.Prepare(root, path, original, ids, config))
                Check(File.Exists(DefaultFile(root)), "OK-stage must create the missing board.");
            Check(!Directory.Exists(DefaultDirectory(root)) &&
                  File.ReadAllBytes(path).SequenceEqual(original),
                "Failed settings commit must clean unchanged board without touching app config.");

            Directory.CreateDirectory(DefaultDirectory(root));
            Invalid(() => InitialDesktopDashboardProvisioning.Prepare(root, path, original, ids, config));
            Check(!File.Exists(DefaultFile(root)),
                "An incomplete directory is a collision, not permission to overwrite.");
            File.WriteAllText(DefaultFile(root), "{bad json");
            Invalid(() => InitialDesktopDashboardProvisioning.Prepare(root, path, original, ids, config));
            Check(File.ReadAllText(DefaultFile(root)) == "{bad json",
                "Invalid existing default must never be overwritten.");
        });
        Sandbox((root, path, config, original) =>
        {
            // Existing explicit references are not silently repaired.
            config.Outputs.Targets[0].Dashboard = InitialDesktopDashboardProvisioning.DashboardName;
            var ids = Ids(config);
            var draft = new DesktopSettingsModel(config);
            draft.Create(InitialDesktopDashboardProvisioning.DashboardName);
            draft.ApplyTo(config);
            Invalid(() => InitialDesktopDashboardProvisioning.Prepare(root, path, original, ids, config));
            Check(!Directory.Exists(DefaultDirectory(root)),
                "A broken assigned board must remain an explicit error.");
        });
    }

    private static string DefaultDirectory(string root) =>
        Path.Combine(root, "dashboards", InitialDesktopDashboardProvisioning.DashboardName);
    private static string DefaultFile(string root) => Path.Combine(DefaultDirectory(root), "dashboard.json");
    private static HashSet<string> Ids(AppConfig config) =>
        config.Outputs.Targets.Select(t => t.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static void Sandbox(Action<string, string, AppConfig, byte[]> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkiesysmon-issue94-desktop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            EditorForm.WriteNewDashboardFile(Path.Combine(root, "dashboards", "Default"), 640, 640, 0);
            var path = Path.Combine(root, "config", "app.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var config = new AppConfig();
            config.Save(path);
            action(root, path, config, File.ReadAllBytes(path));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void Invalid(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Expected invalid preexisting default to be rejected.");
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
