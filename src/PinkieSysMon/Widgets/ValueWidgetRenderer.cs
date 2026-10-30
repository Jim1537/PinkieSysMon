using SkiaSharp;
using PinkieSysMon;
using CanonicalWidgetDefinition = global::PinkieSysMon.DashboardModel.WidgetDefinition;
using CanonicalDashboard = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.Widgets;

internal sealed class ValueWidgetRenderer : IWidgetRenderer, IDisposable
{
    private readonly TextContentRenderer _text = new();

    public string Type => WidgetTypeContract.Value;

    public void Render(SKCanvas canvas, CanonicalWidgetDefinition widget, WidgetRenderContext context)
    {
        var value = widget as CanonicalDashboard.ValueWidgetDefinition
            ?? throw new InvalidDataException($"Value renderer received widget type '{widget.Type}'.");
        _text.Render(
            canvas,
            value,
            context,
            context.ResolveValueText(value),
            presentationOverride: value.TextPresentation.ToRenderProfile());
    }

    public void Dispose() => _text.Dispose();
}
