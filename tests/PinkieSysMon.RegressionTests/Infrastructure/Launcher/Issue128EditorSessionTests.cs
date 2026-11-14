using PinkieSysMon.Editor;

namespace PinkieSysMon.RegressionTests;

internal static class Issue128EditorSessionTests
{
    public static void PerRootGateAndSessionProtocol()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "pinkie-issue128-editor-" + Guid.NewGuid().ToString("N"));
        var gate = EditorSessionWire.StartupGateName(root, "S-1-5-21-TEST");
        if (gate != EditorSessionWire.StartupGateName(root + Path.DirectorySeparatorChar, "S-1-5-21-TEST") ||
            gate == EditorSessionWire.StartupGateName(root + "-other", "S-1-5-21-TEST"))
            throw new InvalidOperationException("Startup gate must use the normalized application root.");
        var pipe = EditorSessionWire.CurrentPipeName(Environment.ProcessId);
        var snapshot = new EditorSessionSnapshot(EditorSessionWire.NormalizeRoot(root),
            Environment.ProcessId, DateTime.UtcNow.Ticks, "ActiveBoard");
        var closed = 0;
        using var server = new EditorSessionEndpoint(pipe,
            () => Task.FromResult(snapshot),
            expected =>
            {
                if (expected != snapshot.ActiveDashboard)
                    throw new InvalidOperationException("Close request lost current document.");
                Interlocked.Increment(ref closed);
                return Task.FromResult(new EditorSessionCloseResult(true, "Closed."));
            });
        server.Start();
        var response = EditorSessionWire.SendAsync(Environment.ProcessId,
            new(Guid.NewGuid().ToString("N"), "STATUS", null, snapshot.StartedUtcTicks, null))
            .GetAwaiter().GetResult();
        if (!response.Accepted || response.Session != snapshot || closed != 0)
            throw new InvalidOperationException("Editor session STATUS modified or misidentified an Editor.");
        var wrong = EditorSessionWire.SendAsync(Environment.ProcessId,
            new(Guid.NewGuid().ToString("N"), "CLOSE", root,
                snapshot.StartedUtcTicks, "AnotherBoard")).GetAwaiter().GetResult();
        if (wrong.Accepted || closed != 0)
            throw new InvalidOperationException("Changed active dashboard admitted a destructive close.");
        var right = EditorSessionWire.SendAsync(Environment.ProcessId,
            new(Guid.NewGuid().ToString("N"), "CLOSE", root,
                snapshot.StartedUtcTicks, "ActiveBoard")).GetAwaiter().GetResult();
        if (!right.Accepted || closed != 1 ||
            !server.WaitForFinalCloseAcknowledgement(TimeSpan.FromSeconds(2)))
            throw new InvalidOperationException("Editor session close lacks an acknowledged completion.");
    }
}
