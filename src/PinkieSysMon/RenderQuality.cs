using SkiaSharp;

namespace PinkieSysMon;

internal static class RenderQuality
{
    // PinkieSysMon renders small, frequently rotated primitives. Antialiasing is part of
    // the visual contract, not an optional preview/runtime preference.
    public const bool Antialias = true;
    public const int MaximumRasterSharpness = 100;

    // The zero setting reproduces the original linear sampling path exactly.
    // Higher settings add bounded cubic reconstruction after low-pass filtering.
    internal static SKSamplingOptions RasterSampling(int sharpness)
    {
        ValidateRasterSharpness(sharpness);
        return sharpness == 0
            ? new SKSamplingOptions(SKFilterMode.Linear)
            : new SKSamplingOptions(new SKCubicResampler(
                0f, 0.10f + 0.35f * (sharpness / (float)MaximumRasterSharpness)));
    }

    internal static void ValidateRasterSharpness(int sharpness)
    {
        if (sharpness is < 0 or > MaximumRasterSharpness)
            throw new ArgumentOutOfRangeException(nameof(sharpness));
    }
}
