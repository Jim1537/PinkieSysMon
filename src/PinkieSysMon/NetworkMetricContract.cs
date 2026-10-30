namespace PinkieSysMon;

internal static class NetworkMetricContract
{
    public const string InternetConnected = "system.network.internet.connected";

    public static readonly string[] MetricNames =
    [
        InternetConnected
    ];

    public static readonly MetricDescriptor[] Descriptors =
    [
        new(InternetConnected, MetricValueKind.Boolean, MetricUnit.None)
    ];
}
