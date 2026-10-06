namespace PinkieSysMon.Widgets;

internal sealed class MediaPlayerWidgetRenderer : StateProfileWidgetRenderer
{
    public MediaPlayerWidgetRenderer(
        ImageAssetCache images,
        IconAssetCache icons,
        Func<long>? clock = null)
        : base(WidgetTypeContract.MediaPlayer, images, icons, clock)
    {
    }
}
