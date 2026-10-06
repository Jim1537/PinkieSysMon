using System.Globalization;
using SkiaSharp;

namespace PinkieSysMon;

internal static class ColorParser
{
    public static SKColor Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return SKColors.Transparent;

        var text = value.AsSpan().Trim();
        if (text.IsEmpty || text[0] != '#')
            throw new InvalidDataException($"Color '{value}' must use #RRGGBB or #AARRGGBB.");

        text = text[1..];
        return text.Length switch
        {
            6 => new SKColor(
                ParseByte(text, 0),
                ParseByte(text, 2),
                ParseByte(text, 4),
                255),
            8 => new SKColor(
                ParseByte(text, 2),
                ParseByte(text, 4),
                ParseByte(text, 6),
                ParseByte(text, 0)),
            _ => throw new InvalidDataException($"Color '{value}' must use #RRGGBB or #AARRGGBB.")
        };
    }

    private static byte ParseByte(ReadOnlySpan<char> text, int offset) =>
        byte.Parse(text.Slice(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}
