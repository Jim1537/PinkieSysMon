using System.Globalization;
using SkiaSharp;
using PinkieSysMon;
using CanonicalWidgetDefinition = global::PinkieSysMon.DashboardModel.WidgetDefinition;
using CanonicalDashboard = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.Widgets;

internal static class ValueTextLayout
{
    public static float GetAutomaticScrollSpeed(CanonicalWidgetDefinition widget) =>
        GetAutomaticScrollSpeed(widget.GetTextPresentation());

    public static float GetAutomaticScrollSpeed(TextPresentation presentation) =>
        presentation.FontSize * 2f;

    public static float GetScrollGap(CanonicalWidgetDefinition widget) =>
        GetScrollGap(widget, widget.GetTextPresentation());

    public static float GetScrollGap(CanonicalWidgetDefinition widget, TextPresentation presentation) =>
        Math.Max(widget.Width * 0.25f, presentation.FontSize * 3f);

    public static ValueTextBlockLayout CreateFixedLayout(
        CanonicalWidgetDefinition widget,
        WidgetRenderContext context,
        string text) =>
        CreateFixedLayout(widget, context, text, widget.GetTextPresentation());

    public static ValueTextBlockLayout CreateFixedLayout(
        CanonicalWidgetDefinition widget,
        WidgetRenderContext context,
        string text,
        TextPresentation presentation)
    {
        if (widget.Width <= 0f)
            throw new ArgumentOutOfRangeException(nameof(widget.Width), "Fixed value layout requires Width > 0.");

        var mode = ValueOverflowContract.Normalize(presentation.OverflowMode);
        var fontSize = presentation.FontSize;

        if (mode == ValueOverflowContract.ShrinkToFit)
        {
            using var originalFont = new SKFont(context.GetTypeface(presentation), presentation.FontSize);
            var measured = originalFont.MeasureText(text);
            if (measured > widget.Width && measured > 0f)
                fontSize = Math.Max(0.1f, presentation.FontSize * widget.Width / measured);
        }

        IReadOnlyList<string> lines;
        using var font = new SKFont(context.GetTypeface(presentation), fontSize);
        lines = mode == ValueOverflowContract.Wrap
            ? WrapText(font, text, widget.Width)
            : [text];

        var metrics = font.Metrics;
        var lineHeight = metrics.Descent - metrics.Ascent + metrics.Leading;
        if (lineHeight <= 0f)
            lineHeight = Math.Max(0.1f, fontSize);

        var lineCount = Math.Max(1, lines.Count);
        var topOffset = metrics.Ascent;
        var bottomOffset = (lineCount - 1) * lineHeight + metrics.Descent;
        var firstBaseline =
            presentation.VerticalAlign.Equals("top", StringComparison.OrdinalIgnoreCase)
                ? widget.Y - topOffset
                : presentation.VerticalAlign.Equals("middle", StringComparison.OrdinalIgnoreCase) ||
                  presentation.VerticalAlign.Equals("center", StringComparison.OrdinalIgnoreCase)
                    ? widget.Y - ((topOffset + bottomOffset) / 2f)
                    : presentation.VerticalAlign.Equals("bottom", StringComparison.OrdinalIgnoreCase)
                        ? widget.Y - bottomOffset
                        : widget.Y;

        var bounds = new SKRect(
            widget.X,
            firstBaseline + topOffset,
            widget.X + widget.Width,
            firstBaseline + bottomOffset);

        return new ValueTextBlockLayout(fontSize, lines, firstBaseline, lineHeight, bounds);
    }


    /// <summary>
    /// Resolves the effective top-left container used when shared text content is hosted by
    /// a container widget such as Binary. Width=0 means intrinsic text width. Callers may
    /// make height content-driven even when a dormant positive Height is stored.
    /// </summary>
    public static SKRect ResolveContainerBounds(
        CanonicalWidgetDefinition widget,
        WidgetRenderContext context,
        string text,
        bool autoHeight = false) =>
        ResolveContainerBounds(widget, context, text, widget.GetTextPresentation(), autoHeight);

