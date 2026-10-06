using CanonicalDashboard = PinkieSysMon.DashboardModel;

namespace PinkieSysMon;

internal static class DashboardMetricUsage
{
    public static IReadOnlySet<string> Collect(CanonicalDashboard.DashboardDefinition dashboard)
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var widget in dashboard.Widgets)
        {
            switch (widget)
            {
                case CanonicalDashboard.ValueWidgetDefinition value:
                    if (CanonicalDashboard.ValueSourceKind.IsMetric(value.SourceKind))
                        Add(required, value.Metric);
                    break;

                case CanonicalDashboard.BinaryWidgetDefinition binary:
                    Add(required, binary.Metric);
                    break;

                case CanonicalDashboard.QuantitativeWidgetDefinition quantitative:
                    Add(required, quantitative.Metric);
                    break;

                case CanonicalDashboard.MediaPlayerWidgetDefinition:
                    required.Add(MediaMetricContract.PlaybackStatus);
                    break;

                case CanonicalDashboard.MediaSystemWidgetDefinition mediaSystem:
                {
                    var input = string.Equals(
                        mediaSystem.MediaSource,
                        MediaMetricContract.InputSource,
                        StringComparison.OrdinalIgnoreCase);
                    required.Add(input ? MediaMetricContract.InputAvailable : MediaMetricContract.OutputAvailable);
                    required.Add(input ? MediaMetricContract.InputType : MediaMetricContract.OutputType);
                    break;
                }

                case CanonicalDashboard.PowerWidgetDefinition power:
                    required.Add(PowerMetricContract.StateMetric(power.PowerSource));
                    break;
            }
        }

        return required;
    }


    private static void Add(ISet<string> metrics, string? metric)
    {
        if (!string.IsNullOrWhiteSpace(metric))
            metrics.Add(metric.Trim());
    }
}
