namespace PinkieSysMon;

// Application policy only; null preserves the device's existing brightness.
// Only a committed percentage is sent by the worker; null preserves hardware state.
internal sealed class EncryptedJpegOutputConfig
{
    public int? BrightnessPercent { get; set; }

    internal static void ValidateFrameOptions(int? brightnessPercent)
    {
        if (brightnessPercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(brightnessPercent), "Brightness must be null or 0..100 percent.");
    }

    public static byte ToControllerBrightness(int percent)
    {
        if (percent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(percent), "Brightness must be 0..100 percent.");
        return checked((byte)(percent * 102 / 100));
    }
}
