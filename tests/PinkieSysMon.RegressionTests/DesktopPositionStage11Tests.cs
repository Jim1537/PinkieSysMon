using System.Drawing;
using System.Text.Json.Nodes;
using PinkieSysMon;

namespace PinkieSysMon.RegressionTests;

internal static class DesktopPositionStage11Tests
{
    public static void MonitorRecovery()
    {
        var primary = new Rectangle(0, 0, 1920, 1040);
        var secondary = new Rectangle(-1600, -200, 1600, 900);
        var monitors = new[] { primary, secondary };
        var canvas = new Size(640, 320);
        Check(DesktopPlacement.Recover(new Point(-1400, -120), canvas, monitors, primary) ==
              new Point(-1400, -120), "Negative secondary-monitor placement must remain valid.");
        Check(DesktopPlacement.Recover(new Point(2100, 200), canvas, monitors, primary) ==
              primary.Location, "Disconnected monitor must recover to primary.");
        Check(DesktopPlacement.Recover(new Point(1880, 20), canvas, monitors, primary) ==
              primary.Location, "Tiny visible strip must trigger recovery.");
        Check(DesktopPlacement.Recover(new Point(1824, 20), canvas, monitors, primary) ==
              new Point(1824, 20), "Sufficiently visible border must preserve placement.");
        Check(DesktopPlacement.Recover(new Point(200, 400), new Size(2400, 900),
              monitors, primary) == new Point(200, 400),
              "Oversized canonical Canvas must remain at its coordinates.");
        Check(DesktopPlacement.Recover(new Point(-3000, 400), canvas,
              new[] { primary }, primary) == primary.Location,
              "Detached negative-coordinate screen must be recoverable.");
    }

    public static void ScopedPersistence()
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkiesysmon-position11-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "app.json");
        try
        {
            File.WriteAllText(path, """
                {
                  "PrivateUnknown": {"Keep": 17},
                  "Outputs": { "Targets": [
                    {"Id": "screen-1", "Kind": "trofeo", "DeviceEnabled": true, "DeviceId": "private-marker"},
                    {"Id": "desk-1", "Kind": "desktop", "Dashboard": "PrivateBoard",
                     "Desktop": {"BoardEnabled": true, "Lock": false, "X": 0, "Y": 0, "Custom": "keep"}},
                    {"Id": "desk-2", "Kind": "desktop", "Desktop": {"X": 3, "Y": 4}}
                  ]}
                }
                """);
            DesktopPositionStore.Save(path, "desk-1", new Point(-480, 125));
            var result = JsonNode.Parse(File.ReadAllText(path))!;
            var targets = result["Outputs"]!["Targets"]!;
            Check((int)targets[1]!["Desktop"]!["X"]! == -480 &&
                  (int)targets[1]!["Desktop"]!["Y"]! == 125,
                  "Per-Desktop X/Y must be persisted exactly.");
            Check((int)result["PrivateUnknown"]!["Keep"]! == 17 &&
                  (string)targets[1]!["Desktop"]!["Custom"]! == "keep" &&
                  (string)targets[1]!["Dashboard"]! == "PrivateBoard" &&
                  (int)targets[2]!["Desktop"]!["X"]! == 3 &&
                  (string)targets[0]!["DeviceId"]! == "private-marker",
                  "Preserve unrelated physical/device/private fields and dashboard.");
            var before = File.ReadAllText(path);
            var rejected = false;
            try { DesktopPositionStore.Save(path, "screen-1", new Point(7, 8)); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && before == File.ReadAllText(path),
                  "Physical target must never be written as Desktop placement.");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static void Check(bool ok, string message)
    {
        if (!ok)
            throw new InvalidOperationException(message);
    }
}
