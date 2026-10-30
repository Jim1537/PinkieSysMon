namespace PinkieSysMon.Widgets;

internal sealed class MediaSystemWidgetRenderer : StateProfileWidgetRenderer
{
    public MediaSystemWidgetRenderer(
        ImageAssetCache images,
        IconAssetCache icons,
        Func<long>? clock = null)
        : base(WidgetTypeContract.MediaSystem, images, icons, clock)
    {
    }
}
