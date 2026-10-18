namespace PinkieSysMon;

// Application policy only; null preserves the device's existing brightness.
// A saved percentage is reapplied by the future adapter, never by discovery.
internal sealed class TurzxOutputConfig
{
    public int? BrightnessPercent { get; set; }

    internal static void ValidateFrameOptions(int? brightnessPercent, bool journalEnabled)
    {
        if (brightnessPercent is not null)
            throw new NotSupportedException("TURZX brightness-to-frame sequencing is not verified; preserve current brightness (null) for this image path.");
        if (journalEnabled)
            throw new NotSupportedException("The detailed TURZX USB journal is not implemented; disable that option for this image path.");
    }

    public static byte ToControllerBrightness(int percent)
    {
        if (percent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(percent), "Brightness must be 0..100 percent.");
        return checked((byte)(percent * 102 / 100));
    }
}
