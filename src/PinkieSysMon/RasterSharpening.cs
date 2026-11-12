using SkiaSharp;

namespace PinkieSysMon;

// An unsharp mask applied once to the final reduced frame, never to decoded
// source pixels or on every render. Reconstructing premultiplied RGB and alpha
// together avoids dark fringes around translucent PNG edges.
internal static class RasterSharpening
{
    private const float MaximumAmount = 2.4f;

    internal static void Apply(SKBitmap bitmap, int sharpness)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        RenderQuality.ValidateRasterSharpness(sharpness);
        if (sharpness == 0 || bitmap.Width < 2 || bitmap.Height < 2)
            return;

        var source = bitmap.Pixels;
        var result = new SKColor[source.Length];
        var width = bitmap.Width;
        var height = bitmap.Height;
        var amount = MaximumAmount * (sharpness / (float)RenderQuality.MaximumRasterSharpness);

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var index = y * width + x;
            var center = source[index];
            float blurredAlpha = 0f;
            float blurredRed = 0f;
            float blurredGreen = 0f;
            float blurredBlue = 0f;

            // A normalized separable 3x3 Gaussian, evaluated in one pass.
            // Clamped edge coordinates prevent transparent border bleeding.
            for (var dy = -1; dy <= 1; dy++)
            {
                var sy = Math.Clamp(y + dy, 0, height - 1);
                var wy = dy == 0 ? 2 : 1;
                for (var dx = -1; dx <= 1; dx++)
                {
                    var sx = Math.Clamp(x + dx, 0, width - 1);
                    var weight = wy * (dx == 0 ? 2 : 1);
                    var sample = source[sy * width + sx];
                    var sampleAlpha = sample.Alpha / 255f;
                    blurredAlpha += weight * sample.Alpha;
                    blurredRed += weight * sample.Red * sampleAlpha;
                    blurredGreen += weight * sample.Green * sampleAlpha;
                    blurredBlue += weight * sample.Blue * sampleAlpha;
                }
            }

            const float kernelScale = 1f / 16f;
            var centerAlpha = center.Alpha;
            var alpha = Math.Clamp(
                centerAlpha + amount * (centerAlpha - blurredAlpha * kernelScale), 0f, 255f);
            var outputAlpha = ToByte(alpha);
            if (outputAlpha == 0)
            {
                result[index] = SKColors.Transparent;
                continue;
            }

            var scale = centerAlpha / 255f;
            var red = Enhance(center.Red * scale, blurredRed * kernelScale, amount, alpha);
            var green = Enhance(center.Green * scale, blurredGreen * kernelScale, amount, alpha);
            var blue = Enhance(center.Blue * scale, blurredBlue * kernelScale, amount, alpha);
            var unpremultiply = 255f / alpha;
            result[index] = new SKColor(
                ToByte(red * unpremultiply),
                ToByte(green * unpremultiply),
                ToByte(blue * unpremultiply),
                outputAlpha);
        }

        // SKBitmap.Pixels converts SKColor back into the bitmap's premultiplied
        // BGRA representation. The original source and shared cache remain isolated.
        bitmap.Pixels = result;
    }

    private static float Enhance(float center, float blurred, float amount, float alpha) =>
        Math.Clamp(center + amount * (center - blurred), 0f, alpha);

    private static byte ToByte(float value) => (byte)Math.Clamp((int)MathF.Round(value), 0, 255);
}
