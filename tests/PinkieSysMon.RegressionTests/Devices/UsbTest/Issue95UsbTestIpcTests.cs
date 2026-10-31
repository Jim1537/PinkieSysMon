namespace PinkieSysMon.RegressionTests;

// No Runtime process, WinUSB open, persistent config or physical dashboard.
internal static class Issue95UsbTestIpcTests
{
    private static readonly Guid Client = new("11111111-2222-3333-4444-555555555555");
    private static readonly Guid Attempt = new("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly string InterfaceId = "usb-interface:" + new string('a', 64);
    private static readonly string NodeKey = "usb-pnp:" + new string('b', 64);

    public static void RoundtripAndCorrelation()
    {
        var intent = new CatalogUsbTrialIntent(Attempt, InterfaceId, NodeKey,
            OutputTargetContract.LyBulkTransport, 1920, 480, 7);
        var line = CatalogUsbTestIpcProtocol.BuildBegin(Client, intent);
        Check(line.Length <= CatalogUsbTestIpcProtocol.MaxCommandLength &&
              RuntimeIpcProtocol.ParseCommand(line) is
              { Kind: RuntimeIpcCommandKind.UsbTestBegin, UsbTestBegin: { } parsed } &&
              parsed.ClientId == Client && parsed.Intent == intent,
            "A Test Begin command must roundtrip the exact client, attempt and immutable wire snapshot.");

        var handle = new CatalogUsbTestHandle(Client, Attempt);
        Check(RuntimeIpcProtocol.ParseCommand(CatalogUsbTestIpcProtocol.BuildStatus(handle)) is
              { Kind: RuntimeIpcCommandKind.UsbTestStatus, UsbTestHandle: { } status } &&
              status == handle, "Test Status must target only the specified client/attempt.");
        Check(RuntimeIpcProtocol.ParseCommand(CatalogUsbTestIpcProtocol.BuildCancel(handle)) is
              { Kind: RuntimeIpcCommandKind.UsbTestCancel, UsbTestHandle: { } cancel } &&
              cancel == handle, "Test Cancel must target only the specified client/attempt.");

        var otherHandle = new CatalogUsbTestHandle(Guid.NewGuid(), Attempt);
        Check(RuntimeIpcProtocol.ParseCommand(CatalogUsbTestIpcProtocol.BuildCancel(otherHandle)) is
              { UsbTestHandle: { } second } && second != handle,
            "A different client identity must never be silently replaced or conflated.");
        Check(RuntimeIpcProtocol.ParseCommand(RuntimeIpcProtocol.StatusCommand).Kind ==
              RuntimeIpcCommandKind.Status &&
              RuntimeIpcProtocol.ParseCommand(RuntimeIpcProtocol.PingCommand).Kind ==
              RuntimeIpcCommandKind.Ping,
            "Existing Runtime control commands must remain unchanged.");
    }

    public static void MalformedRequestsFailClosed()
    {
        var valid = CatalogUsbTestIpcProtocol.BuildBegin(Client,
            new CatalogUsbTrialIntent(Attempt, InterfaceId, NodeKey,
                OutputTargetContract.EncryptedJpegTransport, 1280, 320, 0));
        var invalid = new[]
        {
            "USB_TEST_BEGIN",
            "USB_TEST_START\t{}",
            "USB_TEST_BEGIN\t{}",
            "USB_TEST_BEGIN\tnot-json",
            "USB_TEST_BEGIN\t" + new string('x', CatalogUsbTestIpcProtocol.MaxCommandLength),
            valid.Replace(Client.ToString(), Guid.Empty.ToString(), StringComparison.Ordinal),
            valid.Replace(Attempt.ToString(), Guid.Empty.ToString(), StringComparison.Ordinal),
            valid.Replace(InterfaceId, @"\\?\usb#raw-interface-path", StringComparison.Ordinal),
            valid.Replace(NodeKey, "usb-pnp:not-hex", StringComparison.Ordinal),
            valid.Replace("\"Width\":1280", "\"Width\":0", StringComparison.Ordinal),
            valid.Replace("\"Revision\":0", "\"Revision\":-1", StringComparison.Ordinal),
            valid.Replace("\"Protocol\":\"encrypted-jpeg\"", "\"Protocol\":\"not-implemented\"", StringComparison.OrdinalIgnoreCase),
            valid.Replace("\"Height\":320", "\"Height\":320,\"Injected\":true", StringComparison.Ordinal)
        };
        foreach (var input in invalid)
        {
            var command = RuntimeIpcProtocol.ParseCommand(input);
            Check(command.Kind == RuntimeIpcCommandKind.Invalid &&
                  command.Error is not null &&
                  !command.Error.Contains("raw-interface-path", StringComparison.Ordinal) &&
                  !command.Error.Contains("usb#", StringComparison.Ordinal),
                "Malformed Test requests must be rejected with sanitized diagnostics.");
        }

        foreach (var input in new[]
        {
            "USB_TEST_STATUS\t{}",
            "USB_TEST_CANCEL\t{}",
            "USB_TEST_CANCEL\tnot-json",
            "USB_TEST_CANCEL\t{\"ClientId\":\"00000000-0000-0000-0000-000000000000\",\"AttemptId\":\"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee\"}",
            "USB_TEST_STATUS\t{\"ClientId\":\"11111111-2222-3333-4444-555555555555\",\"AttemptId\":\"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee\",\"Unexpected\":1}"
        })
            Check(RuntimeIpcProtocol.ParseCommand(input).Kind == RuntimeIpcCommandKind.Invalid,
                "Incomplete or extended Test Status/Cancel requests must fail closed.");

        CheckThrows(() => CatalogUsbTestIpcProtocol.BuildStatus(
                new CatalogUsbTestHandle(Guid.Empty, Attempt)),
            "An empty client ID may not be serialized for status.");
        CheckThrows(() => CatalogUsbTestIpcProtocol.BuildCancel(
                new CatalogUsbTestHandle(Client, Guid.Empty)),
            "An empty attempt may not be serialized for cancellation.");
    }

    public static void BoundedServerRequestAdmission()
    {
        using var shortReader = new StringReader("USB_TEST_STATUS\t{}\r\nTRAILING_IGNORED\n");
        Check(RuntimeIpcRequestReader.ReadLineAsync(shortReader).GetAwaiter().GetResult() ==
              "USB_TEST_STATUS\t{}",
            "The Runtime server should consume only a single logical request line.");

        using var exactReader = new StringReader(new string('X',
            RuntimeIpcRequestReader.MaxCommandCharacters) + "\n");
        var exactLine = RuntimeIpcRequestReader.ReadLineAsync(exactReader).GetAwaiter().GetResult();
        Check(exactLine is not null && exactLine.Length == RuntimeIpcRequestReader.MaxCommandCharacters,
            "The finite input boundary must admit its exact maximum.");

        using var oversizedReader = new StringReader(new string('X',
            RuntimeIpcRequestReader.MaxCommandCharacters + 1) + "\n");
        try
        {
            _ = RuntimeIpcRequestReader.ReadLineAsync(oversizedReader).GetAwaiter().GetResult();
            throw new InvalidOperationException("An oversized IPC request was incorrectly accepted.");
        }
        catch (InvalidDataException)
        {
            // Expected: reject before handing the command to any Runtime control.
        }

        using var emptyReader = new StringReader("");
        Check(RuntimeIpcRequestReader.ReadLineAsync(emptyReader).GetAwaiter().GetResult() is null,
            "An EOF without a command must not invent a request.");
    }

    private static void CheckThrows(Action run, string message)
    {
        try { run(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException(message);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
