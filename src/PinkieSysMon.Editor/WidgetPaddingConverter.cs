using System.ComponentModel;
using System.Globalization;
using PinkieSysMon.DashboardModel;

namespace PinkieSysMon.Editor;

internal sealed class WidgetPaddingConverter : TypeConverter
{
    internal const string Help = "Enter one nonnegative number or top|right|bottom|left in dashboard pixels.";
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is not string text)
            return base.ConvertFrom(context, culture, value);
        var parts = text.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length is not (1 or 4))
            throw new FormatException(Help);
        var sides = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!float.TryParse(parts[i], NumberStyles.Float, culture ?? CultureInfo.CurrentCulture, out sides[i]) ||
                !float.IsFinite(sides[i]) || sides[i] < 0)
                throw new FormatException(Help);
        }
        return parts.Length == 1 ? new WidgetPadding(sides[0], sides[0], sides[0], sides[0])
            : new WidgetPadding(sides[0], sides[1], sides[2], sides[3]);
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        if (destinationType != typeof(string) || value is not WidgetPadding p)
            return base.ConvertTo(context, culture, value, destinationType);
        if (p.Top == p.Right && p.Top == p.Bottom && p.Top == p.Left)
            return p.Top.ToString("R", culture);
        return string.Create(culture, $"{p.Top:R}|{p.Right:R}|{p.Bottom:R}|{p.Left:R}");
    }
}
