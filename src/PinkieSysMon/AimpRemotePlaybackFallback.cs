using System.Runtime.InteropServices;

namespace PinkieSysMon;

/// <summary>
/// Reads AIMP playback position through its Remote Access window-message API.
/// This is intentionally a narrow fallback used only when an AIMP SMTC session
/// does not publish timeline data. Public telemetry remains provider-neutral.
/// </summary>
internal static class AimpRemotePlaybackFallback
{
    private const string RemoteWindowClass = "AIMP2_RemoteInfo";

    private const uint WmUser = 0x0400;
    private const uint WmAimpProperty = WmUser + 0x77;

    private const uint PropertyGet = 0;
    private const uint PlayerPositionProperty = 0x20;
    private const uint PlayerDurationProperty = 0x30;

    private const uint SmtoAbortIfHung = 0x0002;
    private const uint QueryTimeoutMs = 100;

    public static bool IsAimpSource(string? source) =>
        !string.IsNullOrWhiteSpace(source) &&
        source.Contains("aimp", StringComparison.OrdinalIgnoreCase);

    public static bool TryGetProgress(out double progress)
    {
        progress = 0d;

        var window = FindWindowW(RemoteWindowClass, null);
        if (window == IntPtr.Zero)
            return false;

        if (!TryReadProperty(window, PlayerPositionProperty, out var positionMs) ||
            !TryReadProperty(window, PlayerDurationProperty, out var durationMs))
        {
            return false;
        }

        if (positionMs < 0 || durationMs <= 0)
            return false;

        var ratio = positionMs / (double)durationMs;
        if (!double.IsFinite(ratio))
            return false;

        progress = Math.Clamp(ratio, 0d, 1d) * 100d;
        return true;
    }

    private static bool TryReadProperty(IntPtr window, uint propertyId, out long value)
    {
        value = 0;

        var sent = SendMessageTimeoutW(
            window,
            WmAimpProperty,
            new UIntPtr(propertyId | PropertyGet),
            IntPtr.Zero,
            SmtoAbortIfHung,
            QueryTimeoutMs,
            out var result);

        if (sent == IntPtr.Zero)
            return false;

        value = unchecked((long)result.ToUInt64());
        return true;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr FindWindowW(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr SendMessageTimeoutW(
        IntPtr hWnd,
        uint Msg,
        UIntPtr wParam,
        IntPtr lParam,
        uint fuFlags,
        uint uTimeout,
        out UIntPtr lpdwResult);
}
