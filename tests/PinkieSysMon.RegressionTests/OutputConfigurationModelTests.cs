using PinkieSysMon;

namespace PinkieSysMon.RegressionTests;

internal static class OutputConfigurationModelTests
{
    public static void Persistence()
    {
        InTemp((path, log) =>
        {
            var config = new AppConfig
            {
                Outputs = new OutputConfig
                {
                    Targets =
                    [
                        new OutputTargetConfig
                        {
                            Id = "desktop-left", Name = "Left", Kind = "desktop",
                            Dashboard = "Default", RefreshIntervalMs = 40,
                            ProviderEnabled = true,
                            Desktop = new DesktopOutputConfig
                            {
                                BoardEnabled = false, Lock = false, X = -100,
                                Y = 240, ZOrder = 2
                            }
                        },
                        new OutputTargetConfig
                        {
                            Id = "desktop-right", Name = "Right", Kind = "desktop",
                            Dashboard = "Default", RefreshIntervalMs = 80,
                            Desktop = new DesktopOutputConfig { BoardEnabled = true }
                        },
                        new OutputTargetConfig
                        {
                            Id = "trofeo-one", Name = "Trofeo", Kind = "trofeo",
                            Dashboard = "Diagnostics", RefreshIntervalMs = 250,
                            ProviderEnabled = true, DeviceEnabled = false,
                            JpegQuality = 67,
                            Usb = new UsbConfig
                            {
                                RetryIntervalMs = 900,
                                TransferTimeoutMs = 1100,
                                DiagnosticJournalEnabled = true
                            }
                        }
                    ]
                }
            };
            config.Save(path);
            var loaded = AppConfig.Load(path, log).Outputs.Targets;
            Check(loaded.Count == 3, "Each configured instance must persist.");
            Check(loaded[0].Id == "desktop-left" && loaded[1].Id == "desktop-right" &&
                  loaded[2].Id == "trofeo-one", "Stable Output IDs must round trip.");
            Check(loaded[0].Dashboard == "Default" && loaded[1].Dashboard == "Default" &&
                  loaded[2].Dashboard == "Diagnostics", "Dashboards may be shared or independently selected.");
            Check(loaded[0].RefreshIntervalMs == 40 && loaded[1].RefreshIntervalMs == 80 &&
                  loaded[2].RefreshIntervalMs == 250, "Refresh cadence belongs to each Output.");
            Check(loaded[0].ProviderEnabled && !loaded[1].ProviderEnabled &&
                  loaded[2].ProviderEnabled && !loaded[2].DeviceEnabled,
                  "Provider Enabled must be independent from physical Device Enabled.");
            Check(loaded[0].Desktop is { BoardEnabled: false, Lock: false, X: -100, Y: 240, ZOrder: 2 } &&
                  loaded[1].Desktop is { BoardEnabled: true, Lock: true },
                  "Independent Desktop options must persist.");
            Check(loaded[0].Transport == "desktop" && loaded[0].JpegQuality is null &&
                  loaded[0].Usb is null && loaded[0].DeviceId.Length == 0,
                  "Desktop outputs must not own JPEG/USB settings.");
            Check(loaded[2].JpegQuality == 67 &&
                  loaded[2].Usb is { RetryIntervalMs: 900, TransferTimeoutMs: 1100, DiagnosticJournalEnabled: true },
                  "Trofeo JPEG and USB settings must persist.");

            var second = AppConfig.Load(path, log);
            second.Outputs.Targets[0].Desktop!.BoardEnabled = true;
            second.Outputs.Targets[0].ProviderEnabled = false;
            second.Outputs.Targets[1].Desktop!.BoardEnabled = false;
            second.Outputs.Targets[1].ProviderEnabled = true;
            second.Save(path);
            var third = AppConfig.Load(path, log).Outputs.Targets;
            Check(third[0].Desktop!.BoardEnabled && !third[0].ProviderEnabled &&
                  !third[1].Desktop!.BoardEnabled && third[1].ProviderEnabled,
                  "All independent Board/Provider flag states must survive save/load.");
        });
    }

