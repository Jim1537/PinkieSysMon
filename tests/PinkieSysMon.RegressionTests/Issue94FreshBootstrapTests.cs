using PinkieSysMon;
using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class Issue94FreshBootstrapTests
{
    public static void FirstDesktopAndNoRepair()
    {
        Sandbox((root, path, original) =>
        {
            var config = AppConfig.Load(path, new FileLogger(Path.Combine(root, "test.log")));
            var previousIds = Ids(config);
            var first = config.Outputs.Targets.Single();
            Check(!OutputTargetContract.IsEnabled(first) &&
                  first.Dashboard == OutputDefaultDashboardPolicy.DesktopName,
                "First-install Desktop is an inactive unprovisioned placeholder.");
            first.Desktop!.BoardEnabled = true; // Explicit first Settings OK.
            Check(!File.Exists(Board(root, OutputDefaultDashboardPolicy.DesktopName)),
                "Opening the Settings draft must not create the board.");
            using (var pending = InitialOutputDashboardProvisioning.Prepare(
                       root, path, original, previousIds, config))
            {
                var board = C.DashboardDefinition.Load(Board(root, OutputDefaultDashboardPolicy.DesktopName));
                Check(board.Canvas.Width == 640 && board.Canvas.Height == 640 &&
                      RuntimeConfigurationValidator.LoadAssignments(config, root).Count == 1,
                    "First confirmed Desktop assignment must materialize before config validation.");
                config.Save(path);
                pending.Commit();
            }

            // Once the config was normalized and committed it is no longer a
            // bootstrap placeholder, even if that board goes missing later.
            File.Delete(Board(root, OutputDefaultDashboardPolicy.DesktopName));
            var stored = File.ReadAllBytes(path);
            var second = AppConfig.Load(path, new FileLogger(Path.Combine(root, "test2.log")));
            var secondIds = Ids(second);
            // Stage the new Output in the detached working model.
            var draft = new DesktopSettingsModel(second);
            draft.Create(OutputDefaultDashboardPolicy.DesktopName);
            draft.ApplyTo(second);
            var rejected = false;
            try
            {
                using var unexpected = InitialOutputDashboardProvisioning.Prepare(
                    root, path, stored, secondIds, second);
            }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && !File.Exists(Board(root, OutputDefaultDashboardPolicy.DesktopName)),
                "A committed-but-deleted board must remain an explicit missing-assignment error.");
        });
    }

    public static void PhysicalFirstAndCancel()
    {
        Sandbox((root, path, original) =>
        {
            var config = AppConfig.Load(path, new FileLogger(Path.Combine(root, "test.log")));
            var ids = Ids(config);
            var physical = new OutputTargetConfig
            {
                Id = "new-physical", Name = "Trofeo",
                Kind = OutputTargetContract.TrofeoKind,
                Transport = OutputTargetContract.TrofeoTransport,
                DeviceId = "trofeo:" + new string('a', 64),
                Dashboard = OutputDefaultDashboardPolicy.TrofeoName
            };
            config.Outputs.Targets.Add(physical);
            Check(!File.Exists(Board(root, OutputDefaultDashboardPolicy.DesktopName)) &&
                  !File.Exists(Board(root, OutputDefaultDashboardPolicy.TrofeoName)),
                "Draft adoption and Cancel must not create dashboard files.");
            using (var pending = InitialOutputDashboardProvisioning.Prepare(root, path, original, ids, config))
            {
                Check(File.Exists(Board(root, OutputDefaultDashboardPolicy.DesktopName)) &&
                      File.Exists(Board(root, OutputDefaultDashboardPolicy.TrofeoName)) &&
                      RuntimeConfigurationValidator.LoadAssignments(config, root).Count == 2 &&
                      !physical.DeviceEnabled,
                    "First physical Settings OK provisions the pending Desktop and native board without USB.");
                // Simulated settings validation/apply failure: no Commit().
            }
            Check(!File.Exists(Board(root, OutputDefaultDashboardPolicy.DesktopName)) &&
                  !File.Exists(Board(root, OutputDefaultDashboardPolicy.TrofeoName)) &&
                  File.ReadAllBytes(path).SequenceEqual(original),
                "Failed first-time transaction removes only its own unchanged files.");
        });
    }

    private static string Board(string root, string name) =>
        Path.Combine(root, "dashboards", name, "dashboard.json");
    private static HashSet<string> Ids(AppConfig cfg) =>
        cfg.Outputs.Targets.Select(t => t.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static void Sandbox(Action<string, string, byte[]> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkiesysmon-issue94-clean-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "config", "app.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """
                {
                  "Outputs": {"Targets": [{
                    "Id": "desktop-1",
                    "Name": "Desktop 1",
                    "Kind": "desktop",
                    "Transport": "desktop",
                    "Dashboard": "desktop_default",
                    "ProviderEnabled": false,
                    "Desktop": {"BoardEnabled": false, "Lock": true, "X": 0, "Y": 0, "ZOrder": 0}
                  }]}
                }
                """);
            action(root, path, File.ReadAllBytes(path));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }
}
