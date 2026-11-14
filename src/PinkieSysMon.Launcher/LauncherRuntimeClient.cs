using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using PinkieSysMon;

namespace PinkieSysMon.Launcher;

internal sealed record LauncherRuntimeState(bool Present, bool OutputLifetimeRunning,
    string? Root, Guid? InstanceId)
{
    internal static LauncherRuntimeState Absent => new(false, false, null, null);
}

// An absent Runtime and a present-but-unresponsive Runtime have radically
// different ownership semantics. Never start or silently retry a foreign owner.
internal static class LauncherRuntimeClient
{
    internal const string PipeName = "PinkieSysMon.Runtime.v1";
    internal const string StatusVerb = "STATUS";
    internal const string RunningReloadVerb = "OUTPUT_START_RELOAD_ALL";
    internal const string StoppedReloadVerb = "RELOAD";

    internal static string ReloadVerb(bool outputsRunning) =>
        outputsRunning ? RunningReloadVerb : StoppedReloadVerb;

    internal static LauncherRuntimeState VerifyStatus(string expectedRoot, string statusJson)
    {
        using var doc = JsonDocument.Parse(statusJson);
        var node = doc.RootElement;
        if (node.ValueKind != JsonValueKind.Object ||
            !node.TryGetProperty("ApplicationRoot", out var rootNode) ||
            rootNode.ValueKind != JsonValueKind.String ||
            !node.TryGetProperty("RuntimeInstanceId", out var instanceNode) ||
            instanceNode.ValueKind != JsonValueKind.String ||
            !node.TryGetProperty("OutputLifetimeRunning", out var runningNode) ||
            runningNode.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            !Guid.TryParse(instanceNode.GetString(), out var instance) || instance == Guid.Empty)
            throw new InvalidDataException(
                "The responding Runtime lacks the installation/instance/lifetime status contract. No Settings were applied.");

        var root = rootNode.GetString() ?? string.Empty;
        if (!string.Equals(global::PinkieSysMon.Editor.EditorSessionWire.NormalizeRoot(root),
            global::PinkieSysMon.Editor.EditorSessionWire.NormalizeRoot(expectedRoot), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "A different PinkieSysMon installation owns the Runtime endpoint. No Settings were applied.");

        return new LauncherRuntimeState(true, runningNode.GetBoolean(), root, instance);
    }

    internal static async Task<LauncherRuntimeState> GetStatusAsync(string root,
        CancellationToken cancellationToken = default)
    {
        if (!RuntimeProcessLease.ExistsForCurrentUser())
            return LauncherRuntimeState.Absent;
        var answer = await SendAsync(StatusVerb, 2500, cancellationToken).ConfigureAwait(false);
        if (!answer.Success)
            throw new InvalidOperationException(
                "An existing Runtime did not provide verifiable STATUS: " + answer.Message);
        return VerifyStatus(root, answer.Message);
    }

    internal static async Task ReloadAsync(string root, LauncherRuntimeState before,
        CancellationToken cancellationToken = default)
    {
        if (!before.Present || before.InstanceId is null)
            throw new InvalidOperationException("Cannot reload an absent or unverified Runtime.");

        // Recheck owner immediately before the mutating IPC request. If another
        // instance replaced it during the Settings transaction, fail closed.
        var fresh = await GetStatusAsync(root, cancellationToken).ConfigureAwait(false);
        if (!fresh.Present || fresh.InstanceId != before.InstanceId)
            throw new InvalidOperationException("Runtime ownership changed before reload; the command was not sent.");

        var response = await SendAsync(ReloadVerb(fresh.OutputLifetimeRunning), 20000, cancellationToken)
            .ConfigureAwait(false);
        if (!response.Success)
            throw new InvalidOperationException("Runtime rejected Settings reload: " + response.Message);

        var after = await GetStatusAsync(root, cancellationToken).ConfigureAwait(false);
        if (!after.Present || after.InstanceId != before.InstanceId)
            throw new InvalidOperationException(
                "Runtime did not retain its verified instance after Settings reload; apply outcome is uncertain.");
    }

    private sealed record WireReply(bool Success, string Message);

    private static async Task<WireReply> SendAsync(string verb, int timeoutMs,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMs);
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous);
        var writeAttempted = false;
        try
        {
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true)
            { AutoFlush = true, NewLine = "\n" };
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
            writeAttempted = true;
            await writer.WriteLineAsync(verb.AsMemory(), timeout.Token).ConfigureAwait(false);
            var line = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
            if (string.IsNullOrEmpty(line) || line.Length > 32768)
                throw new InvalidDataException("Runtime returned no usable response.");
            var separator = line.IndexOf('\t');
            var status = separator < 0 ? line : line[..separator];
            var message = separator < 0 ? string.Empty : line[(separator + 1)..];
            if (status == "OK") return new WireReply(true, message);
            if (status == "ERR") return new WireReply(false, message);
            throw new InvalidDataException("Runtime returned an unrecognized response.");
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or
                                          InvalidDataException or UnauthorizedAccessException)
        {
            if (verb != StatusVerb && writeAttempted)
                throw new InvalidOperationException(
                    "The Runtime command may already have been applied. Do not repeat it or blindly roll back without inspecting Runtime status.", error);
            throw new InvalidOperationException(
                "Runtime STATUS/connection is unavailable; no Runtime state can be inferred.", error);
        }
    }
}
