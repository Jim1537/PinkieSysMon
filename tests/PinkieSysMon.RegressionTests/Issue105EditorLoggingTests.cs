using System.Reflection;
using System.Text.Json;
using PinkieSysMon;
using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static class Issue105EditorLoggingTests
{
    public static void OperationOutcomesAndCorrelation()
    {
        Sandbox(root =>
        {
            var log = new FileLogger(Path.Combine(root, "session.log"), "Editor");
            using (var cancelled = new LogOperation(log, "fixture.cancel")) cancelled.Cancel("user choice");
            using (var incomplete = new LogOperation(log, "fixture.incomplete")) incomplete.Phase("waiting");
            Check(!File.Exists(log.ErrorsPath), "Cancellation and unconfirmed completion must not invent errors.");
            var original = new IOException("original fixture failure");
            string operationId;
            using (var failed = new LogOperation(log, "fixture.operation", "logical-board", "parent-id"))
            {
                operationId = failed.Id;
                failed.Phase("json.commit");
                failed.Fail(original);
                failed.RecordFailure(new IOException("cleanup fixture failure"), "cleanup");
            }
            log.Error("propagated boundary", original);
            var errors = Read(log.ErrorsPath);
            var operations = Operations(errors, "fixture.operation");
            Check(operations.Length == 2 && operations.All(e =>
                    e.GetProperty("operationId").GetString() == operationId &&
                    e.GetProperty("parentOperationId").GetString() == "parent-id"),
                "Primary and secondary failures must retain the same operation and parent.");
            Check(operations.Select(e => e.GetProperty("phase").GetString())
                    .SequenceEqual(new[] { "json.commit", "cleanup" }),
                "The primary failure phase must survive cleanup reporting.");
            Check(errors.Count(e => e.GetProperty("exception").ValueKind == JsonValueKind.String) == 2 &&
                  errors.Count(e => e.GetProperty("eventId").GetString() ==
                      errors[0].GetProperty("eventId").GetString()) == 2,
                "Propagation must add context without repeating the original exception.");
            var session = Read(log.Path);
            Check(Operations(session, "fixture.cancel").Last().GetProperty("outcome").GetString() == "cancelled" &&
                  Operations(session, "fixture.incomplete").Last().GetProperty("outcome").GetString() == "incomplete",
                "Disposed scopes must not claim unobserved success.");
            Check(!File.ReadAllText(log.Path).Contains("fixture failure", StringComparison.Ordinal),
                "Complete failures must stay out of the session log.");
        });
    }

    public static void RecoveryFlushPreservesOtherIncidents()
    {
        Sandbox(root =>
        {
            var log = new FileLogger(Path.Combine(root, "session.log"), "Editor");
            foreach (var key in new[] { "preview", "other" })
                for (var i = 0; i < 3; i++)
                    log.ErrorThrottled(key, TimeSpan.FromHours(1), key + " failed", new IOException(key));
            log.FlushSuppressed("preview");
            var afterRecovery = Read(log.ErrorsPath);
            Check(afterRecovery.Length == 3 &&
                  afterRecovery.Last().GetProperty("message").GetString()!.Contains("key=preview;") &&
                  afterRecovery.Last().GetProperty("message").GetString()!.Contains("suppressed=2"),
                "Recovery must flush only the selected incident's pending count.");
            log.ErrorThrottled("preview", TimeSpan.FromHours(1), "preview failed", new IOException("preview"));
            Check(Read(log.ErrorsPath).Length == 4, "A new incident after recovery must not be suppressed.");
            log.FlushSuppressed();
            var final = Read(log.ErrorsPath).Last().GetProperty("message").GetString()!;
            Check(final.Contains("\"key\":\"other\"") && final.Contains("\"suppressed\":2") &&
                  !final.Contains("\"key\":\"preview\""),
                "Recovery must preserve another incident until its own final flush.");
        });
    }

    public static void LoadSaveAndHistory() => WithEditor((root, editor, log, messages) =>
    {
        CreateDashboard(root, "Editable");
        Check((bool)Invoke(editor, "LoadDashboard", "Editable")!, "Fixture document did not load.");
        var definition = Get<C.DashboardDefinition>(editor, "_definition");
        var prior = Invoke(editor, "CaptureSnapshot")!;
        definition.Canvas.Width += 10;
        Invoke(editor, "PushUndo", prior);
        Invoke(editor, "MarkDirty");
        Invoke(editor, "Undo");
        Invoke(editor, "Redo");
        Check((bool)Invoke(editor, "SaveDashboard")!, "Fixture document did not save.");
        var records = Read(log.Path);
        foreach (var name in new[] { "editor.dashboard.load", "editor.dashboard.save", "editor.history.undo", "editor.history.redo" })
        {
            var events = Operations(records, name);
            Check(events.First().GetProperty("outcome").GetString() == "started" &&
                  events.Last().GetProperty("outcome").GetString() == "succeeded" &&
                  events.Select(e => e.GetProperty("operationId").GetString()).Distinct().Count() == 1 &&
                  events.All(e => e.GetProperty("elapsedMs").GetDouble() >= 0),
                name + " must have one timed operation identity and an observed terminal outcome.");
        }
        var save = Operations(records, "editor.dashboard.save");
        Check(save.Where(e => e.GetProperty("outcome").GetString() == "progress")
                .Select(e => e.GetProperty("phase").GetString())
                .SequenceEqual(new[] { "serialize", "json.write", "json.commit", "metadata" }),
            "Save must expose the JSON and metadata boundaries in order.");
        Check(records.Count(e => e.GetProperty("message").GetString()!.Contains("editor.edit.committed")) == 1 &&
              !Get<bool>(editor, "_dirty") && messages.Count == 0,
            "One checkpoint must produce one edit summary and preserve the existing clean-after-Save behavior.");
        Check(!File.ReadAllText(log.Path).Contains("\"canvas\""),
            "Operation records must not contain serialized dashboard content.");
    });

    public static void LockedJsonSaveFailure() => WithEditor((root, editor, log, messages) =>
    {
        var path = CreateDashboard(root, "Locked");
        Check((bool)Invoke(editor, "LoadDashboard", "Locked")!, "Fixture document did not load.");
        var before = File.ReadAllBytes(path);
        Get<C.DashboardDefinition>(editor, "_definition").Canvas.Width += 10;
        Invoke(editor, "MarkDirty");
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Check(!(bool)Invoke(editor, "SaveDashboard")!, "A denied JSON replacement must return failure.");
        }
        Check(File.ReadAllBytes(path).SequenceEqual(before) && Get<bool>(editor, "_dirty") && messages.Count == 1,
            "A failed JSON commit must preserve disk bytes, dirty state and its existing user notification.");
        var errors = Read(log.ErrorsPath);
        var failure = Operations(errors, "editor.dashboard.save").Single();
        Check(failure.GetProperty("outcome").GetString() == "failed" &&
              failure.GetProperty("phase").GetString() == "json.commit" &&
              failure.GetProperty("detail").GetString() == "jsonCommitted=False",
            "A real denied replacement must identify the failed phase and uncommitted JSON.");
        Check(errors.Any(e => e.GetProperty("exception").ValueKind == JsonValueKind.String) &&
              Operations(Read(log.Path), "editor.dashboard.save")
                  .All(e => e.GetProperty("outcome").GetString() != "succeeded"),
            "Failed Save must retain the original exception and never log success.");
    });

    public static void MetadataPartialSave() => WithEditor((root, editor, log, messages) =>
    {
        var path = CreateDashboard(root, "Partial");
        Check((bool)Invoke(editor, "LoadDashboard", "Partial")!, "Fixture document did not load.");
        var metadataPath = Path.Combine(Path.GetDirectoryName(path)!, ".editor.json");
        if (File.Exists(metadataPath)) File.Delete(metadataPath);
        Directory.CreateDirectory(metadataPath); // Deliberately block replacement of this fixture-only metadata.
        var definition = Get<C.DashboardDefinition>(editor, "_definition");
        definition.Canvas.Width += 10;
        Invoke(editor, "MarkDirty");
        Check(!(bool)Invoke(editor, "SaveDashboard")!, "Metadata failure must leave Save unsuccessful.");
        Check(C.DashboardDefinition.Load(path).Canvas.Width == definition.Canvas.Width &&
              Get<bool>(editor, "_dirty") && Get<bool>(editor, "_loadRequiresSave") && messages.Count == 1,
            "Committed JSON and unsaved metadata must retain the existing partial-save semantics.");
        var session = Operations(Read(log.Path), "editor.dashboard.save");
        var failure = Operations(Read(log.ErrorsPath), "editor.dashboard.save").Single();
        Check(session.Last().GetProperty("outcome").GetString() == "partial" &&
              failure.GetProperty("phase").GetString() == "metadata" &&
              failure.GetProperty("operationId").GetString() == session.Last().GetProperty("operationId").GetString(),
            "The retained metadata failure and session partial result must identify the same Save.");
        Directory.Delete(metadataPath);
        Check((bool)Invoke(editor, "SaveDashboard")! && !Get<bool>(editor, "_dirty"),
            "Retry after removing the fixture obstruction must recover and clear dirty state.");
    });

    public static void FailedLoadPreservesDocument() => WithEditor((root, editor, log, messages) =>
    {
        CreateDashboard(root, "Current");
        Check((bool)Invoke(editor, "LoadDashboard", "Current")!, "Fixture document did not load.");
        var definition = Get<C.DashboardDefinition>(editor, "_definition");
        Check(!(bool)Invoke(editor, "LoadDashboard", "Missing")!, "Missing document must return failure.");
        Check(ReferenceEquals(definition, Get<C.DashboardDefinition>(editor, "_definition")) &&
              Get<string>(editor, "_dashboardName") == "Current" && messages.Count == 1,
            "An early load failure must preserve the current document and its existing notification.");
        var failure = Operations(Read(log.ErrorsPath), "editor.dashboard.load").Single();
        Check(failure.GetProperty("subject").GetString() == "Missing" &&
              failure.GetProperty("phase").GetString() == "json.read" &&
              failure.GetProperty("outcome").GetString() == "failed",
            "Missing-file evidence must remain self-contained after session replacement.");
    });

    public static void InvalidMetadataLoadBackup() => WithEditor((root, editor, log, messages) =>
    {
        var path = CreateDashboard(root, "InvalidMetadata");
        var metadataPath = Path.Combine(Path.GetDirectoryName(path)!, ".editor.json");
        const string invalidMetadata = "{invalid fixture metadata";
        File.WriteAllText(metadataPath, invalidMetadata);
        Check((bool)Invoke(editor, "LoadDashboard", "InvalidMetadata")!, "A metadata fallback must still load the document.");
        var backups = Directory.GetFiles(Path.GetDirectoryName(path)!, ".editor.json.invalid-*.bak");
        Check(backups.Length == 1 && File.ReadAllText(backups[0]) == invalidMetadata && messages.Count == 1,
            "The original metadata backup and warning must remain intact.");
        var session = Operations(Read(log.Path), "editor.dashboard.load");
        var failure = Operations(Read(log.ErrorsPath), "editor.dashboard.load").Single();
        Check(session.Last().GetProperty("outcome").GetString() == "partial" &&
              failure.GetProperty("detail").GetString()!.Contains("backupPreserved=True") &&
              session.Last().GetProperty("operationId").GetString() == failure.GetProperty("operationId").GetString(),
            "Metadata fallback must retain the source failure, backup result and partial load identity.");
    });

    private static void WithEditor(Action<string, EditorForm, FileLogger, List<string>> test)
    {
        var applicationRoot = ApplicationRootLocator.Find(AppContext.BaseDirectory);
        var root = Path.Combine(applicationRoot, "artifacts", "issue105-editor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Exception? scenarioFailure = null;
        EditorForm? editor = null;
        try
        {
            Issue84EditorStartupTests.CreateEditorIconFixtures(root);
            var log = new FileLogger(Path.Combine(root, "logs", "PinkieSysMon.Editor.log"), "Editor");
            var messages = new List<string>();
            using (editor = new EditorForm(root, null, log, (message, _, _) => messages.Add(message)))
                test(root, editor, log, messages);
        }
        catch (Exception error) { scenarioFailure = error; throw; }
        finally { RegressionFixtureCleanup.Delete(root, scenarioFailure, editor?.IsDisposed); }
    }

    private static string CreateDashboard(string root, string name)
    {
        var directory = Path.Combine(DashboardCatalog.GetRoot(root), name);
        EditorForm.WriteNewDashboardFile(directory, 640, 640, 0);
        return DashboardCatalog.GetDefinitionPath(root, name);
    }

    private static object? Invoke(EditorForm editor, string method, params object?[] args) =>
        (typeof(EditorForm).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing Editor method: " + method)).Invoke(editor, args);

    private static T Get<T>(EditorForm editor, string field) =>
        (T)(typeof(EditorForm).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing Editor field: " + field)).GetValue(editor)!;

    private static JsonElement[] Read(string path) => !File.Exists(path) ? [] :
        File.ReadAllLines(path).Select(line =>
        {
            using var json = JsonDocument.Parse(line[line.IndexOf('{')..]);
            return json.RootElement.Clone();
        }).ToArray();

    private static JsonElement[] Operations(IEnumerable<JsonElement> records, string operation) => records
        .Select(record => record.GetProperty("message").GetString())
        .Where(message => message is not null && message.StartsWith('{'))
        .Select(message =>
        {
            using var json = JsonDocument.Parse(message!);
            return json.RootElement.Clone();
        })
        .Where(value => value.TryGetProperty("operation", out var name) && name.GetString() == operation)
        .ToArray();

    private static void Sandbox(Action<string> work)
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkie-operation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { work(root); }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