    public static void DesktopOnly()
    {
        InTemp((path, log) =>
        {
            var cfg = new AppConfig
            {
                Outputs = new OutputConfig
                {
                    Targets =
                    [
                        new OutputTargetConfig { Id = "virtual-1", Kind = "desktop", Name = "One" },
                        new OutputTargetConfig { Id = "virtual-2", Kind = "desktop", Name = "Two" }
                    ]
                }
            };
            cfg.Save(path);
            var loaded = AppConfig.Load(path, log);
            Check(loaded.Outputs.Targets.Count == 2 &&
                  loaded.Outputs.Targets.All(t => t.Kind == OutputTargetContract.DesktopKind),
                  "Zero physical displays and multiple Desktop outputs must be representable.");

            File.WriteAllText(path,
                """{"Dashboard":{"Active":"Legacy"},"Display":{"RefreshIntervalMs":740},"Renderer":{"JpegQuality":81}}""");
            var legacy = OutputTargetContract.GetPrimary(AppConfig.Load(path, log));
            Check(legacy.Dashboard == "Legacy" && legacy.RefreshIntervalMs == 740 &&
                  legacy.JpegQuality == 81 && !legacy.DeviceEnabled && !legacy.ProviderEnabled,
                  "Missing per-output fields must safely inherit legacy defaults with switches OFF.");
        });
    }

    public static void InvalidReferences()
    {
        InTemp((path, log) =>
        {
            AppConfig MakeValid() => new()
            {
                Outputs = new OutputConfig
                {
                    Targets =
                    [
                        new OutputTargetConfig { Id = "left", Name = "Left", Kind = "desktop" },
                        new OutputTargetConfig { Id = "right", Name = "Right", Kind = "desktop" }
                    ]
                }
            };

            var duplicate = MakeValid();
            duplicate.Outputs.Targets[1].Id = "LEFT";
            Invalid(() => duplicate.Save(path), "Duplicate Output ID must fail.");

            foreach (var name in new[] { "", "../outside", "CON", "name/child", "bad." })
            {
                var config = MakeValid();
                config.Outputs.Targets[0].Dashboard = name;
                Invalid(() => config.Save(path), "Invalid Dashboard reference must fail: " + name);
            }

            var invalidKind = MakeValid();
            invalidKind.Outputs.Targets[0].Kind = "unknown";
            Invalid(() => invalidKind.Save(path), "Unsupported output kind must fail.");

            var invalidRefresh = MakeValid();
            invalidRefresh.Outputs.Targets[0].RefreshIntervalMs = 9;
            Invalid(() => invalidRefresh.Save(path), "Refresh interval below minimum must fail.");

            var deviceInDesktop = MakeValid();
            deviceInDesktop.Outputs.Targets[0].DeviceId = "trofeo:abc";
            Invalid(() => deviceInDesktop.Save(path), "Desktop device binding must fail.");

            var jpegInDesktop = MakeValid();
            jpegInDesktop.Outputs.Targets[0].JpegQuality = 40;
            Invalid(() => jpegInDesktop.Save(path), "Desktop JPEG settings must fail.");

            var usbInDesktop = MakeValid();
            usbInDesktop.Outputs.Targets[0].Usb = new UsbConfig();
            Invalid(() => usbInDesktop.Save(path), "Desktop USB settings must fail.");

            var physical = new AppConfig();
            physical.Outputs.Targets[0].JpegQuality = 9;
            Invalid(() => physical.Save(path), "Invalid physical JPEG quality must fail.");
            physical.Outputs.Targets[0].JpegQuality = 80;
            physical.Outputs.Targets[0].Usb = new UsbConfig { RetryIntervalMs = 10 };
            Invalid(() => physical.Save(path), "Invalid physical USB intervals must fail.");

            var sharedDevice = new AppConfig();
            sharedDevice.Outputs.Targets =
            [
                new OutputTargetConfig { Id = "physical-a", DeviceId = "trofeo:abc" },
                new OutputTargetConfig { Id = "physical-b", DeviceId = "trofeo:abc" }
            ];
            Invalid(() => sharedDevice.Save(path), "A device must not be bound to two outputs.");

            var mixed = MakeValid();
            mixed.Outputs.Targets.Add(new OutputTargetConfig
            {
                Id = "device", Name = "Device", Dashboard = "Hardware"
            });
            mixed.Save(path);
            Check(AppConfig.Load(path, log).Outputs.Targets.Count == 3,
                  "One physical and multiple virtual targets must not require physical binding.");
        });
    }

    private static void InTemp(Action<string, FileLogger> action)
    {
        var root = Path.Combine(Path.GetTempPath(), $"pinkiesysmon-output-stage02-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            action(Path.Combine(root, "app.json"),
                   new FileLogger(Path.Combine(root, "tests.log")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void Invalid(Action action, string message)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException(message);
    }
}
