using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;

namespace PinkieSysMon;

/// <summary>
/// Reads representative utilization for the integrated Windows graphics adapter without
/// depending on a hardware-monitor provider. DXCore identifies the adapter; Windows GPU
/// Engine performance counters provide the utilization data.
/// </summary>
internal sealed class WindowsIntegratedGpuTelemetry
{
    private static readonly TimeSpan RediscoveryInterval = TimeSpan.FromSeconds(30);

    private readonly FileLogger _log;
    private string[]? _integratedAdapterLuidTokens;
    private DateTimeOffset _nextDiscoveryAttempt = DateTimeOffset.MinValue;
    private bool _discoveryFailureLogged;
    private bool _counterFailureLogged;

    public WindowsIntegratedGpuTelemetry(FileLogger log)
    {
        _log = log;
    }

    public double? ReadLoadPercent()
    {
        EnsureIntegratedAdaptersDiscovered(force: false);
        var luidTokens = _integratedAdapterLuidTokens;
        if (luidTokens is null || luidTokens.Length == 0)
            return null;

        try
        {
            const string query =
                "SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine";

            using var searcher = WindowsManagementQuery.CreateSearcher(query);
            using var rows = searcher.Get();

            // GPU Engine exposes per-process instances. Aggregate all process instances
            // belonging to the same physical engine, then use the busiest engine as the
            // representative adapter load (the same aggregation principle used by Task Manager).
            var engineLoads = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var matchedRows = 0;

            foreach (ManagementObject row in rows)
            {
                using (row)
                {
                    var name = Convert.ToString(row["Name"], CultureInfo.InvariantCulture)?.Trim();
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    var luidToken = FindMatchingLuidToken(name, luidTokens);
                    if (luidToken is null)
                        continue;

                    var engineKey = TryGetPhysicalEngineKey(name, luidToken);
                    if (engineKey is null)
                        continue;

                    var utilization = TryReadDouble(row["UtilizationPercentage"]);
                    if (utilization is null || !double.IsFinite(utilization.Value))
                        continue;

                    matchedRows++;
                    engineLoads.TryGetValue(engineKey, out var current);
                    engineLoads[engineKey] = current + Math.Max(0d, utilization.Value);
                }
            }

            if (_counterFailureLogged)
            {
                _log.Info("Windows integrated GPU telemetry recovered.");
                _counterFailureLogged = false;
            }

            if (engineLoads.Count == 0)
            {
                // A driver reset/upgrade can change an adapter LUID. Do not spin discovery on
                // every frame; allow a throttled rediscovery and retry on a later capture.
                if (matchedRows == 0)
                    EnsureIntegratedAdaptersDiscovered(force: true);
                return null;
            }

            var busiestEngine = engineLoads.Values.Max();
            return Math.Clamp(busiestEngine, 0d, 100d);
        }
        catch (Exception ex)
        {
            if (!_counterFailureLogged)
            {
                _log.Error(
                    "Windows integrated GPU utilization query failed; system.gpu.integrated.load will report unavailable until counters recover",
                    ex);
                _counterFailureLogged = true;
            }

            return null;
        }
    }

    private void EnsureIntegratedAdaptersDiscovered(bool force)
    {
        var now = DateTimeOffset.UtcNow;
        if (!force && _integratedAdapterLuidTokens is not null)
            return;
        if (now < _nextDiscoveryAttempt)
            return;

        _nextDiscoveryAttempt = now + RediscoveryInterval;

        try
        {
            var tokens = DxCoreIntegratedAdapterDiscovery.FindIntegratedAdapterLuidTokens();
            _integratedAdapterLuidTokens = tokens;

            if (_discoveryFailureLogged)
            {
                _log.Info("DXCore integrated GPU discovery recovered.");
                _discoveryFailureLogged = false;
            }

            if (tokens.Length == 0)
                _log.Info("DXCore reports no active integrated hardware graphics adapter; system.gpu.integrated.load is unavailable.");
            else
                _log.Info($"DXCore discovered {tokens.Length} active integrated hardware graphics adapter(s) for system.gpu.integrated.load.");
        }
        catch (Exception ex)
        {
            _integratedAdapterLuidTokens = null;
            if (!_discoveryFailureLogged)
            {
                _log.Error(
                    "DXCore integrated GPU discovery failed; system.gpu.integrated.load will report unavailable until discovery recovers",
                    ex);
                _discoveryFailureLogged = true;
            }
        }
    }

    private static string? FindMatchingLuidToken(string instanceName, IReadOnlyList<string> luidTokens)
    {
        foreach (var token in luidTokens)
        {
            if (instanceName.Contains(token, StringComparison.OrdinalIgnoreCase))
                return token;
        }

        return null;
    }

    private static string? TryGetPhysicalEngineKey(string instanceName, string luidToken)
    {
        var luidStart = instanceName.IndexOf(luidToken, StringComparison.OrdinalIgnoreCase);
        if (luidStart < 0)
            return null;

        var engineTypeStart = instanceName.IndexOf("_engtype_", luidStart, StringComparison.OrdinalIgnoreCase);
        if (engineTypeStart < 0)
            return null;

        // Excluding pid and engtype collapses every process instance onto the same physical
        // engine while keeping adapter LUID, physical adapter index and engine index distinct.
        return instanceName[luidStart..engineTypeStart];
    }

