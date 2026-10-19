namespace PinkieSysMon;

internal interface ITurzxUsbConnection : IDisposable
{
    int Write(byte[] packet, CancellationToken cancellationToken);
    int Read(byte[] buffer, CancellationToken cancellationToken);
}

internal enum TurzxReplyMode { JpegStream, PrimaryOnly }

// One serialized command/response boundary. The frame adapter owns worker integration.
internal sealed class TurzxTransport : IDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ITurzxUsbConnection _connection;
    private readonly Func<uint> _milliseconds;
    private readonly Action<CancellationToken> _syncDelay;
    private readonly byte[] _response = new byte[TurzxWireProtocol.HeaderLength];
    private bool _synchronized;
    private bool _faulted;
    private int _closing;
    public TurzxReplyMode ReplyMode { get; }

    internal TurzxTransport(ITurzxUsbConnection connection, Func<uint>? milliseconds = null,
        Action<CancellationToken>? syncDelay = null, TurzxReplyMode replyMode = TurzxReplyMode.JpegStream)
    {
        if (replyMode is not (TurzxReplyMode.JpegStream or TurzxReplyMode.PrimaryOnly))
            throw new ArgumentOutOfRangeException(nameof(replyMode));
        ReplyMode = replyMode;
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _milliseconds = milliseconds ?? (() => (uint)DateTime.Now.TimeOfDay.TotalMilliseconds);
        _syncDelay = syncDelay ?? (token => { if (token.WaitHandle.WaitOne(200)) token.ThrowIfCancellationRequested(); });
    }

    public static TurzxTransport? TryOpen(OutputTargetConfig target, int timeoutMs, CancellationToken token,
        PhysicalHandshakeDiagnostics? diagnostics = null) =>
        TryOpen(target, timeoutMs, token, TurzxDeviceDiscovery.Capture, OpenConnection, diagnostics: diagnostics);

    private static ITurzxUsbConnection OpenConnection(OutputDeviceDescriptor device, int timeoutMs)
    {
        var native = WindowsTurzxNativeIo.Open(device.DevicePath, timeoutMs);
        try { return new TurzxUsbConnection(native, timeoutMs); }
        catch { native.Dispose(); throw; }
    }

    internal static TurzxTransport? TryOpen(OutputTargetConfig target, int timeoutMs, CancellationToken token,
        Func<IReadOnlyList<OutputDeviceDescriptor>> discover,
        Func<OutputDeviceDescriptor, int, ITurzxUsbConnection> open,
        Action<CancellationToken>? syncDelay = null, TurzxReplyMode replyMode = TurzxReplyMode.JpegStream,
        PhysicalHandshakeDiagnostics? diagnostics = null)
    {
        diagnostics?.Stage("discovery");
        ArgumentNullException.ThrowIfNull(target);
        if (replyMode is not (TurzxReplyMode.JpegStream or TurzxReplyMode.PrimaryOnly))
            throw new ArgumentOutOfRangeException(nameof(replyMode));
        token.ThrowIfCancellationRequested();
        if (!target.DeviceEnabled || !string.Equals(target.Kind, OutputTargetContract.TurzxKind, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(target.Transport, OutputTargetContract.TurzxTransport, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only an explicitly enabled TURZX target can acquire this transport.");
        TurzxUsbConnection.ValidateTimeout(timeoutMs);
        var resolution = OutputDeviceSelection.Resolve(target, discover());
        if (resolution.Status == OutputDeviceResolutionStatus.Missing) return null;
        if (resolution.Status != OutputDeviceResolutionStatus.Found || resolution.Device is null)
            throw new OutputDeviceResolutionException(resolution.Status, resolution.Message);
        var device = resolution.Device;
        var parts = device.DevicePath.Split('#');
        if (parts.Length != 4 || !Guid.TryParse(parts[3], out var deviceClass) ||
            !TurzxDeviceDiscovery.IsExactInterface(device.DevicePath, deviceClass) ||
            !device.DeviceId.Equals(TurzxDeviceDiscovery.CreateStableDeviceId(device.DevicePath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("TURZX interface descriptor does not match its exact binding.");
        token.ThrowIfCancellationRequested();
        diagnostics?.Stage("native-open");
        var connection = open(device, timeoutMs);
        TurzxTransport? transport = null;
        try
        {
            transport = new TurzxTransport(connection, syncDelay: syncDelay, replyMode: replyMode);
            transport.Synchronize(token, diagnostics);
            return transport;
        }
        catch
        {
            if (transport is null) connection.Dispose(); else transport.Dispose();
            throw;
        }
    }

    public TurzxCommandCompletion Synchronize(CancellationToken token = default,
        PhysicalHandshakeDiagnostics? diagnostics = null) => Execute(token, linked =>
    {
        if (_synchronized) throw new InvalidOperationException("TURZX is already synchronized.");
        var result = Exchange(10, TurzxWireProtocol.Sync(_milliseconds()), linked, diagnostics);
        diagnostics?.Stage("sync-settle");
        _syncDelay(linked);
        linked.ThrowIfCancellationRequested();
        if (ReplyMode == TurzxReplyMode.JpegStream) result = CompleteReplyBoundary(result, linked, diagnostics);
        _synchronized = true;
        return result;
    });

    public TurzxCommandCompletion SendImage(TurzxImageFormat format, ReadOnlySpan<byte> image,
        CancellationToken token = default)
    {
        // Validate before admission: malformed caller input must not poison a
        // healthy connection or emit any native write.
        if (ReplyMode == TurzxReplyMode.JpegStream && format != TurzxImageFormat.Jpeg)
            throw new NotSupportedException("The verified TURZX stream supports JPEG only; use the explicit diagnostic for other codecs.");
        var packet = TurzxWireProtocol.Image(format, image, _milliseconds());
        return Execute(token, linked =>
        {
            RequireSynchronized();
            var result = Exchange((byte)format, packet, linked);
            return ReplyMode == TurzxReplyMode.JpegStream ? CompleteReplyBoundary(result, linked) : result;
        });
    }

    public TurzxCommandCompletion SetBrightnessPercent(int percent, CancellationToken token = default)
    {
        if (ReplyMode == TurzxReplyMode.JpegStream)
            throw new NotSupportedException("Brightness sequencing is not verified for the TURZX JPEG stream.");
        var value = TurzxOutputConfig.ToControllerBrightness(percent);
        return Execute(token, linked =>
        {
            RequireSynchronized();
            var result = Exchange(14, TurzxWireProtocol.Brightness(value, _milliseconds()), linked);
            // No positive ACK, and a delayed reply cannot safely be assigned to
            // a subsequent frame. Return the actual result and forbid reuse.
            if (result.Reply == TurzxReplyKind.Empty) _faulted = true;
            return result;
        });
    }

    private TurzxCommandCompletion CompleteReplyBoundary(TurzxCommandCompletion primary, CancellationToken token,
        PhysicalHandshakeDiagnostics? diagnostics = null)
    {
        // One observed empty completion, not a retry/read-until-empty loop. Keep
        // the command lock and native owner through this second read as well.
        token.ThrowIfCancellationRequested();
        diagnostics?.Begin("sync-follow-up-read", "read", TurzxUsbConnection.PipeIn, _response.Length);
        var received = _connection.Read(_response, token);
        diagnostics?.CompleteRead(_response, received);
        diagnostics?.Stage("sync-follow-up-validation");
        if (received < 0 || received > _response.Length)
            throw new IOException("TURZX follow-up read returned an invalid length.");
        token.ThrowIfCancellationRequested();
        if (received != 0)
            throw new InvalidDataException($"TURZX command {primary.Command} returned a nonempty follow-up response ({received} bytes).");
        return primary with { FollowUpResponseBytes = received };
    }

    private void RequireSynchronized()
    {
        if (!_synchronized) throw new InvalidOperationException("TURZX must synchronize before another command.");
    }

    private TurzxCommandCompletion Execute(CancellationToken token, Func<CancellationToken, TurzxCommandCompletion> command)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing != 0, this);
            if (_faulted) throw new InvalidOperationException("TURZX reply boundary is unavailable; dispose this transport before continuing.");
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdown.Token);
            linked.Token.ThrowIfCancellationRequested();
            try { return command(linked.Token); }
            catch { _faulted = true; throw; }
        }
    }

    private TurzxCommandCompletion Exchange(byte command, byte[] packet, CancellationToken token,
        PhysicalHandshakeDiagnostics? diagnostics = null)
    {
        token.ThrowIfCancellationRequested();
        diagnostics?.Begin("sync-write", "write", TurzxUsbConnection.PipeOut, packet.Length);
        var written = _connection.Write(packet, token);
        diagnostics?.CompleteWrite(written);
        if (written != packet.Length) throw new IOException($"TURZX short USB write: {written}/{packet.Length} bytes.");
        token.ThrowIfCancellationRequested();
        diagnostics?.Begin("sync-primary-read", "read", TurzxUsbConnection.PipeIn, _response.Length);
        var received = _connection.Read(_response, token);
        diagnostics?.CompleteRead(_response, received);
        diagnostics?.Stage("sync-primary-validation");
        if (received < 0 || received > _response.Length) throw new IOException("TURZX USB read returned an invalid length.");
        token.ThrowIfCancellationRequested();
        return TurzxWireProtocol.Interpret(command, _response.AsSpan(0, received));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _closing, 1) != 0) return;
        _shutdown.Cancel();
        lock (_gate)
        {
            try { _connection.Dispose(); }
            finally { _shutdown.Dispose(); }
        }
    }
}
