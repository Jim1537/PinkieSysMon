namespace PinkieSysMon;

// Provider identity uses the stable Output ID, never the mutable display label,
// Windows device path, or an assumed USB serial.
internal static class OutputProviderContract
{
    // Passive physical USB reading when no session owns transport state.
    public const string InactiveUsbState = "UNAVAILABLE";

    public static string Id(string outputId)
    {
        if (string.IsNullOrWhiteSpace(outputId))
            throw new ArgumentException("Output ID cannot be empty.", nameof(outputId));
        return OutputMetricContract.Prefix + outputId;
    }

    public static bool IsEnabled(OutputTargetConfig output) => output.ProviderEnabled;

    public static IReadOnlyDictionary<string, object?> PassiveStatus(
        OutputTargetConfig output, string observedLifecycleState, bool hasSession = true)
    {
        if (!output.ProviderEnabled)
            return OutputMetricContract.Unavailable(output);

        var snapshot = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [OutputMetricContract.Id(output.Id, OutputMetricContract.Status)] = observedLifecycleState
        };
        // Only the physical session publishes transport states. When it does not
        // exist, report known inactivity without pretending USB discovery occurred.
        if (!hasSession && OutputTargetContract.IsPhysicalKind(output.Kind))
            snapshot[OutputMetricContract.Id(output.Id, OutputMetricContract.UsbState)] = InactiveUsbState;
        return snapshot;
    }
}
