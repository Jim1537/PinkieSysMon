using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PinkieSysMon;

internal sealed class LibreHardwareMonitorHttpClient : IDisposable
{
    // The raw provider relies only on the Libre Hardware Monitor web API schema:
    // data.json for discovery (and values for duplicate SensorId workarounds), plus
    // Sensor?action=Get for ordinary provider-native sensor reads.
    public const int DefaultPort = 8085;
    public static readonly Uri DefaultLoopbackBaseAddress = new("http://127.0.0.1:8085/", UriKind.Absolute);
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan RetryBackoff = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    private static readonly TimeSpan ErrorLogThrottleInterval = TimeSpan.FromSeconds(30);

    private readonly FileLogger _log;
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly Uri? _fixedBaseAddress;
    private readonly object _stateSync = new();

    private Uri? _activeBaseAddress;
    private bool _transportStateKnown;
    private bool _transportAvailable;
    private long _retryNotBeforeTick;
    private long _stateGeneration;
    private bool _disposed;

    public LibreHardwareMonitorHttpClient(
        FileLogger log,
        Uri? baseAddress = null,
        TimeSpan? timeout = null)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _fixedBaseAddress = baseAddress is null ? null : NormalizeAndValidateLocalBaseAddress(baseAddress);
        _activeBaseAddress = _fixedBaseAddress;
        _http = CreateHttpClient(timeout ?? DefaultTimeout);
        _ownsHttpClient = true;
    }

    internal LibreHardwareMonitorHttpClient(FileLogger log, HttpClient httpClient, bool ownsHttpClient = false)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsHttpClient = ownsHttpClient;

        if (_http.BaseAddress is null)
            throw new ArgumentException("Libre Hardware Monitor HTTP client requires a BaseAddress.", nameof(httpClient));

        _fixedBaseAddress = NormalizeAndValidateLocalBaseAddress(_http.BaseAddress);
        _activeBaseAddress = _fixedBaseAddress;
    }

    public Uri? ActiveBaseAddress
    {
        get
        {
            lock (_stateSync)
                return _activeBaseAddress;
        }
    }

    public long StateGeneration
    {
        get
        {
            lock (_stateSync)
                return _stateGeneration;
        }
    }

    public bool TryGetTree(out LibreHardwareMonitorTreeSnapshot? snapshot)
    {
        ThrowIfDisposed();
        snapshot = null;
        if (!ShouldAttemptRequest())
            return false;

        Exception? lastFailure = null;
        foreach (var baseAddress in GetCandidateBaseAddresses())
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseAddress, "data.json"));
                using var response = _http.Send(request, HttpCompletionOption.ResponseContentRead, CancellationToken.None);
                response.EnsureSuccessStatusCode();

                using var stream = response.Content.ReadAsStream();
                var root = JsonSerializer.Deserialize<LibreHardwareMonitorJsonNode>(stream, JsonOptions)
                    ?? throw new InvalidDataException("Libre Hardware Monitor returned an empty data.json document.");

                MarkTransportAvailable(baseAddress);
                snapshot = new LibreHardwareMonitorTreeSnapshot(root);
                return true;
            }
            catch (Exception ex) when (IsExpectedTransportException(ex))
            {
                lastFailure = ex;
            }
            catch (JsonException ex)
            {
                lastFailure = ex;
                ReportProtocolFailure(
                    $"lhm.http.tree.json.{baseAddress.Host}",
                    $"HTTP endpoint {baseAddress} returned data.json that could not be parsed as Libre Hardware Monitor telemetry",
                    ex);
            }
            catch (InvalidDataException ex)
            {
                lastFailure = ex;
                ReportProtocolFailure(
                    $"lhm.http.tree.protocol.{baseAddress.Host}",
                    $"HTTP endpoint {baseAddress} returned an invalid Libre Hardware Monitor payload: {ex.Message}",
                    ex);
            }
        }

        ReportTransportFailure(
            "Could not locate a usable Libre Hardware Monitor web endpoint on this computer",
            lastFailure ?? new HttpRequestException("No local Libre Hardware Monitor endpoint candidates were available."));
        return false;
    }

    public bool TryReadSensor(string sensorId, out LibreHardwareMonitorSensorReading reading)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(sensorId))
            throw new ArgumentException("Sensor ID cannot be empty.", nameof(sensorId));

        reading = default;
        if (!ShouldAttemptRequest())
            return false;

        Uri? baseAddress;
        lock (_stateSync)
            baseAddress = _activeBaseAddress;
        if (baseAddress is null)
            return false;

        try
        {
            var relativeUri = $"Sensor?action=Get&id={Uri.EscapeDataString(sensorId)}";
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseAddress, relativeUri));
            using var response = _http.Send(request, HttpCompletionOption.ResponseContentRead, CancellationToken.None);
            response.EnsureSuccessStatusCode();

            using var stream = response.Content.ReadAsStream();
            var payload = JsonSerializer.Deserialize<LibreHardwareMonitorSensorResponse>(stream, JsonOptions)
                ?? throw new InvalidDataException("Libre Hardware Monitor returned an empty sensor response.");

            MarkTransportAvailable(baseAddress);

            if (!payload.Result.Equals("ok", StringComparison.OrdinalIgnoreCase))
            {
                var message = string.IsNullOrWhiteSpace(payload.Message)
                    ? $"Libre Hardware Monitor rejected sensor '{sensorId}'."
                    : $"Libre Hardware Monitor rejected sensor '{sensorId}': {payload.Message}";
                ReportProtocolFailure("lhm.http.sensor.response", message, null);
                return false;
            }

            reading = new LibreHardwareMonitorSensorReading(
                NormalizeFinite(payload.Value),
                NormalizeFinite(payload.Min),
                NormalizeFinite(payload.Max),
                payload.Format);
            return true;
        }
        catch (Exception ex) when (IsExpectedTransportException(ex))
        {
            ReportTransportFailure($"Could not read Libre Hardware Monitor sensor '{sensorId}'", ex);
            return false;
        }
        catch (JsonException ex)
        {
            ReportProtocolFailure(
                "lhm.http.sensor.json",
                $"Libre Hardware Monitor response for sensor '{sensorId}' could not be parsed",
                ex);
            return false;
        }
        catch (InvalidDataException ex)
        {
            ReportProtocolFailure("lhm.http.sensor.protocol", ex.Message, ex);
            return false;
        }
    }

    private static HttpClient CreateHttpClient(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "HTTP timeout must be positive.");

        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            UseCookies = false,
            UseProxy = false
        };

        var client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = timeout
        };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PinkieSysMon-LHM/1.0");
        return client;
    }

    private IReadOnlyList<Uri> GetCandidateBaseAddresses()
    {
        if (_fixedBaseAddress is not null)
            return [_fixedBaseAddress];

        var result = new List<Uri> { DefaultLoopbackBaseAddress };
        try
        {
            var addresses = Dns.GetHostEntry(Dns.GetHostName()).AddressList
                .Where(IsUsableLocalAddress)
                .OrderBy(address => address.AddressFamily == AddressFamily.InterNetwork ? 0 : 1);

            foreach (var address in addresses)
            {
                var candidate = BuildBaseAddress(address, DefaultPort);
                if (!result.Contains(candidate))
                    result.Add(candidate);
            }
        }
        catch (SocketException ex)
        {
            _log.WarnThrottled(
                "lhm.http.local-addresses",
                ErrorLogThrottleInterval,
                "Could not enumerate local IP addresses for Libre Hardware Monitor endpoint discovery; loopback will still be tried.",
                ex);
        }

        return result;
    }

    private static Uri NormalizeAndValidateLocalBaseAddress(Uri uri)
    {
        if (!uri.IsAbsoluteUri)
            throw new ArgumentException("Libre Hardware Monitor base address must be absolute.", nameof(uri));
        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Libre Hardware Monitor endpoint must use HTTP.", nameof(uri));
        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Libre Hardware Monitor endpoint must not contain credentials.", nameof(uri));
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Libre Hardware Monitor base address must not contain a query or fragment.", nameof(uri));
        if (!string.IsNullOrEmpty(uri.AbsolutePath) && uri.AbsolutePath != "/")
            throw new ArgumentException("Libre Hardware Monitor base address must point to the server root.", nameof(uri));
        if (!IsLocalHost(uri))
            throw new ArgumentException("Libre Hardware Monitor endpoint must resolve to this computer.", nameof(uri));

        return EnsureTrailingSlash(uri);
    }

    private static bool IsLocalHost(Uri uri)
    {
        if (uri.IsLoopback)
            return true;
        if (!IPAddress.TryParse(uri.Host, out var target))
            return false;

        try
        {
            return Dns.GetHostEntry(Dns.GetHostName()).AddressList.Any(address => address.Equals(target));
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static bool IsUsableLocalAddress(IPAddress address) =>
        !IPAddress.IsLoopback(address) &&
        !address.Equals(IPAddress.Any) &&
        !address.Equals(IPAddress.IPv6Any) &&
        !address.Equals(IPAddress.None) &&
        !address.Equals(IPAddress.IPv6None) &&
        !address.IsIPv6Multicast;

    private static Uri BuildBaseAddress(IPAddress address, int port) =>
        EnsureTrailingSlash(new UriBuilder(Uri.UriSchemeHttp, address.ToString(), port).Uri);

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        if (uri.AbsolutePath.EndsWith("/", StringComparison.Ordinal))
            return uri;

        var builder = new UriBuilder(uri)
        {
            Path = uri.AbsolutePath + "/"
        };
        return builder.Uri;
    }

    private static bool IsExpectedTransportException(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException;

    private bool ShouldAttemptRequest()
    {
        lock (_stateSync)
        {
            return !_transportStateKnown ||
                   _transportAvailable ||
                   Environment.TickCount64 >= _retryNotBeforeTick;
        }
    }

    private void MarkTransportAvailable(Uri baseAddress)
    {
        lock (_stateSync)
        {
            var firstDetection = !_transportStateKnown;
            var recovered = _transportStateKnown && !_transportAvailable;
            var endpointChanged = _activeBaseAddress is not null && _activeBaseAddress != baseAddress;
            var stateChanged = firstDetection || recovered || endpointChanged;
            _transportStateKnown = true;
            _transportAvailable = true;
            _activeBaseAddress = baseAddress;
            _retryNotBeforeTick = 0;
            if (stateChanged)
                _stateGeneration++;

            if (firstDetection || endpointChanged)
            {
                _log.Info($"Libre Hardware Monitor HTTP endpoint detected at {baseAddress}.");
            }
            else if (recovered)
            {
                _log.Info($"Libre Hardware Monitor HTTP endpoint recovered at {baseAddress}.");
            }
        }
    }

    private void ReportTransportFailure(string message, Exception ex)
    {
        lock (_stateSync)
        {
            var stateChanged = !_transportStateKnown || _transportAvailable;
            _transportStateKnown = true;
            _transportAvailable = false;
            _activeBaseAddress = _fixedBaseAddress;
            _retryNotBeforeTick = Environment.TickCount64 + (long)RetryBackoff.TotalMilliseconds;
            if (stateChanged)
                _stateGeneration++;

            if (stateChanged)
                _log.Warn($"{message}; telemetry provider remains unavailable until a later request succeeds.", ex);
            else
                _log.WarnThrottled(
                    "lhm.http.transport",
                    ErrorLogThrottleInterval,
                    $"{message}; Libre Hardware Monitor is still unavailable.",
                    ex);
        }
    }

    private void ReportProtocolFailure(string key, string message, Exception? ex)
    {
        _log.WarnThrottled(key, ErrorLogThrottleInterval, message, ex);
    }

    private static double? NormalizeFinite(double? value)
    {
        if (value is null || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
            return null;
        return value.Value;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(LibreHardwareMonitorHttpClient));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_ownsHttpClient)
            _http.Dispose();
    }
}

