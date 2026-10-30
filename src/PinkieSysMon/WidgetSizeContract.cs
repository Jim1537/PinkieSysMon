using CanonicalDashboard = PinkieSysMon.DashboardModel;

namespace PinkieSysMon;

internal static class WidgetSizeContract
{
    public static bool IsHeightDerived(CanonicalDashboard.WidgetDefinition widget) =>
        widget is CanonicalDashboard.GaugeWidgetDefinition or CanonicalDashboard.ValueWidgetDefinition ||
        widget is CanonicalDashboard.StateVisualWidgetDefinition stateWidget && stateWidget.UsesOnlyValueSources();

    public static bool IsSquareConstrained(CanonicalDashboard.WidgetDefinition widget) =>
        widget is CanonicalDashboard.GaugeWidgetDefinition;

    public static bool IsWidthOnlyResizable(CanonicalDashboard.WidgetDefinition widget) =>
        widget is CanonicalDashboard.ValueWidgetDefinition ||
        widget is CanonicalDashboard.StateVisualWidgetDefinition stateWidget && stateWidget.UsesOnlyValueSources();

    public static void ApplyDerivedDimensions(CanonicalDashboard.WidgetDefinition widget)
    {
        if (IsSquareConstrained(widget))
            widget.Height = widget.Width;
    }
}
