using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PinkieSysMon;

internal sealed record RuntimeMetricSnapshot(Guid InstanceId, IReadOnlyDictionary<string, object?> Values);

// Versioned, bounded current-value transport for Runtime-owned Output and
// Filesystem metrics. The Runtime owner filters configured/authorized producers.
internal static class RuntimeMetricSnapshotProtocol
{
    internal const string Command = "METRICS_V1";
    internal const int MaxRequestIds = 32;
    internal const int MaxTotalIds = 128;
    internal const int MaxResponseCharacters = 32768;
    private const int MaxMetricIdCharacters = 192;
    private const int MaxTextCharacters = 256;

    private static readonly JsonSerializerOptions Strict = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    // The namespace decision is shared by the IPC parser, Editor demand and
    // preview merge. Individual Runtime configuration still authorizes each ID.
    internal static bool IsSupportedMetricId(string id) =>
        OutputMetricContract.DescribeKnownId(id) is not null ||
        FilesystemSourceContract.SourceIdForMetric(id) is not null;

    internal static bool IsRuntimeOwnedNamespace(string id) =>
        id.StartsWith(OutputMetricContract.Prefix, StringComparison.OrdinalIgnoreCase) ||
        id.StartsWith(FilesystemSourceContract.Prefix, StringComparison.OrdinalIgnoreCase);

    private sealed record Request([property: JsonRequired] string[] Ids);
    private sealed record Response(
        [property: JsonRequired] int Version,
        [property: JsonRequired] Guid InstanceId,
        [property: JsonRequired] Dictionary<string, JsonElement> Values);
    private sealed record WireValue(string Kind, object? Value);
    private sealed record OutgoingResponse(int Version, Guid InstanceId, IReadOnlyDictionary<string, WireValue> Values);

    internal static string BuildRequest(IEnumerable<string> metricIds)
    {
        var ids = ValidateIds(metricIds);
        var command = Command + "\t" + JsonSerializer.Serialize(new Request(ids));
        if (command.Length > RuntimeIpcRequestReader.MaxCommandCharacters)
            throw new InvalidDataException("Metric snapshot request exceeds the IPC input limit.");
        return command;
    }

    internal static string[] ParseRequest(string command)
    {
        if (command.Length > RuntimeIpcRequestReader.MaxCommandCharacters ||
            !command.StartsWith(Command + "\t", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid metric snapshot command.");

        var request = JsonSerializer.Deserialize<Request>(command[(Command.Length + 1)..], Strict);
        if (request?.Ids is null)
            throw new InvalidDataException("Metric snapshot request is missing IDs.");
        return ValidateIds(request.Ids);
    }

    internal static string SerializeSnapshot(
        Guid instanceId,
        IReadOnlyList<string> requestedIds,
        IReadOnlyDictionary<string, object?> published)
    {
        if (instanceId == Guid.Empty)
            throw new InvalidDataException("Runtime instance identity is missing.");

        var ids = ValidateIds(requestedIds);
        var selected = new Dictionary<string, WireValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
        {
            if (published.TryGetValue(id, out var value))
                selected[id] = EncodeValue(value);
        }

        var payload = JsonSerializer.Serialize(new OutgoingResponse(1, instanceId, selected));
        if (payload.Length > MaxResponseCharacters)
            throw new InvalidDataException("Metric snapshot response exceeds the IPC output limit.");
        return payload;
    }

    internal static RuntimeMetricSnapshot DeserializeSnapshot(string payload, IReadOnlyList<string> requestedIds)
    {
        var ids = ValidateIds(requestedIds);
        if (string.IsNullOrWhiteSpace(payload) || payload.Length > MaxResponseCharacters)
            throw new InvalidDataException("Metric snapshot response has an invalid size.");

        var response = JsonSerializer.Deserialize<Response>(payload, Strict);
        if (response is null || response.Version != 1 || response.InstanceId == Guid.Empty ||
            response.Values is null || response.Values.Count > ids.Length)
            throw new InvalidDataException("Metric snapshot response has an invalid version or envelope.");

        var expected = ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, element) in response.Values)
        {
            if (!expected.Contains(id) || !values.TryAdd(id, DecodeValue(element)))
                throw new InvalidDataException("Metric snapshot response contains an unrequested or repeated ID.");
        }
        return new RuntimeMetricSnapshot(response.InstanceId, values);
    }

