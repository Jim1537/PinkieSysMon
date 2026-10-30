using System.Text.Json;
using System.Text.Json.Serialization;

namespace PinkieSysMon;

// IPC carries only opaque passive candidate hashes and the immutable draft
// snapshot. Neither deserialization nor command parsing opens an interface.
internal sealed record CatalogUsbTestBeginRequest(Guid ClientId, CatalogUsbTrialIntent Intent);
internal sealed record CatalogUsbTestHandle(Guid ClientId, Guid AttemptId);

internal static class CatalogUsbTestIpcProtocol
{
    internal const string BeginCommand = "USB_TEST_BEGIN";
    internal const string StatusCommand = "USB_TEST_STATUS";
    internal const string CancelCommand = "USB_TEST_CANCEL";
    internal const int MaxCommandLength = 2048;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    internal static string BuildBegin(Guid clientId, CatalogUsbTrialIntent intent)
    {
        var request = new CatalogUsbTestBeginRequest(clientId, intent);
        if (!ValidBegin(request))
            throw new ArgumentException("USB Test requires a valid opaque binding and complete attempt snapshot.", nameof(intent));
        return Serialize(BeginCommand, request);
    }

    internal static string BuildStatus(CatalogUsbTestHandle handle)
    {
        if (!ValidHandle(handle))
            throw new ArgumentException("USB Test status requires a valid client and attempt.", nameof(handle));
        return Serialize(StatusCommand, handle);
    }

    internal static string BuildCancel(CatalogUsbTestHandle handle)
    {
        if (!ValidHandle(handle))
            throw new ArgumentException("USB Test cancellation requires a valid client and attempt.", nameof(handle));
        return Serialize(CancelCommand, handle);
    }

    private static string Serialize<T>(string verb, T payload)
    {
        var line = verb + "\t" + JsonSerializer.Serialize(payload, JsonOptions);
        if (line.Length > MaxCommandLength)
            throw new ArgumentException("USB Test command exceeds its maximum length.");
        return line;
    }

    // Return false only for unrelated existing Runtime commands. Any USB_TEST_
    // prefix is owned here, and unknown/malformed variants fail without
    // including untrusted payloads or paths in the error response.
    internal static bool TryParse(string line, out RuntimeIpcCommand command)
    {
        command = null!;
        if (!line.StartsWith("USB_TEST_", StringComparison.OrdinalIgnoreCase))
            return false;

        command = new RuntimeIpcCommand(RuntimeIpcCommandKind.Invalid,
            Error: "Invalid or unsupported USB Test IPC request.");
        if (line.Length > MaxCommandLength || line.Contains('\r') || line.Contains('\n'))
            return true;

        var separator = line.IndexOf('\t');
        if (separator < 0)
            return true;

        var verb = line[..separator];
        var json = line[(separator + 1)..];
        try
        {
            switch (verb.ToUpperInvariant())
            {
                case BeginCommand:
                {
                    var request = JsonSerializer.Deserialize<CatalogUsbTestBeginRequest>(json, JsonOptions);
                    if (request is not null && ValidBegin(request))
                        command = new RuntimeIpcCommand(RuntimeIpcCommandKind.UsbTestBegin,
                            UsbTestBegin: request);
                    break;
                }
                case StatusCommand:
                case CancelCommand:
                {
                    var handle = JsonSerializer.Deserialize<CatalogUsbTestHandle>(json, JsonOptions);
                    if (ValidHandle(handle))
                        command = new RuntimeIpcCommand(
                            verb.Equals(StatusCommand, StringComparison.OrdinalIgnoreCase)
                                ? RuntimeIpcCommandKind.UsbTestStatus
                                : RuntimeIpcCommandKind.UsbTestCancel,
                            UsbTestHandle: handle);
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Deliberately do not echo untrusted JSON or native interface paths.
        }
        return true;
    }

    private static bool ValidBegin(CatalogUsbTestBeginRequest? request)
    {
        var intent = request?.Intent;
        return request is not null && request.ClientId != Guid.Empty &&
            intent is not null && intent.AttemptId != Guid.Empty &&
            IsOpaqueId(intent.InterfaceId, "usb-interface:") &&
            IsOpaqueId(intent.PnpNodeKey, "usb-pnp:") &&
            !string.IsNullOrWhiteSpace(intent.Protocol) &&
            OutputTargetContract.HasIntegratedTransport(intent.Protocol) &&
            intent.Width > 0 && intent.Height > 0 && intent.Revision >= 0;
    }

    private static bool ValidHandle(CatalogUsbTestHandle? handle) =>
        handle is not null && handle.ClientId != Guid.Empty && handle.AttemptId != Guid.Empty;

    private static bool IsOpaqueId(string? value, string prefix) =>
        value is not null && value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
        value.Length == prefix.Length + 64 && value.AsSpan(prefix.Length).ToArray().All(Uri.IsHexDigit);
}
