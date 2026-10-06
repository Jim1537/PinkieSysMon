using System.Runtime.InteropServices;

namespace PinkieSysMon;

internal sealed class WindowsNetworkTelemetrySource : IMetricSource
{
    private readonly FileLogger _log;
    private readonly Func<bool?> _readInternetConnectivity;
    private bool _failureLogged;

    public WindowsNetworkTelemetrySource(FileLogger log)
        : this(log, WindowsNetworkConnectivityProbe.ReadInternetConnected)
    {
    }

    internal WindowsNetworkTelemetrySource(FileLogger log, Func<bool?> readInternetConnectivity)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _readInternetConnectivity = readInternetConnectivity
            ?? throw new ArgumentNullException(nameof(readInternetConnectivity));
    }

    public string ProviderId => MetricProviderContract.System;
    public string Name => "windows-network";
    public IReadOnlyCollection<string> MetricNames => NetworkMetricContract.MetricNames;
    public int DefaultIntervalMs => 5000;

    public IReadOnlyDictionary<string, object?> Capture() =>
        Capture(NetworkMetricContract.MetricNames);

    public IReadOnlyDictionary<string, object?> Capture(IReadOnlyCollection<string> requestedMetrics)
    {
        var requested = requestedMetrics as HashSet<string>
            ?? new HashSet<string>(requestedMetrics, StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, object?>(requested.Count, StringComparer.OrdinalIgnoreCase);

        if (!requested.Contains(NetworkMetricContract.InternetConnected))
            return result;

        try
        {
            result[NetworkMetricContract.InternetConnected] = _readInternetConnectivity();
            if (_failureLogged)
            {
                _log.Info("Windows network connectivity telemetry recovered.");
                _failureLogged = false;
            }
        }
        catch (Exception ex)
        {
            result[NetworkMetricContract.InternetConnected] = null;
            if (!_failureLogged)
            {
                _log.Error(
                    "Windows network connectivity query failed; system.network.internet.connected will report unavailable until NLM recovers",
                    ex);
                _failureLogged = true;
            }
        }

        return result;
    }
}

internal static class WindowsNetworkConnectivityProbe
{
    // NetworkListManager coclass. INetworkListManager derives from IDispatch, so late-bound
    // access to IsConnectedToInternet avoids an external COM interop assembly/reference.
    private static readonly Guid NetworkListManagerClassId = new("DCB00C01-570F-4A9B-8D69-199FDBA5723B");

    public static bool? ReadInternetConnected()
    {
        object? manager = null;
        try
        {
            var type = Type.GetTypeFromCLSID(NetworkListManagerClassId, throwOnError: true)
                ?? throw new InvalidOperationException("Windows Network List Manager COM class is unavailable.");
            manager = Activator.CreateInstance(type)
                ?? throw new InvalidOperationException("Windows Network List Manager could not be created.");

            dynamic networkListManager = manager;
            return (bool)networkListManager.IsConnectedToInternet;
        }
        finally
        {
            if (manager is not null && Marshal.IsComObject(manager))
                Marshal.FinalReleaseComObject(manager);
        }
    }
}