    private static string[] ValidateIds(IEnumerable<string> metricIds)
    {
        ArgumentNullException.ThrowIfNull(metricIds);
        var ids = metricIds.ToArray();
        if (ids.Length is < 1 or > MaxRequestIds)
            throw new InvalidDataException("Metric snapshot request has an invalid ID count.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > MaxMetricIdCharacters ||
                id != id.Trim() || id.Any(char.IsControl) ||
                !IsSupportedMetricId(id) || !seen.Add(id))
                throw new InvalidDataException("Metric snapshot request contains an invalid or repeated Runtime-owned metric ID.");
        }
        return ids;
    }

    // Explicit wire kinds preserve long vs double even for a value like 24.0:
    // a JSON number alone cannot represent that CLR distinction.
    private static WireValue EncodeValue(object? value)
    {
        if (value is null) return new("null", null);
        if (value is string text)
            return text.Length <= MaxTextCharacters ? new("text", text) : new("null", null);
        if (value is bool boolean) return new("bool", boolean);
        if (value is DateTime timestamp)
            return new("datetime", timestamp.ToString("O", CultureInfo.InvariantCulture));
        if (value is DateTimeOffset timestampOffset)
            return new("datetimeoffset", timestampOffset.ToString("O", CultureInfo.InvariantCulture));

        switch (Type.GetTypeCode(value.GetType()))
        {
            case TypeCode.SByte:
            case TypeCode.Byte:
            case TypeCode.Int16:
            case TypeCode.UInt16:
            case TypeCode.Int32:
            case TypeCode.UInt32:
            case TypeCode.Int64:
                return new("integer", Convert.ToInt64(value, CultureInfo.InvariantCulture));
            case TypeCode.UInt64:
                return (ulong)value <= (ulong)long.MaxValue
                    ? new("integer", (long)(ulong)value) : new("null", null);
            case TypeCode.Single:
            case TypeCode.Double:
            case TypeCode.Decimal:
                var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return double.IsFinite(number) ? new("number", number) : new("null", null);
            default:
                return new("null", null);
        }
    }

    private static object? DecodeValue(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object || element.EnumerateObject().Count() != 2 ||
            !element.TryGetProperty("Kind", out var kind) || kind.ValueKind != JsonValueKind.String ||
            !element.TryGetProperty("Value", out var payload))
            throw new InvalidDataException("Metric snapshot response contains an invalid typed value.");

        return kind.GetString() switch
        {
            "null" when payload.ValueKind == JsonValueKind.Null => null,
            "text" when payload.ValueKind == JsonValueKind.String &&
                        payload.GetString() is { Length: <= MaxTextCharacters } text => text,
            "bool" when payload.ValueKind == JsonValueKind.True => true,
            "bool" when payload.ValueKind == JsonValueKind.False => false,
            "datetime" when payload.ValueKind == JsonValueKind.String &&
                DateTime.TryParseExact(payload.GetString(), "O", CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var timestamp) => timestamp,
            "datetimeoffset" when payload.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParseExact(payload.GetString(), "O", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var timestampOffset) => timestampOffset,
            "integer" when payload.ValueKind == JsonValueKind.Number &&
                           payload.TryGetInt64(out var integer) => integer,
            "number" when payload.ValueKind == JsonValueKind.Number &&
                          payload.TryGetDouble(out var number) && double.IsFinite(number) => number,
            _ => throw new InvalidDataException("Metric snapshot response contains an unsupported typed value.")
        };
    }
}
