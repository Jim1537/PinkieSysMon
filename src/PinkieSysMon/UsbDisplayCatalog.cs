using System.Globalization;

namespace PinkieSysMon;

internal readonly record struct UsbProductId(ushort VendorId, ushort ProductId)
{
    public override string ToString() => $"{VendorId:X4}:{ProductId:X4}";
}

// Recognition metadata, never an authorization to use an adapter. Published
// family names do not identify an exact model, firmware, geometry or USB class.
internal sealed record UsbDisplayFamily(UsbProductId Product, string Name, string EvidenceUrl)
{
    // A candidate handler still requires its own present interface discovery,
    // exact binding, consent and runtime validation. A USB product match alone
    // never authorizes dispatch, supplies geometry, or creates an Output.
    public string? CandidateTransport { get; init; }
    public int? IntegrationIssue { get; init; }
}

internal static class UsbDisplayCatalog
{
    private const string ThermalrightEvidence =
        "https://github.com/Lexonight1/thermalright-trcc-linux/blob/73a1921812af9b56319a07626ac9c67e592bb557/src/trcc/core/registry.py";
    private const string TuringEvidence =
        "https://github.com/mathoudebine/turing-smart-screen-python/blob/2b33ab4f00a096916dd6a1174441a53a7ec33b03/library/lcd/lcd_comm_turing_usb.py";

    // Approved for passive recognition in #77. In particular, 87CD and 87AD
    // are distinct families; 0416:8001 LED/segment controllers are excluded.
    public static IReadOnlyList<UsbDisplayFamily> Families { get; } = Array.AsReadOnly(new[]
    {
        Family(0x0416, 0x5408, "Thermalright LY display family", ThermalrightEvidence,
            OutputTargetContract.TrofeoTransport),
        Family(0x0416, 0x5409, "Thermalright LY1 display family", ThermalrightEvidence),
        Family(0x0416, 0x5302, "Thermalright Type 2 display family", ThermalrightEvidence),
        Family(0x0418, 0x5303, "ALi Type 3 display family", ThermalrightEvidence),
        Family(0x0418, 0x5304, "ALi Type 3 display family", ThermalrightEvidence),
        Family(0x0416, 0x5406, "ALi/F5 display family", ThermalrightEvidence),
        Family(0x0402, 0x3922, "Thermalright SCSI display family", ThermalrightEvidence),
        Family(0x87CD, 0x70DB, "Thermalright SCSI display family", ThermalrightEvidence),
        Family(0x87AD, 0x70DB, "Thermalright bulk display family", ThermalrightEvidence),
        Family(0x1CBE, 0x0123, "Turing/TURZX 12.3-inch family", TuringEvidence, integrationIssue: 71),
        Family(0x1CBE, 0x0028, "Turing 2.8-inch family", TuringEvidence),
        Family(0x1CBE, 0x0046, "Turing 4.6-inch family", TuringEvidence),
        Family(0x1CBE, 0x0050, "Turing 5.2-inch family", TuringEvidence),
        Family(0x1CBE, 0x0080, "Turing 8.0-inch family", TuringEvidence),
        Family(0x1CBE, 0x0088, "Turing 8.8-inch family", TuringEvidence),
        Family(0x1CBE, 0x0092, "Turing 9.2-inch family", TuringEvidence)
    });

    private static readonly IReadOnlyDictionary<UsbProductId, UsbDisplayFamily> ByProduct =
        Families.ToDictionary(family => family.Product);

    public static UsbDisplayFamily? Find(UsbProductId product) => ByProduct.GetValueOrDefault(product);

    private static UsbDisplayFamily Family(ushort vid, ushort pid, string name, string evidence,
        string? candidateTransport = null, int? integrationIssue = null) =>
        new(new UsbProductId(vid, pid), name, evidence)
        { CandidateTransport = candidateTransport, IntegrationIssue = integrationIssue };

    // Parse only an actual USB devnode instance ID, not an interface path,
    // description, substring, generic HID ID, or USBSTOR disk name. Function
    // nodes may carry MI; a parent relation, not that token, establishes grouping.
    internal static bool TryParseInstanceId(string? value, out UsbProductId product, out bool isFunction)
    {
        product = default;
        isFunction = false;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200 ||
            value.Any(ch => char.IsControl(ch) || char.IsWhiteSpace(ch) || ch is '/' or '#'))
            return false;
        var parts = value.Split('\\');
        if (parts.Length != 3 || !parts[0].Equals("USB", StringComparison.OrdinalIgnoreCase) ||
            parts[2].Length == 0)
            return false;
        var tokens = parts[1].Split('&');
        if (tokens.Length is < 2 or > 4 ||
            !TryHexToken(tokens[0], "VID_", 4, out var vid) ||
            !TryHexToken(tokens[1], "PID_", 4, out var pid))
            return false;
        var revisionSeen = false;
        foreach (var token in tokens.Skip(2))
        {
            if (!isFunction && TryHexToken(token, "MI_", 2, out _)) isFunction = true;
            else if (!revisionSeen && TryHexToken(token, "REV_", 4, out _)) revisionSeen = true;
            else return false;
        }
        product = new UsbProductId(vid, pid);
        return true;
    }

    private static bool TryHexToken(string value, string prefix, int digits, out ushort number)
    {
        number = 0;
        return value.Length == prefix.Length + digits &&
               value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               ushort.TryParse(value.AsSpan(prefix.Length), NumberStyles.AllowHexSpecifier,
                   CultureInfo.InvariantCulture, out number);
    }
}
