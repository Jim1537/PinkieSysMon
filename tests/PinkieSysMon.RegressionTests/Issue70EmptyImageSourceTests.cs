using PinkieSysMon;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class Issue70EmptyImageSourceTests
{
    public static void CanonicalAndRendering()
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkiesysmon-issue70-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, "dashboards", "BlankSources");
        Directory.CreateDirectory(Path.Combine(root, "config"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(root, "config", "app.json"), "{}");
        try
        {
            foreach (var blank in new string?[] { null, "", "  " })
            {
                var definition = new C.DashboardDefinition
                {
                    BaseDirectory = directory,
                    Canvas = new C.CanvasDefinition { Width = 64, Height = 64, BackgroundColor = "#FF000000" },
                    Widgets =
                    [
                        new C.ImageWidgetDefinition
                        {
                            Id = "file", Width = 16, Height = 16,
                            Asset = new C.ImageAssetPresentationDefinition
                            {
                                SourceType = C.ImageAssetSourceType.File, Source = blank
                            }
                        },
                        new C.ImageWidgetDefinition
                        {
                            Id = "icon", X = 20, Width = 16, Height = 16,
                            Asset = new C.ImageAssetPresentationDefinition
                            {
                                SourceType = C.ImageAssetSourceType.Icon, Source = blank
                            }
                        },
                        new C.BarWidgetDefinition
                        {
                            Id = "bar", Metric = "system.cpu.usage",
                            Y = 20, Width = 32, Height = 8,
                            ContentMode = BarImageContract.ContentModeImage,
                            Image = new C.BarImagePresentationDefinition { Source = blank }
                        },
                        new C.BinaryWidgetDefinition
                        {
                            Id = "binary", Metric = "system.cpu.usage",
                            Y = 36, Width = 16, Height = 16,
                            Profiles = new Dictionary<string, C.StateVisualProfileDefinition>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["true"] = new C.StateVisualProfileDefinition
                                {
                                    ContentType = C.StateContentType.Image,
                                    Asset = new C.ImageAssetPresentationDefinition
                                    {
                                        SourceType = C.ImageAssetSourceType.File, Source = blank
                                    }
                                },
                                ["false"] = new C.StateVisualProfileDefinition
                                {
                                    ContentType = C.StateContentType.Image,
                                    Asset = new C.ImageAssetPresentationDefinition
                                    {
                                        SourceType = C.ImageAssetSourceType.Icon, Source = blank
                                    }
                                }
                            }
                        }
                    ]
                };
                var json = DashboardJson.Serialize(definition);
                var restored = C.DashboardDefinition.Parse(json, Path.Combine(directory, "dashboard.json"));
                if (restored.Widgets.Count != 4 ||
                    ((C.ImageWidgetDefinition)restored.Widgets[0]).Asset.Source != blank)
                    throw new InvalidOperationException("Blank sources must survive dashboard save and load.");

                using (var renderer = new DashboardRenderer(restored))
                using (var frame = renderer.RenderBgra(new Dictionary<string, object?>()))
                {
                    if (frame.Bitmap.Width != 64 || frame.Bitmap.Height != 64)
                        throw new InvalidOperationException("An empty image source must not break rendering.");
                }

                ((C.ImageWidgetDefinition)restored.Widgets[0]).Asset.Source = "images/does-not-exist.png";
                var rejected = false;
                try { _ = DashboardJson.Serialize(restored); }
                catch (IOException) { rejected = true; }
                if (!rejected)
                    throw new InvalidOperationException("Nonempty invalid file sources must still be rejected.");
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
