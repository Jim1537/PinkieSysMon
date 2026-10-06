namespace PinkieSysMon;

internal static class RenderQuality
{
    // PinkieSysMon renders small, frequently rotated primitives. Antialiasing is part of
    // the visual contract, not an optional preview/runtime preference.
    public const bool Antialias = true;
}