    public static SKRect ResolveContainerBounds(
        CanonicalWidgetDefinition widget,
        WidgetRenderContext context,
        string text,
        TextPresentation presentation,
        bool autoHeight = false)
    {
        float width;
        float contentHeight;

        if (widget.Width > 0f)
        {
            width = widget.Width;
            var probe = CreateFixedLayout(widget, context, text, presentation);
            contentHeight = Math.Max(1f, probe.Bounds.Height);
        }
        else
        {
            using var font = new SKFont(context.GetTypeface(presentation), presentation.FontSize);
            width = Math.Max(1f, font.MeasureText(text));
            var metrics = font.Metrics;
            contentHeight = Math.Max(1f, metrics.Descent - metrics.Ascent);
        }

        var height = !autoHeight && widget.Height > 0f ? widget.Height : contentHeight;
        return new SKRect(widget.X, widget.Y, widget.X + width, widget.Y + height);
    }

    public static ValueTextBlockLayout CreateContainerFixedLayout(
        CanonicalWidgetDefinition widget,
        WidgetRenderContext context,
        string text,
        SKRect bounds) =>
        CreateContainerFixedLayout(widget, context, text, bounds, widget.GetTextPresentation());

    public static ValueTextBlockLayout CreateContainerFixedLayout(
        CanonicalWidgetDefinition widget,
        WidgetRenderContext context,
        string text,
        SKRect bounds,
        TextPresentation presentation)
    {
        if (bounds.Width <= 0f)
            throw new ArgumentOutOfRangeException(nameof(bounds), "Container text layout requires positive width.");

        var mode = ValueOverflowContract.Normalize(presentation.OverflowMode);
        var fontSize = presentation.FontSize;

        if (mode == ValueOverflowContract.ShrinkToFit)
        {
            using var originalFont = new SKFont(context.GetTypeface(presentation), presentation.FontSize);
            var measured = originalFont.MeasureText(text);
            if (measured > bounds.Width && measured > 0f)
                fontSize = Math.Max(0.1f, presentation.FontSize * bounds.Width / measured);
        }

        IReadOnlyList<string> lines;
        using var font = new SKFont(context.GetTypeface(presentation), fontSize);
        lines = mode == ValueOverflowContract.Wrap
            ? WrapText(font, text, bounds.Width)
            : [text];

        var metrics = font.Metrics;
        var lineHeight = metrics.Descent - metrics.Ascent + metrics.Leading;
        if (lineHeight <= 0f)
            lineHeight = Math.Max(0.1f, fontSize);

        var lineCount = Math.Max(1, lines.Count);
        var topOffset = metrics.Ascent;
        var bottomOffset = (lineCount - 1) * lineHeight + metrics.Descent;
        var firstBaseline = GetContainerBaseline(bounds, topOffset, bottomOffset, presentation);

        return new ValueTextBlockLayout(fontSize, lines, firstBaseline, lineHeight, bounds);
    }

    public static float GetContainerBaseline(
        CanonicalWidgetDefinition widget,
        SKRect bounds,
        SKFontMetrics metrics) =>
        GetContainerBaseline(bounds, metrics.Ascent, metrics.Descent, widget.GetTextPresentation());

    public static float GetContainerBaseline(
        SKRect bounds,
        SKFontMetrics metrics,
        TextPresentation presentation) =>
        GetContainerBaseline(bounds, metrics.Ascent, metrics.Descent, presentation);

    public static float GetAlignedX(SKRect bounds, string? align) =>
        string.Equals(align, "center", StringComparison.OrdinalIgnoreCase)
            ? bounds.MidX
            : string.Equals(align, "right", StringComparison.OrdinalIgnoreCase)
                ? bounds.Right
                : bounds.Left;

    private static float GetContainerBaseline(
        SKRect bounds,
        float topOffset,
        float bottomOffset,
        TextPresentation presentation) =>
        presentation.VerticalAlign.Equals("top", StringComparison.OrdinalIgnoreCase)
            ? bounds.Top - topOffset
            : presentation.VerticalAlign.Equals("middle", StringComparison.OrdinalIgnoreCase) ||
              presentation.VerticalAlign.Equals("center", StringComparison.OrdinalIgnoreCase)
                ? bounds.MidY - ((topOffset + bottomOffset) / 2f)
                : bounds.Bottom - bottomOffset;

