namespace PinkieSysMon.Editor;

internal enum ConfiguredDeviceRuntimeState
{
    Applied,
    AlreadyRunning,
    Unresponsive,
    Declined,
    Started,
    StartFailed
}

internal sealed record ConfiguredDeviceRuntimeResult(ConfiguredDeviceRuntimeState State, string Message = "");

// Called only after a configured card has been committed. The prompt belongs to
// this save, never to candidate adoption, Test, Forget, or Remove.
internal static class ConfiguredDeviceRuntimeStart
{
    internal static async Task<ConfiguredDeviceRuntimeResult> AfterSaveAsync(
        bool applied, Func<Task<RuntimeIpcStatusResponse>> getStatus,
        Func<bool> processPresent, Func<bool> confirmStart,
        Func<Task<RuntimeIpcStatusResponse>> launch)
    {
        if (applied)
            return new(ConfiguredDeviceRuntimeState.Applied);
        try
        {
            var status = await getStatus();
            if (status.Success)
                return new(ConfiguredDeviceRuntimeState.AlreadyRunning);
            if (processPresent())
                return new(ConfiguredDeviceRuntimeState.Unresponsive);
            if (!confirmStart())
                return new(ConfiguredDeviceRuntimeState.Declined);
            // The user may leave this modal open while Runtime starts elsewhere.
            status = await getStatus();
            if (status.Success)
                return new(ConfiguredDeviceRuntimeState.AlreadyRunning);
            if (processPresent())
                return new(ConfiguredDeviceRuntimeState.Unresponsive);
            var launched = await launch();
            return launched.Success && launched.Status is not null
                ? new(ConfiguredDeviceRuntimeState.Started)
                : new(ConfiguredDeviceRuntimeState.StartFailed, launched.Message);
        }
        catch (Exception error)
        {
            return new(ConfiguredDeviceRuntimeState.StartFailed, error.Message);
        }
    }
}
