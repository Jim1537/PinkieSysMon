namespace PinkieSysMon.Widgets;

internal static class WidgetRendererRegistry
{
    public static Dictionary<string, IWidgetRenderer> Create(
        ImageAssetCache imageAssets,
        IconAssetCache iconAssets)
    {
        IWidgetRenderer[] renderers =
        [
            new ValueWidgetRenderer(),
            new BarWidgetRenderer(imageAssets),
            new GaugeWidgetRenderer(),
            new ImageWidgetRenderer(imageAssets, iconAssets),
            new BinaryWidgetRenderer(imageAssets, iconAssets),
            new PowerWidgetRenderer(imageAssets, iconAssets),
            new MediaSystemWidgetRenderer(imageAssets, iconAssets),
            new MediaPlayerWidgetRenderer(imageAssets, iconAssets)
        ];

        return renderers.ToDictionary(renderer => renderer.Type, StringComparer.OrdinalIgnoreCase);
    }
}
