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
        // A two-row Equals key must keep the same caption size as a one-row key.
        var standardKeyHeight = keys.Count > 0 ? keys[0].Bounds.Height : 0f;
        using var fill = new SKPaint { Color = ColorParser.Parse(calculator.ButtonFillColor), IsAntialias = context.Antialias };
        using var border = calculator.ButtonBorderWidth > 0f ? new SKPaint
        {
            Color = ColorParser.Parse(calculator.ButtonBorderColor), IsAntialias = context.Antialias,
            Style = SKPaintStyle.Stroke, StrokeWidth = calculator.ButtonBorderWidth
        } : null;
        using var caption = new SKPaint { Color = ColorParser.Parse(calculator.ButtonTextColor), IsAntialias = context.Antialias };
        using var displayFill = new SKPaint { Color = ColorParser.Parse(calculator.DisplayFillColor), IsAntialias = context.Antialias };
        using var displayBorder = calculator.DisplayBorderWidth > 0f ? new SKPaint
        {
            Color = ColorParser.Parse(calculator.DisplayBorderColor), IsAntialias = context.Antialias,
            Style = SKPaintStyle.Stroke, StrokeWidth = calculator.DisplayBorderWidth
        } : null;
        using var display = new SKPaint { Color = ColorParser.Parse(calculator.DisplayTextColor), IsAntialias = context.Antialias };
        var fontProfile = new TextPresentation(calculator.FontFamily, 12f, 400, false,
            "center", "middle", 0f, "#00000000", ValueOverflowContract.None, 0f, 0);
        var typeface = context.GetTypeface(fontProfile);
        foreach (var key in keys)
        {
            var r = key.Bounds;
            var bounds = new SKRect(r.Left, r.Top, r.Right, r.Bottom);
            DrawSurface(canvas, bounds, fill, border, calculator.ButtonBorderWidth, calculator.ButtonCornerRadius);
            var displayCaption = CalculatorGeometry.DisplayCaption(key.Caption);
            var desiredSize = Math.Min(standardKeyHeight * .48f, r.Width / Math.Max(2f, displayCaption.Length * .65f));
            if (desiredSize <= 0f) continue;
            using var font = new SKFont(typeface, desiredSize);
            var m = font.Metrics;
            var y = r.Top + r.Height / 2f - (m.Ascent + m.Descent) / 2f;
            canvas.Save();
            try
            {
                canvas.ClipRect(bounds);
                canvas.DrawText(displayCaption, r.Left + r.Width / 2f, y, SKTextAlign.Center, font, caption);
            }
            finally { canvas.Restore(); }
        }
        if (readout.Width <= 0 || readout.Height <= 0) return;
        var textRect = new SKRect(readout.Left, readout.Top, readout.Right, readout.Bottom);
        DrawSurface(canvas, textRect, displayFill, displayBorder, calculator.DisplayBorderWidth, calculator.DisplayCornerRadius);
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

    // Corner Radius shapes the fill independently of whether a border is drawn.
    // Keys and readout share this path so their geometry stays consistent.
    internal static void DrawSurface(SKCanvas canvas, SKRect bounds, SKPaint fill, SKPaint? border,
        float borderWidth, float cornerRadius)
    {
        var radius = Math.Clamp(cornerRadius, 0f, Math.Min(bounds.Width, bounds.Height) / 2f);
        if (radius > 0f)
            canvas.DrawRoundRect(bounds, radius, radius, fill);
        else
            canvas.DrawRect(bounds, fill);

        if (borderWidth <= 0f || border is null || border.Color.Alpha == 0) return;

        var inset = Math.Min(borderWidth / 2f, Math.Min(bounds.Width, bounds.Height) / 2f);
        var innerBounds = new SKRect(bounds.Left + inset, bounds.Top + inset,
            bounds.Right - inset, bounds.Bottom - inset);
        var innerRadius = Math.Min(Math.Max(0f, radius - inset),
            Math.Max(0f, Math.Min(innerBounds.Width, innerBounds.Height) / 2f));
        if (innerRadius > 0f)
            canvas.DrawRoundRect(innerBounds, innerRadius, innerRadius, border);
        else
            canvas.DrawRect(innerBounds, border);
    }
}
