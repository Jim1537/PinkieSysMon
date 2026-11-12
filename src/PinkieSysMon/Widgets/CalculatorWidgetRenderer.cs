using SkiaSharp;
using PinkieSysMon;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.Widgets;

internal sealed class CalculatorWidgetRenderer : IWidgetRenderer
{
    public string Type => WidgetTypeContract.Calculator;

    public void Render(SKCanvas canvas, C.WidgetDefinition widget, WidgetRenderContext context)
    {
        var calculator = widget as C.CalculatorWidgetDefinition
            ?? throw new InvalidDataException($"Calculator renderer received '{widget.Type}'.");
        var text = context.GetCalculatorReadout(calculator);
        var readout = CalculatorGeometry.Readout(calculator);
        var keys = CalculatorGeometry.Keys(calculator);
        using var fill = new SKPaint { Color = ColorParser.Parse(calculator.ButtonFillColor), IsAntialias = context.Antialias };
        using var border = new SKPaint
        {
            Color = ColorParser.Parse(calculator.ButtonBorderColor), IsAntialias = context.Antialias,
            Style = SKPaintStyle.Stroke, StrokeWidth = calculator.ButtonBorderWidth
        };
        using var caption = new SKPaint { Color = ColorParser.Parse(calculator.ButtonTextColor), IsAntialias = context.Antialias };
        using var display = new SKPaint { Color = ColorParser.Parse(calculator.DisplayTextColor), IsAntialias = context.Antialias };
        var fontProfile = new TextPresentation(calculator.FontFamily, 12f, 400, false,
            "center", "middle", 0f, "#00000000", ValueOverflowContract.None, 0f, 0);
        var typeface = context.GetTypeface(fontProfile);
        foreach (var key in keys)
        {
            var r = key.Bounds;
            var bounds = new SKRect(r.Left, r.Top, r.Right, r.Bottom);
            canvas.DrawRect(bounds, fill);
            if (calculator.ButtonBorderWidth > 0 && border.Color.Alpha != 0)
            {
                var inset = Math.Min(calculator.ButtonBorderWidth / 2f, Math.Min(r.Width, r.Height) / 2f);
                canvas.DrawRect(new SKRect(r.Left + inset, r.Top + inset, r.Right - inset, r.Bottom - inset), border);
            }
            var desiredSize = Math.Min(r.Height * .48f, r.Width / Math.Max(2f, key.Caption.Length * .65f));
            if (desiredSize <= 0f) continue;
            using var font = new SKFont(typeface, desiredSize);
            var m = font.Metrics;
            var y = r.Top + r.Height / 2f - (m.Ascent + m.Descent) / 2f;
            canvas.Save();
            try
            {
                canvas.ClipRect(bounds);
                canvas.DrawText(key.Caption, r.Left + r.Width / 2f, y, SKTextAlign.Center, font, caption);
            }
            finally { canvas.Restore(); }
        }
        if (readout.Width <= 0 || readout.Height <= 0) return;
        var textRect = new SKRect(readout.Left, readout.Top, readout.Right, readout.Bottom);
        var fontSize = Math.Min(readout.Height * .70f, readout.Width * .30f);
        if (fontSize <= 0) return;
        using var resultFont = new SKFont(typeface, fontSize);
        var measure = resultFont.MeasureText(text);
        if (measure > readout.Width - 4f && measure > 0)
            resultFont.Size *= Math.Max(.01f, (readout.Width - 4f) / measure);
        var metrics = resultFont.Metrics;
        var baseline = readout.Top + readout.Height / 2f - (metrics.Ascent + metrics.Descent) / 2f;
        canvas.Save();
        try
        {
            canvas.ClipRect(textRect);
            canvas.DrawText(text, readout.Right - 2f, baseline, SKTextAlign.Right, resultFont, display);
        }
        finally { canvas.Restore(); }
    }
}
