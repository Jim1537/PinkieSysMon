using System.ComponentModel;
using System.Drawing.Design;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using PinkieSysMon;
using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static partial class Program
{
    private static void WindowsInternetConnectivityMetric()
    {
        var descriptor = MetricContract.GetDescriptor(NetworkMetricContract.InternetConnected);
        Assert(descriptor is not null, "Internet connectivity metric descriptor is missing.");
        Assert(descriptor!.ValueKind == MetricValueKind.Boolean,
            "Internet connectivity metric must be boolean.");

        var tempRoot = Path.Combine(Path.GetTempPath(), $"PinkieSysMon-network-regression-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var log = new FileLogger(Path.Combine(tempRoot, "regression.log"));

            var connectedSource = new WindowsNetworkTelemetrySource(log, () => true);
            Assert(connectedSource.DefaultIntervalMs == 5000,
                "Internet connectivity metric must use the 5 second polling baseline.");
            var connected = connectedSource.Capture([NetworkMetricContract.InternetConnected]);
            Assert(connected.TryGetValue(NetworkMetricContract.InternetConnected, out var connectedValue) &&
                   connectedValue is bool connectedBool && connectedBool,
                "Connected state was not published as true.");

            var disconnectedSource = new WindowsNetworkTelemetrySource(log, () => false);
            var disconnected = disconnectedSource.Capture([NetworkMetricContract.InternetConnected]);
            Assert(disconnected.TryGetValue(NetworkMetricContract.InternetConnected, out var disconnectedValue) &&
                   disconnectedValue is bool disconnectedBool && !disconnectedBool,
                "Disconnected state was not published as false.");

            var failingSource = new WindowsNetworkTelemetrySource(log, () => throw new InvalidOperationException("probe failed"));
            var unavailable = failingSource.Capture([NetworkMetricContract.InternetConnected]);
            Assert(unavailable.TryGetValue(NetworkMetricContract.InternetConnected, out var unavailableValue) &&
                   unavailableValue is null,
                "Network probe failure must publish the metric as unavailable.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }


}
