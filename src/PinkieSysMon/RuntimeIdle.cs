using System.Runtime.InteropServices;

namespace PinkieSysMon;

// A value of zero means disabled. Windows' "Never" (zero seconds) is not a ceiling.
internal static class RuntimeIdlePolicy
{
    public const int MaximumTimeoutMinutes = 1440;

    public static long? EffectiveTimeoutMs(int configuredMinutes, uint? windowsDisplaySeconds)
    {
        if (configuredMinutes == 0)
            return null;
        if (configuredMinutes < 0 || configuredMinutes > MaximumTimeoutMinutes)
            throw new ArgumentOutOfRangeException(nameof(configuredMinutes));

        var configuredMs = (long)configuredMinutes * 60_000;
        return windowsDisplaySeconds is > 0
            ? Math.Min(configuredMs, (long)windowsDisplaySeconds.Value * 1000)
            : configuredMs;
    }

    public static bool IsIdle(int configuredMinutes, uint? windowsDisplaySeconds, uint idleMilliseconds)
    {
        var effective = EffectiveTimeoutMs(configuredMinutes, windowsDisplaySeconds);
        return effective.HasValue && idleMilliseconds >= effective.Value;
    }
}

// Evaluated by the already-existing low-frequency Runtime tray timer. All transitions
// are handled by the UI-thread composition root, not by callbacks from native APIs.
internal sealed class RuntimeIdleMonitor(
    Func<uint?>? readIdleMs = null,
    Func<uint?>? readDisplaySeconds = null,
    Func<long>? readTickMs = null)
{
    private const long PowerRefreshIntervalMs = 15_000;
    private readonly Func<uint?> _readIdleMs = readIdleMs ?? WindowsIdleNative.ReadIdleMilliseconds;
    private readonly Func<uint?> _readDisplaySeconds = readDisplaySeconds ?? WindowsIdleNative.ReadDisplayTimeoutSeconds;
    private readonly Func<long> _readTickMs = readTickMs ?? (() => Environment.TickCount64);
    private long? _powerReadAtMs;
    private uint? _windowsDisplaySeconds;

    public void InvalidatePowerSetting() => _powerReadAtMs = null;

    public bool ShouldPause(int configuredMinutes)
    {
        if (configuredMinutes == 0)
            return false;

        var idle = _readIdleMs();
        if (!idle.HasValue)
            return false; // Fail open if session input cannot be observed.

        var now = _readTickMs();
        if (!_powerReadAtMs.HasValue ||
            now - _powerReadAtMs.Value >= PowerRefreshIntervalMs ||
            now < _powerReadAtMs.Value)
        {
            _windowsDisplaySeconds = _readDisplaySeconds();
            _powerReadAtMs = now;
        }

        return RuntimeIdlePolicy.IsIdle(configuredMinutes, _windowsDisplaySeconds, idle.Value);
    }
}

internal static class WindowsIdleNative
{
    private static readonly Guid VideoSubgroup = new("7516b95f-f776-4464-8c53-06167f40cc99");
    private static readonly Guid DisplayTimeout = new("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint TickCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    [DllImport("kernel32.dll")]
    private static extern uint GetTickCount();

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr rootPowerKey, out IntPtr activeScheme);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(IntPtr rootPowerKey,
        ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey,
        ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    public static uint? ReadIdleMilliseconds()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
            return null;

        // Both DWORD ticks wrap together. An apparent future input timestamp can
        // occur with SendInput; treat that observation as recent instead of 49 days idle.
        var elapsed = unchecked(GetTickCount() - info.TickCount);
        return elapsed > int.MaxValue ? 0u : elapsed;
    }

    public static uint? ReadDisplayTimeoutSeconds()
    {
        if (!GetSystemPowerStatus(out var status) || status.AcLineStatus is not (0 or 1))
            return null;
        if (PowerGetActiveScheme(IntPtr.Zero, out var schemePointer) != 0 || schemePointer == IntPtr.Zero)
            return null;

        try
        {
            var scheme = Marshal.PtrToStructure<Guid>(schemePointer);
            var subgroup = VideoSubgroup;
            var setting = DisplayTimeout;
            uint seconds;
            var result = status.AcLineStatus == 1
                ? PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out seconds)
                : PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out seconds);
            return result == 0 ? seconds : null;
        }
        finally
        {
            _ = LocalFree(schemePointer);
        }
    }

}
