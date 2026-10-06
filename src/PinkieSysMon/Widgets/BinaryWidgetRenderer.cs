namespace PinkieSysMon.Widgets;

internal sealed class BinaryWidgetRenderer : StateProfileWidgetRenderer
{
    public BinaryWidgetRenderer(
        ImageAssetCache images,
        IconAssetCache icons,
        Func<long>? clock = null)
        : base(WidgetTypeContract.Binary, images, icons, clock)
    {
    }
}
