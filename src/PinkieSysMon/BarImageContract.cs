namespace PinkieSysMon;

internal static class BarImageContract
{
    public const string ContentModeFill = "Fill";
    public const string ContentModeImage = "Image";

    public const string ImageFitClip = "Clip";
    public const string ImageFitContain = "Contain";
    public const string ImageFitCover = "Cover";
    public const string ImageFitStretch = "Stretch";

    public const string ProgressModeScale = "Scale";
    public const string ProgressModeSlide = "Slide";
    public const string ProgressModeReveal = "Reveal";

    public static readonly string[] SupportedContentModes =
    [
        ContentModeFill,
        ContentModeImage
    ];

    public static readonly string[] SupportedImageFits =
    [
        ImageFitClip,
        ImageFitContain,
        ImageFitCover,
        ImageFitStretch
    ];

    public static readonly string[] SupportedProgressModes =
    [
        ProgressModeScale,
        ProgressModeSlide,
        ProgressModeReveal
    ];

    public static bool IsImageMode(string? mode) =>
        string.Equals(mode, ContentModeImage, StringComparison.OrdinalIgnoreCase);

    public static string NormalizeImageFit(string? fit)
    {
        if (string.Equals(fit, ImageFitClip, StringComparison.OrdinalIgnoreCase))
            return ImageFitClip;
        if (string.Equals(fit, ImageFitContain, StringComparison.OrdinalIgnoreCase))
            return ImageFitContain;
        if (string.Equals(fit, ImageFitCover, StringComparison.OrdinalIgnoreCase))
            return ImageFitCover;
        return ImageFitStretch;
    }

    public static string NormalizeProgressMode(string? mode)
    {
        if (string.Equals(mode, ProgressModeSlide, StringComparison.OrdinalIgnoreCase))
            return ProgressModeSlide;
        if (string.Equals(mode, ProgressModeReveal, StringComparison.OrdinalIgnoreCase))
            return ProgressModeReveal;
        return ProgressModeScale;
    }
}
