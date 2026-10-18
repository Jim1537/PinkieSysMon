using PinkieSysMon;

namespace PinkieSysMon.RegressionTests;

// Explicit manual observation mode, never part of deterministic regression.
// Its only device API is the metadata-only discovery seam.
internal static class TurzxPassiveDiagnostic
{
    public const string Argument = "--turzx-passive-check";

    public static int? TryRun(string[] args, Action<string> write, ITurzxInterfaceDiscoveryApi? api = null)
    {
        if (args.Length == 0) return null;
        if (args.Length != 1 || args[0] != Argument)
        {
            write("Unsupported regression-runner arguments. Use no arguments for regressions or --turzx-passive-check for passive metadata observation.");
            return 2;
        }
        write($"TURZX PASSIVE DISCOVERY CHECK — Runtime v{RuntimeVersion.Current}");
        write("Scope: cached Windows metadata only; no USB open, commands, adoption or configuration writes.");
        if (api is null && !OperatingSystem.IsWindows())
        {
            write("PASSIVE DISCOVERY: NOT RUN (Windows required).");
            return 2;
        }
        var observed = new ObservedApi(api ?? new WindowsTurzxInterfaceDiscoveryApi());
        try
        {
            var devices = TurzxDeviceDiscovery.Capture(observed);
            write($"Usable interfaces: {devices.Count}");
            if (devices.Count != 1)
            {
                write("PASSIVE DISCOVERY: INCOMPLETE (expected exactly one usable interface; no device selected).");
                return 1;
            }
            var target = new OutputTargetConfig
            {
                Id = "passive-check", Name = "Passive check", Kind = "turzx", Transport = "turzx",
                DeviceId = devices[0].DeviceId, DeviceEnabled = false
            };
            if (OutputDeviceSelection.Resolve(target, devices).Status != OutputDeviceResolutionStatus.Found)
            {
                write("PASSIVE DISCOVERY: INCOMPLETE (exact binding did not resolve).");
                return 1;
            }
            write("PASSIVE DISCOVERY: PASS (one exact registered interface, devnode correlation and binding).");
            write("Transport / image / brightness: NOT TESTED. Runtime integration remains unavailable.");
            return 0;
        }
        catch (Exception error)
        {
            // Do not expose exception messages, records, paths, serials or IDs.
            write($"PASSIVE DISCOVERY: ERROR ({error.GetType().Name}; HRESULT 0x{error.HResult:X8}).");
            return 1;
        }
        finally
        {
            write($"Distinct matching devnode lookups: {observed.Nodes.Count}");
            write($"Eligible devnodes at last lookup: {observed.Eligible.Count}");
            write($"Registered interface classes observed: {observed.Classes}");
            write($"Distinct enumerated interface paths: {observed.Paths.Count}");
            write("USB transport opened: NO");
        }
    }

    // Private IDs are retained only for deduplication during this one scan.
    // Output is aggregate counts; no identifier or generated record ToString.
    private sealed class ObservedApi(ITurzxInterfaceDiscoveryApi inner) : ITurzxInterfaceDiscoveryApi
    {
        public HashSet<string> Nodes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Eligible { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Paths { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int Classes { get; private set; }
        public uint GetListSize(out uint characters) => inner.GetListSize(out characters);
        public uint GetList(char[] buffer) => inner.GetList(buffer);
        public UsbPnpNode? ReadNode(string id)
        {
            Nodes.Add(id);
            Eligible.Remove(id);
            var node = inner.ReadNode(id);
            if (node is not null && TurzxDeviceDiscovery.IsEligibleNode(node)) Eligible.Add(id);
            return node;
        }
        public IReadOnlyList<Guid> ReadInterfaceClasses(string id)
        {
            var classes = inner.ReadInterfaceClasses(id);
            Classes += classes.Distinct().Count();
            return classes;
        }
        public IReadOnlyList<string> EnumerateInterfaces(Guid cls, string id)
        {
            var paths = inner.EnumerateInterfaces(cls, id);
            Paths.UnionWith(paths);
            return paths;
        }
        public string? ReadInterfaceInstanceId(string path) => inner.ReadInterfaceInstanceId(path);
    }
}
