namespace PinkieSysMon;

// Provider identity uses the stable Output ID, never the mutable display label,
// Windows device path, or an assumed USB serial.
internal static class OutputProviderContract
{
    public static string Id(string outputId)
    {
        if (string.IsNullOrWhiteSpace(outputId))
            throw new ArgumentException("Output ID cannot be empty.", nameof(outputId));
        return OutputMetricContract.Prefix + outputId;
    }

    public static bool IsEnabled(OutputTargetConfig output) => output.ProviderEnabled;

    public static IReadOnlyDictionary<string, object?> PassiveStatus(
        OutputTargetConfig output, string observedLifecycleState)
    {
        if (!output.ProviderEnabled)
            return OutputMetricContract.Unavailable(output);
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [OutputMetricContract.Id(output.Id, OutputMetricContract.Status)] = observedLifecycleState
        };
    }
}
