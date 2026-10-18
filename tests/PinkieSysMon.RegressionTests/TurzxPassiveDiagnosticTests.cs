using PinkieSysMon;

namespace PinkieSysMon.RegressionTests;

internal static class TurzxPassiveDiagnosticTests
{
    public static void ExplicitAdmission()
    {
        var api = new FakeApi();
        var output = new List<string>();
        Check(TurzxPassiveDiagnostic.TryRun([], output.Add, api) is null && output.Count == 0 && api.Lookups == 0,
            "Normal regressions must not invoke manual/native observation.");
        foreach (var args in new[] { new[] { "--unknown" }, new[] { TurzxPassiveDiagnostic.Argument, "extra" } })
            Check(TurzxPassiveDiagnostic.TryRun(args, output.Add, api) == 2 && api.Lookups == 0,
                "Unsupported or extra arguments must fail before discovery.");
    }

    public static void ExactMetadataAndPrivateOutput()
    {
        var output = new List<string>();
        var api = new FakeApi();
        Check(TurzxPassiveDiagnostic.TryRun([TurzxPassiveDiagnostic.Argument], output.Add, api) == 0,
            "The diagnostic must execute actual production passive discovery and exact selection.");
        var text = string.Join('\n', output);
        Check(text.Contains("PASSIVE DISCOVERY: PASS") && text.Contains("Usable interfaces: 1") &&
            text.Contains("Distinct matching devnode lookups: 1") && text.Contains("Eligible devnodes at last lookup: 1") &&
            text.Contains("Registered interface classes observed: 1") && text.Contains("Distinct enumerated interface paths: 1") &&
            text.Contains("USB transport opened: NO") && api.Lookups == 2,
            "Aggregate counts must include final revalidation without double-counting one devnode.");
        Check(!text.Contains("SYNTHETIC") && !text.Contains("turzx:") && !text.Contains(FakeApi.Class.ToString()),
            "The diagnostic must not print raw paths, instance tokens, binding hashes or interface classes.");
    }

    public static void MissingAmbiguousAndRemoved()
    {
        foreach (var api in new[]
        {
            new FakeApi { Present = false }, new FakeApi { Registration = false },
            new FakeApi { Eligible = false }, new FakeApi { RemovedAfterFirstRead = true },
            new FakeApi { MultiplePaths = true }
        })
        {
            var output = new List<string>();
            Check(TurzxPassiveDiagnostic.TryRun([TurzxPassiveDiagnostic.Argument], output.Add, api) == 1 &&
                output.Any(s => s.StartsWith("PASSIVE DISCOVERY: INCOMPLETE")) &&
                !output.Any(s => s.StartsWith("PASSIVE DISCOVERY: PASS")),
                "A missing registration/metadata gap/removal/multiple interfaces cannot become a discovery PASS.");
            if (api.RemovedAfterFirstRead)
                Check(output.Contains("Eligible devnodes at last lookup: 0"), "A removed node must not remain eligible in the diagnostic summary.");
        }
    }

    public static void ErrorsDoNotLeakPrivateMessages()
    {
        var output = new List<string>();
        Check(TurzxPassiveDiagnostic.TryRun([TurzxPassiveDiagnostic.Argument], output.Add,
            new FakeApi { Failure = true }) == 1, "A failed scan must return failure rather than an empty successful observation.");
        var text = string.Join('\n', output);
        Check(text.Contains("ERROR (InvalidOperationException;") && !text.Contains("PRIVATE") && !text.Contains("SYNTHETIC") &&
            text.Contains("USB transport opened: NO"), "Exceptions must be sanitized and retain a clear failed gate.");
    }

    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class FakeApi : ITurzxInterfaceDiscoveryApi
    {
        public static readonly Guid Class = new("11111111-2222-3333-4444-555555555555");
        private const string Node = @"USB\VID_1CBE&PID_0123\SYNTHETIC-PRIVATE";
        private static readonly string Path = $@"\\?\usb#vid_1cbe&pid_0123#SYNTHETIC-PRIVATE#{{{Class}}}";
        public bool Present = true, Eligible = true, Registration = true, RemovedAfterFirstRead, MultiplePaths, Failure;
        public int Lookups;
        private string List => Present ? Node + "\0\0" : "\0\0";
        public uint GetListSize(out uint characters) { characters = (uint)List.Length; return 0; }
        public uint GetList(char[] buffer) { if (Failure) throw new InvalidOperationException("PRIVATE SYNTHETIC path must not be logged."); List.CopyTo(0, buffer, 0, List.Length); return 0; }
        public UsbPnpNode? ReadNode(string id)
        {
            Lookups++;
            if (RemovedAfterFirstRead && Lookups > 1) return null;
            return new(Node, null, "WINUSB", Class, 0, Eligible);
        }
        public IReadOnlyList<Guid> ReadInterfaceClasses(string id) => Registration ? [Class] : [];
        public IReadOnlyList<string> EnumerateInterfaces(Guid cls, string id) => MultiplePaths ? [Path, Path.Replace("PRIVATE", "OTHER")] : [Path];
        public string? ReadInterfaceInstanceId(string path) => Node;
    }
}
