using PinkieSysMon;
using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class Issue94PhysicalDefaultAssignmentTests
{
    public static void IntegratedProfilesAndReuse()
    {
        Sandbox((root, appPath, config, oldBytes) =>
        {
            var initialIds = Ids(config);
            var trofeo = new OutputTargetConfig
            { Width = 1920, Height = 480,
                Id = "new-trofeo", Name = "Synthetic Trofeo", Kind = OutputTargetContract.PhysicalKind,
                Transport = OutputTargetContract.LyBulkTransport, DeviceId = "usb-interface:" + new string('a', 64),
                Dashboard = "device_1920x480_default"
            };
            var turzx = new OutputTargetConfig
            { Width = 1920, Height = 720,
                Id = "new-turzx", Name = "Synthetic TURZX", Kind = OutputTargetContract.PhysicalKind,
                Transport = OutputTargetContract.EncryptedJpegTransport, DeviceId = "usb-interface:" + new string('b', 64),
                Dashboard = "device_1920x720_default"
            };
            config.Outputs.Targets.Add(trofeo);
            config.Outputs.Targets.Add(turzx);
            using (var pending = InitialOutputDashboardProvisioning.Prepare(
                root, appPath, oldBytes, initialIds, config))
            {
                var one = C.DashboardDefinition.Load(Board(root, trofeo.Dashboard!));
                var two = C.DashboardDefinition.Load(Board(root, turzx.Dashboard!));
                Check(one.Canvas.Width == 1920 && one.Canvas.Height == 480 && one.Canvas.Orientation == 0 &&
                      two.Canvas.Width == 1920 && two.Canvas.Height == 720 && two.Canvas.Orientation == 0,
                    "Integrated devices must generate distinct verified-native default geometry.");
                Check(one.Widgets.Count >= 3 && two.Widgets.Count >= 3,
                    "Physical defaults must contain the required editable canonical widgets.");
                Check(!trofeo.DeviceEnabled && !turzx.DeviceEnabled,
                    "Creating dashboard files must not enable or contact USB hardware.");
                pending.Commit();
            }

            File.AppendAllText(Board(root, trofeo.Dashboard!), Environment.NewLine);
            var edited = File.ReadAllBytes(Board(root, trofeo.Dashboard!));
            var secondIds = Ids(config);
            config.Outputs.Targets.Add(new OutputTargetConfig
            { Width = 1920, Height = 480,
                Id = "another-trofeo", Name = "Another Synthetic", Kind = OutputTargetContract.PhysicalKind,
                Transport = OutputTargetContract.LyBulkTransport, DeviceId = "usb-interface:" + new string('c', 64),
                Dashboard = "device_1920x480_default"
            });
            using (var pending = InitialOutputDashboardProvisioning.Prepare(
                root, appPath, oldBytes, secondIds, config))
                pending.Commit();
            Check(File.ReadAllBytes(Board(root, trofeo.Dashboard!)).SequenceEqual(edited),
                "Reuse must not overwrite a customized physical default.");

            var unusedIds = Ids(config);
            config.Outputs.Targets.Add(new OutputTargetConfig
            { Width = 1920, Height = 480,
                Id = "manual", Name = "Manual Synthetic", Kind = OutputTargetContract.PhysicalKind,
                Transport = OutputTargetContract.LyBulkTransport, DeviceId = "usb-interface:" + new string('d', 64),
                Dashboard = "Existing"
            });
            using (var pending = InitialOutputDashboardProvisioning.Prepare(
                root, appPath, oldBytes, unusedIds, config))
                pending.Commit();
            Check(File.ReadAllBytes(Board(root, trofeo.Dashboard!)).SequenceEqual(edited),
                "Manual physical assignment must not regenerate the default.");
        });
    }

    public static void FailureAndUnsupportedProfiles()
    {
        Sandbox((root, appPath, config, oldBytes) =>
        {
            var ids = Ids(config);
            config.Outputs.Targets.Add(new OutputTargetConfig
            { Width = 1920, Height = 480,
                Id = "new", Name = "Synthetic Trofeo", Kind = OutputTargetContract.PhysicalKind,
                Transport = OutputTargetContract.LyBulkTransport, DeviceId = "usb-interface:" + new string('a', 64),
                Dashboard = "device_1920x480_default"
            });
            using (InitialOutputDashboardProvisioning.Prepare(root, appPath, oldBytes, ids, config))
                Check(File.Exists(Board(root, "device_1920x480_default")), "OK phase must create the board.");
            Check(!File.Exists(Board(root, "device_1920x480_default")),
                "Failed Settings commit must revert the untouched generated board.");

            var folder = Path.GetDirectoryName(Board(root, "device_1920x480_default"))!;
            Directory.CreateDirectory(folder);
            Invalid(() => InitialOutputDashboardProvisioning.Prepare(root, appPath, oldBytes, ids, config));
            File.WriteAllText(Board(root, "device_1920x480_default"), "{ bad");
            Invalid(() => InitialOutputDashboardProvisioning.Prepare(root, appPath, oldBytes, ids, config));
            Check(File.ReadAllText(Board(root, "device_1920x480_default")) == "{ bad",
                "Collision or invalid board must not be overwritten.");

            var unknown = new OutputTargetConfig
            {
                Name = "Unprofiled integrated future device", Kind = "future", Transport = "future"
            };
            try { _ = OutputDefaultDashboardPolicy.PhysicalProfile(unknown); }
            catch (InvalidDataException) { return; }
            throw new InvalidOperationException("Unknown native geometry must not be guessed.");
        });
    }

    private static string Board(string root, string name) =>
        Path.Combine(root, "dashboards", name, "dashboard.json");
    private static HashSet<string> Ids(AppConfig config) =>
        config.Outputs.Targets.Select(t => t.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static void Sandbox(Action<string, string, AppConfig, byte[]> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkiesysmon-issue94-physical-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            EditorForm.WriteNewDashboardFile(
                Path.Combine(root, "dashboards", "Default"), 640, 640, 0);
            EditorForm.WriteNewDashboardFile(
                Path.Combine(root, "dashboards", "Existing"), 640, 640, 0);
            var path = Path.Combine(root, "config", "app.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var config = new AppConfig();
            // A previously configured Output with its own valid manual board.
            config.Outputs.Targets[0].Dashboard = "Default";
            config.Outputs.Targets[0].Desktop!.BoardEnabled = true;
            config.Save(path);
            action(root, path, config, File.ReadAllBytes(path));
        }
        finally { Directory.Delete(root, true); }
    }

    private static void Invalid(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Expected invalid generated-dashboard collision.");
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
