using SkiaSharp;

namespace PinkieSysMon;

internal static class RenderQuality
{
    // PinkieSysMon renders small, frequently rotated primitives. Antialiasing is part of
    // the visual contract, not an optional preview/runtime preference.
    public const bool Antialias = true;
    public const int MaximumRasterSharpness = 100;

    // Scaling always retains linear reconstruction. Positive sharpness values
    // now use a true post-downscale unsharp mask in the shared raster cache.
    // The zero setting preserves the original linear sampling exactly.
    internal static SKSamplingOptions RasterSampling(int sharpness)
    {
        ValidateRasterSharpness(sharpness);
        return new SKSamplingOptions(SKFilterMode.Linear);
    }

    internal static void ValidateRasterSharpness(int sharpness)
    {
        if (sharpness is < 0 or > MaximumRasterSharpness)
            throw new ArgumentOutOfRangeException(nameof(sharpness));
    }
}
