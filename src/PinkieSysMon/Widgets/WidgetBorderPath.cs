using SkiaSharp;

namespace PinkieSysMon.Widgets;

// Paths are local to the inset border rectangle, so moving a widget reuses the geometry.
internal sealed class WidgetBorderPath : IDisposable
{
    private readonly List<(SKPath Path, SKPathEffect? Dots, SKPath? Envelope)> _runs = [];
    internal readonly record struct Geometry(float Width, float Height, float Radius, float Stroke, int Sides, bool Dotted);
    internal Geometry Key { get; }

    internal WidgetBorderPath(Geometry key)
    {
        Key = key;
        try
        {
            if (key.Sides == 15)
                AddRun(0, 4);
            else
                for (int side = 0; side < 4; side++)
                {
                    if (!Enabled(side) || Enabled(side + 3)) continue;
                    int count = 1;
                    while (Enabled(side + count)) count++;
                    AddRun(side, count);
                }
        }
        catch { Dispose(); throw; }
    }

    private bool Enabled(int side) => (Key.Sides & (1 << (side % 4))) != 0;

    private SKPoint Center(int corner) => (corner % 4) switch
    {
        0 => new(Key.Radius, Key.Radius),
        1 => new(Key.Width - Key.Radius, Key.Radius),
        2 => new(Key.Width - Key.Radius, Key.Height - Key.Radius),
        _ => new(Key.Radius, Key.Height - Key.Radius)
    };

    private SKPoint Point(SKPoint center, float degrees)
    {
        var radians = degrees * MathF.PI / 180;
        return new(center.X + Key.Radius * MathF.Cos(radians), center.Y + Key.Radius * MathF.Sin(radians));
    }

    private void HalfArc(SKPathBuilder builder, SKPoint center, float start)
    {
        if (Key.Radius > 0)
            builder.ArcTo(new SKRect(center.X - Key.Radius, center.Y - Key.Radius,
                center.X + Key.Radius, center.Y + Key.Radius), start, 45, false);
    }

    private void AddRun(int first, int count)
    {
        using var builder = new SKPathBuilder();
        builder.MoveTo(Point(Center(first), 225 + 90 * first));
        double length = 0;
        for (var index = first; index < first + count; index++)
        {
            int side = index % 4;
            float angle = 225 + 90 * side;
            HalfArc(builder, Center(side), angle % 360);
            var next = Center(side + 1);
            builder.LineTo(Point(next, (angle + 45) % 360));
            HalfArc(builder, next, (angle + 45) % 360);
            length += ((side & 1) == 0 ? Key.Width : Key.Height) - 2d * Key.Radius + Math.PI * Key.Radius / 2;
        }
        if (count == 4) builder.Close();
        if (length <= 0) return;
        SKPathEffect? effect = null;
        if (Key.Dotted)
        {
            double nominal = 2d * Key.Stroke;
            var intervals = Math.Max(1d, Math.Round(length / nominal));
            var step = (float)(count == 4 ? length / intervals : nominal);
            // Center the open pattern within the run, leaving half a dot at either end.
            var firstCenter = count == 4 ? 0 : (length - Math.Max(0, Math.Floor((length - Key.Stroke) / step)) * step) / 2;
            effect = SKPathEffect.CreateDash([0f, step], (float)((step - firstCenter % step) % step));
        }
        var path = builder.Detach();
        using var stroke = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = Key.Stroke, StrokeCap = SKStrokeCap.Butt };
        var envelope = Key.Dotted && count < 4 ? stroke.GetFillPath(path) : null;
        _runs.Add((path, effect, envelope));
    }

    internal void Draw(SKCanvas canvas, SKPaint paint)
    {
        foreach (var run in _runs)
        {
            paint.PathEffect = run.Dots;
            if (run.Envelope is null)
            {
                canvas.DrawPath(run.Path, paint);
                continue;
            }
            canvas.Save();
            try
            {
                canvas.ClipPath(run.Envelope, SKClipOperation.Intersect, paint.IsAntialias);
                canvas.DrawPath(run.Path, paint);
            }
            finally { canvas.Restore(); }
        }
        paint.PathEffect = null;
    }

    public void Dispose()
    {
        foreach (var run in _runs)
        {
            run.Envelope?.Dispose();
            run.Path.Dispose();
            run.Dots?.Dispose();
        }
    }
}
