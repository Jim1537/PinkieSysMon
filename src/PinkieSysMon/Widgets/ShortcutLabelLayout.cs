using SkiaSharp;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.Widgets;

internal static class ShortcutLabelLayout
{
    public static SKRect? GetBounds(C.ShortcutWidgetDefinition widget, WidgetRenderContext context)
    {
        if (widget.Label.Length == 0)
            return null;
        var text = widget.TextPresentation.ToRenderProfile();
        var height = ValueTextLayout.CreateFixedLayout(widget, context, widget.Label, text).Bounds.Height;
        var y = string.Equals(widget.LabelPosition, ShortcutContract.Top, StringComparison.OrdinalIgnoreCase)
            ? widget.Y - widget.Gap - height
            : string.Equals(widget.LabelPosition, ShortcutContract.Middle, StringComparison.OrdinalIgnoreCase)
                ? widget.Y + (widget.Height - height) / 2f
                : widget.Y + widget.Height + widget.Gap;
        return new SKRect(widget.X, y, widget.X + widget.Width, y + height);
    }
}
