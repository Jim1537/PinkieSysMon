namespace PinkieSysMon.Widgets;

internal sealed class PowerWidgetRenderer : StateProfileWidgetRenderer
{
    public PowerWidgetRenderer(
        ImageAssetCache images,
        IconAssetCache icons,
        Func<long>? clock = null)
        : base(WidgetTypeContract.Power, images, icons, clock)
    {
    }
}
