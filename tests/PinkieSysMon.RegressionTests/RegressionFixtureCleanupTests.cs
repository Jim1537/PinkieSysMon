using System.Runtime.InteropServices;
using System.Text.Json;

namespace PinkieSysMon.RegressionTests;

internal static class RegressionFixtureCleanupTests
{
    public static void OriginalFailuresAndNoRetry()
    {
        var scenario = new InvalidDataException("fixture original scenario failure");
        var cleanup = new IOException("fixture original sharing failure", unchecked((int)0x80070020));
        var deletes = 0;
        var observations = 0;
        var error = Capture(() => RegressionFixtureCleanup.Delete("fixture", scenario, true,
            delete: _ => { deletes++; throw cleanup; },
            capture: (root, disposed) =>
            {
                observations++;
                Check(root == "fixture" && disposed == true, "Cleanup lost its fixture/disposal context.");
                return "fixture observed holder";
            }));
        Check(error is AggregateException both && ReferenceEquals(both.InnerExceptions[0], scenario) &&
              both.InnerExceptions[1] is IOException augmented && ReferenceEquals(augmented.InnerException, cleanup) &&
              augmented.Message.Contains("fixture observed holder") && deletes == 1 && observations == 1,
            "Both original failures must survive one deletion attempt and one diagnostic snapshot.");

        deletes = 0;
        var brokenObserver = Capture(() => RegressionFixtureCleanup.Delete("fixture", delete: _ =>
            { deletes++; throw cleanup; }, capture: (_, _) => throw new InvalidOperationException("fixture diagnostic failure")));
        Check(brokenObserver is IOException retained && ReferenceEquals(retained.InnerException, cleanup) &&
              retained.Message.Contains("captureUnavailable=InvalidOperationException") && deletes == 1,
            "An unavailable observer must not replace, hide or retry the cleanup failure.");

        RegressionFixtureCleanup.Delete("fixture", delete: _ => deletes++, capture: (_, _) =>
            throw new InvalidOperationException("A successful cleanup must not invoke the observer."));
        Check(deletes == 2, "Successful cleanup must still execute its single ordinary deletion.");
    }

    public static void HeldFileSnapshotAndNativeLayout()
    {
        Check(Marshal.SizeOf<RegressionFixtureCleanup.RmUniqueProcess>() == 12 &&
              Marshal.SizeOf<RegressionFixtureCleanup.RmProcessInfo>() == 668 &&
              Marshal.OffsetOf<RegressionFixtureCleanup.RmProcessInfo>("ApplicationName").ToInt32() == 12 &&
              Marshal.OffsetOf<RegressionFixtureCleanup.RmProcessInfo>("ServiceName").ToInt32() == 524 &&
              Marshal.OffsetOf<RegressionFixtureCleanup.RmProcessInfo>("Restartable").ToInt32() == 664,
            "Restart Manager structures must retain the documented Unicode/FILETIME/BOOL native layout.");
        var root = Path.Combine(Path.GetTempPath(), "pinkie-fixture-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "palette.svg");
        File.WriteAllText(path, "<svg xmlns=\"http://www.w3.org/2000/svg\"/>");
        try
        {
            using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var error = Capture(() => RegressionFixtureCleanup.Delete(root, editorDisposed: true));
                Check(error is IOException augmented && augmented.InnerException is IOException original &&
                      original.HResult == unchecked((int)0x80070020) && File.Exists(path),
                    "A real denied fixture deletion must remain a failure and preserve its sharing code/file.");
                const string marker = "FIXTURE_CLEANUP_DIAGNOSTICS ";
                using var json = JsonDocument.Parse(error.Message[(error.Message.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..]);
                var snapshot = json.RootElement;
                var file = snapshot.GetProperty("files").EnumerateArray().Single();
                Check(snapshot.GetProperty("currentPid").GetInt32() == Environment.ProcessId &&
                      snapshot.GetProperty("editorDisposed").GetBoolean() &&
                      file.GetProperty("file").GetString() == "palette.svg" &&
                      !file.GetProperty("exclusiveReadOpened").GetBoolean() &&
                      file.GetProperty("hResult").GetString() == "0x80070020",
                    "The failure must include real relative-file/probe/disposal evidence without changing ownership.");
                var native = snapshot.GetProperty("restartManager");
                var start = native.GetProperty("StartCode");
                Check(native.GetProperty("Status").GetString() != "not-queried" &&
                      native.GetProperty("Users").GetArrayLength() <= 256 &&
                      (start.ValueKind == JsonValueKind.Number
                          ? start.GetInt32() != 0 || native.GetProperty("EndCode").ValueKind == JsonValueKind.Number ||
                              native.GetProperty("EndErrorType").ValueKind == JsonValueKind.String
                          : native.GetProperty("QueryErrorType").ValueKind == JsonValueKind.String),
                    "The native query must expose its result/access/visibility state and session-end outcome.");
                // No universal owner-visibility promise: Restart Manager can
                // report no users or access failure even for a real denied file.
            }
            using var available = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        }
        finally { RegressionFixtureCleanup.Delete(root); }
    }

    private static Exception Capture(Action action)
    {
        try { action(); }
        catch (Exception error) { return error; }
        throw new InvalidOperationException("Expected the fixture cleanup to fail.");
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