    private static double? TryReadDouble(object? value)
    {
        if (value is null)
            return null;

        try
        {
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    private static class DxCoreIntegratedAdapterDiscovery
    {
        private const uint InstanceLuidProperty = 0;
        private const uint IsHardwareProperty = 11;
        private const uint IsIntegratedProperty = 12;

        private static readonly Guid AdapterFactoryInterfaceId = new("78ee5945-c36e-4b13-a669-005dd11c0f06");
        private static readonly Guid AdapterListInterfaceId = new("526c7776-40e9-459b-b711-f32ad76dfc28");
        private static readonly Guid AdapterInterfaceId = new("f0db4c7f-fe5a-42a2-bd62-f2a6cf6fc83e");
        private static readonly Guid D3D11GraphicsAttribute = new("8c47866b-7583-450d-f0f0-6bada895af4b");

        public static string[] FindIntegratedAdapterLuidTokens()
        {
            IDXCoreAdapterFactory? factory = null;
            IDXCoreAdapterList? adapterList = null;
            try
            {
                var factoryId = AdapterFactoryInterfaceId;
                Marshal.ThrowExceptionForHR(DXCoreCreateAdapterFactory(ref factoryId, out factory));
                var activeFactory = factory ?? throw new COMException("DXCore returned a null adapter factory.");

                var attribute = D3D11GraphicsAttribute;
                var listId = AdapterListInterfaceId;
                Marshal.ThrowExceptionForHR(activeFactory.CreateAdapterList(1, ref attribute, ref listId, out adapterList));
                var activeAdapterList = adapterList ?? throw new COMException("DXCore returned a null adapter list.");

                var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var adapterCount = activeAdapterList.GetAdapterCount();
                for (uint index = 0; index < adapterCount; index++)
                {
                    IDXCoreAdapter? adapter = null;
                    try
                    {
                        var adapterId = AdapterInterfaceId;
                        Marshal.ThrowExceptionForHR(activeAdapterList.GetAdapter(index, ref adapterId, out adapter));
                        var activeAdapter = adapter ?? throw new COMException("DXCore returned a null adapter.");

                        if (!activeAdapter.IsValid() ||
                            !activeAdapter.IsPropertySupported(IsHardwareProperty) ||
                            !activeAdapter.IsPropertySupported(IsIntegratedProperty) ||
                            !activeAdapter.IsPropertySupported(InstanceLuidProperty))
                        {
                            continue;
                        }

                        if (!ReadBooleanProperty(activeAdapter, IsHardwareProperty) ||
                            !ReadBooleanProperty(activeAdapter, IsIntegratedProperty))
                        {
                            continue;
                        }

                        var luid = ReadLuidProperty(activeAdapter);
                        tokens.Add(FormatLuidToken(luid));
                    }
                    finally
                    {
                        ReleaseComObject(adapter);
                    }
                }

                return tokens.ToArray();
            }
            finally
            {
                ReleaseComObject(adapterList);
                ReleaseComObject(factory);
            }
        }

        private static bool ReadBooleanProperty(IDXCoreAdapter adapter, uint property)
        {
            var buffer = Marshal.AllocHGlobal(1);
            try
            {
                Marshal.ThrowExceptionForHR(adapter.GetProperty(property, (nuint)1, buffer));
                return Marshal.ReadByte(buffer) != 0;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static NativeLuid ReadLuidProperty(IDXCoreAdapter adapter)
        {
            var size = Marshal.SizeOf<NativeLuid>();
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.ThrowExceptionForHR(adapter.GetProperty(InstanceLuidProperty, (nuint)size, buffer));
                return Marshal.PtrToStructure<NativeLuid>(buffer);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static string FormatLuidToken(NativeLuid luid) =>
            $"luid_0x{unchecked((uint)luid.HighPart):x8}_0x{luid.LowPart:x8}";

        private static void ReleaseComObject(object? value)
        {
            if (value is not null && Marshal.IsComObject(value))
                Marshal.ReleaseComObject(value);
        }

        [DllImport("dxcore.dll", ExactSpelling = true)]
        private static extern int DXCoreCreateAdapterFactory(
            ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IDXCoreAdapterFactory factory);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeLuid
        {
            public uint LowPart;
            public int HighPart;
        }

        [ComImport]
        [Guid("78ee5945-c36e-4b13-a669-005dd11c0f06")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDXCoreAdapterFactory
        {
            [PreserveSig]
            int CreateAdapterList(
                uint numAttributes,
                ref Guid filterAttributes,
                ref Guid riid,
                [MarshalAs(UnmanagedType.Interface)] out IDXCoreAdapterList adapterList);
        }

        [ComImport]
        [Guid("526c7776-40e9-459b-b711-f32ad76dfc28")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDXCoreAdapterList
        {
            [PreserveSig]
            int GetAdapter(
                uint index,
                ref Guid riid,
                [MarshalAs(UnmanagedType.Interface)] out IDXCoreAdapter adapter);

            [PreserveSig]
            uint GetAdapterCount();
        }

        [ComImport]
        [Guid("f0db4c7f-fe5a-42a2-bd62-f2a6cf6fc83e")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDXCoreAdapter
        {
            [PreserveSig]
            [return: MarshalAs(UnmanagedType.I1)]
            bool IsValid();

            [PreserveSig]
            [return: MarshalAs(UnmanagedType.I1)]
            bool IsAttributeSupported(ref Guid attributeGuid);

            [PreserveSig]
            [return: MarshalAs(UnmanagedType.I1)]
            bool IsPropertySupported(uint property);

            [PreserveSig]
            int GetProperty(uint property, nuint bufferSize, IntPtr propertyData);
        }
    }
}