    public static float GetAlignedX(CanonicalWidgetDefinition widget)
    {
        var presentation = widget.GetTextPresentation();
        return presentation.Align.Equals("center", StringComparison.OrdinalIgnoreCase)
            ? widget.X + widget.Width / 2f
            : presentation.Align.Equals("right", StringComparison.OrdinalIgnoreCase)
                ? widget.X + widget.Width
                : widget.X;
    }

    public static SKTextAlign GetTextAlign(CanonicalWidgetDefinition widget) =>
        GetTextAlign(widget.GetTextPresentation());

    public static SKTextAlign GetTextAlign(TextPresentation presentation) =>
        presentation.Align.Equals("center", StringComparison.OrdinalIgnoreCase)
            ? SKTextAlign.Center
            : presentation.Align.Equals("right", StringComparison.OrdinalIgnoreCase)
                ? SKTextAlign.Right
                : SKTextAlign.Left;

    public static string Ellipsize(SKFont font, string text, float maxWidth)
    {
        if (maxWidth <= 0f || string.IsNullOrEmpty(text))
            return string.Empty;
        if (font.MeasureText(text) <= maxWidth)
            return text;

        const string ellipsis = "…";
        var ellipsisWidth = font.MeasureText(ellipsis);
        if (ellipsisWidth > maxWidth)
            return string.Empty;

        var starts = StringInfo.ParseCombiningCharacters(text);
        var low = 0;
        var high = starts.Length;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            var candidate = PrefixByTextElements(text, starts, mid) + ellipsis;
            if (font.MeasureText(candidate) <= maxWidth)
                low = mid;
            else
                high = mid - 1;
        }

        return PrefixByTextElements(text, starts, low) + ellipsis;
    }

    public static IReadOnlyList<string> WrapText(SKFont font, string text, float maxWidth)
    {
        if (maxWidth <= 0f)
            return [text];

        var normalized = (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var result = new List<string>();
        foreach (var paragraph in normalized.Split('\n'))
            WrapParagraph(font, paragraph.Replace('\t', ' '), maxWidth, result);

        return result.Count == 0 ? [string.Empty] : result;
    }

    private static void WrapParagraph(SKFont font, string paragraph, float maxWidth, List<string> output)
    {
        if (paragraph.Length == 0)
        {
            output.Add(string.Empty);
            return;
        }

        var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            output.Add(string.Empty);
            return;
        }

        var current = string.Empty;
        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (font.MeasureText(candidate) <= maxWidth)
            {
                current = candidate;
                continue;
            }

            if (current.Length > 0)
            {
                output.Add(current);
                current = string.Empty;
            }

            if (font.MeasureText(word) <= maxWidth)
            {
                current = word;
                continue;
            }

            var chunks = BreakLongToken(font, word, maxWidth);
            for (var i = 0; i < chunks.Count - 1; i++)
                output.Add(chunks[i]);
            current = chunks.Count == 0 ? string.Empty : chunks[^1];
        }

        if (current.Length > 0)
            output.Add(current);
    }

    private static IReadOnlyList<string> BreakLongToken(SKFont font, string token, float maxWidth)
    {
        var starts = StringInfo.ParseCombiningCharacters(token);
        if (starts.Length == 0)
            return [string.Empty];

        var chunks = new List<string>();
        var index = 0;
        while (index < starts.Length)
        {
            var best = index + 1;
            var low = index + 1;
            var high = starts.Length;
            while (low <= high)
            {
                var mid = (low + high) / 2;
                var candidate = SliceByTextElements(token, starts, index, mid - index);
                if (font.MeasureText(candidate) <= maxWidth || mid == index + 1)
                {
                    best = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            chunks.Add(SliceByTextElements(token, starts, index, best - index));
            index = best;
        }

        return chunks;
    }

    private static string PrefixByTextElements(string text, int[] starts, int count)
    {
        if (count <= 0)
            return string.Empty;
        if (count >= starts.Length)
            return text;
        return text[..starts[count]];
    }

    private static string SliceByTextElements(string text, int[] starts, int index, int count)
    {
        var start = starts[index];
        var endIndex = index + count;
        var end = endIndex >= starts.Length ? text.Length : starts[endIndex];
        return text[start..end];
    }
}

internal readonly record struct ValueTextBlockLayout(
    float FontSize,
    IReadOnlyList<string> Lines,
    float FirstBaseline,
    float LineHeight,
    SKRect Bounds);