internal sealed record LibreHardwareMonitorTreeSnapshot(
    LibreHardwareMonitorJsonNode Root);

internal sealed class LibreHardwareMonitorJsonNode
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("Text")]
    public string? Text { get; init; }

    [JsonPropertyName("HardwareId")]
    public string? HardwareId { get; init; }

    [JsonPropertyName("SensorId")]
    public string? SensorId { get; init; }

    [JsonPropertyName("Type")]
    public string? SensorType { get; init; }

    [JsonPropertyName("RawValue")]
    public JsonElement RawValue { get; init; }

    [JsonPropertyName("Value")]
    public JsonElement Value { get; init; }

    [JsonPropertyName("Children")]
    public List<LibreHardwareMonitorJsonNode> Children { get; init; } = [];

    public bool TryGetNumericValue(out double value) =>
        TryParseNumericElement(RawValue, out value) ||
        TryParseNumericElement(Value, out value);

    private static bool TryParseNumericElement(JsonElement element, out double value)
    {
        value = default;
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                return element.TryGetDouble(out value) && double.IsFinite(value);

            case JsonValueKind.String:
                return TryParseNumericText(element.GetString(), out value);

            default:
                return false;
        }
    }

    private static bool TryParseNumericText(string? text, out double value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        if (TryParseFinite(trimmed, CultureInfo.InvariantCulture, out value) ||
            TryParseFinite(trimmed, CultureInfo.CurrentCulture, out value))
        {
            return true;
        }

        // Some LHM releases have emitted formatted strings even in RawValue. Keep the
        // workaround local to the web transport and parse only the leading numeric token.
        var length = 0;
        while (length < trimmed.Length)
        {
            var ch = trimmed[length];
            if (!(char.IsDigit(ch) || ch is '+' or '-' or '.' or ',' or 'e' or 'E'))
                break;
            length++;
        }

        if (length == 0)
            return false;

        var numericToken = trimmed[..length];
        return TryParseFinite(numericToken, CultureInfo.InvariantCulture, out value) ||
               TryParseFinite(numericToken, CultureInfo.CurrentCulture, out value);
    }

    private static bool TryParseFinite(string text, CultureInfo culture, out double value)
    {
        if (!double.TryParse(
                text,
                NumberStyles.Float | NumberStyles.AllowThousands,
                culture,
                out value) ||
            !double.IsFinite(value))
        {
            value = default;
            return false;
        }

        return true;
    }

    public IEnumerable<LibreHardwareMonitorJsonNode> DescendantsAndSelf()
    {
        yield return this;
        if (Children is null)
            yield break;

        foreach (var child in Children)
        {
            foreach (var descendant in child.DescendantsAndSelf())
                yield return descendant;
        }
    }
}

internal readonly record struct LibreHardwareMonitorSensorReading(
    double? Value,
    double? Min,
    double? Max,
    string? Format);

internal sealed class LibreHardwareMonitorSensorResponse
{
    [JsonPropertyName("result")]
    public string Result { get; init; } = string.Empty;

    [JsonPropertyName("value")]
    public double? Value { get; init; }

    [JsonPropertyName("min")]
    public double? Min { get; init; }

    [JsonPropertyName("max")]
    public double? Max { get; init; }

    [JsonPropertyName("format")]
    public string? Format { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
