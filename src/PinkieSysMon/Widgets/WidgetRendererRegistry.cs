namespace PinkieSysMon.Widgets;

internal static class WidgetRendererRegistry
{
    public static Dictionary<string, IWidgetRenderer> Create(
        ImageAssetCache imageAssets,
        IconAssetCache iconAssets, FileLogger? log = null)
    {
        IWidgetRenderer[] renderers =
        [
            new ValueWidgetRenderer(),
            new BarWidgetRenderer(imageAssets),
            new GaugeWidgetRenderer(),
            new GraphWidgetRenderer(imageAssets, iconAssets),
            new ImageWidgetRenderer(imageAssets, iconAssets),
            new ShortcutWidgetRenderer(imageAssets, iconAssets, log),
            new BinaryWidgetRenderer(imageAssets, iconAssets),
            new PowerWidgetRenderer(imageAssets, iconAssets),
            new MediaSystemWidgetRenderer(imageAssets, iconAssets),
            new MediaPlayerWidgetRenderer(imageAssets, iconAssets)
        ];

        return renderers.ToDictionary(renderer => renderer.Type, StringComparer.OrdinalIgnoreCase);
    }
}

