using System.Reflection;
using System.Text.Json;
using System.Drawing;
using System.Windows.Forms;
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
            Check(!Text(log.Path).Contains("fixture failure", StringComparison.Ordinal),
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
        Check(!Text(log.Path).Contains("\"canvas\""),
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

    public static void EditIdentitiesAndPrivacy() => WithEditor((root, editor, log, messages) =>
    {
        var original = PrepareEditFixture(root, editor);
        var firstId = original.Id;
        Invoke(editor, "DuplicateSelectedWidgets");
        var widget = Get<C.WidgetDefinition>(editor, "_selectedWidget");
        var before = Invoke(editor, "CaptureSnapshot");
        const string privateName = "private-fixture-name-do-not-log";
        widget.Name = privateName;
        Invoke(editor, "OnPropertyChanged", "Name", before);
        var definition = Get<C.DashboardDefinition>(editor, "_definition");
        Invoke(editor, "SetPropertyGridTarget", definition.Canvas);
        before = Invoke(editor, "CaptureSnapshot");
        definition.Canvas.Width += 10;
        Invoke(editor, "OnPropertyChanged", "Width", before);
        Invoke(editor, "SelectWidget", original);
        Invoke(editor, "ToggleSelection", widget);
        before = Invoke(editor, "CaptureSnapshot");
        original.Name = widget.Name = privateName + "-bulk";
        Invoke(editor, "OnPropertyChanged", "Name", before);

        var edits = Edits(log);
        Check(edits.Select(e => e.GetProperty("action").GetString()).SequenceEqual(
                  new[] { "widget.add", "widget.duplicate", "property", "property", "property" }) &&
              Sample(edits[0], "widgets").SequenceEqual(new[] { firstId }) &&
              Sample(edits[1], "widgets").SequenceEqual(new[] { widget.Id }) && firstId != widget.Id &&
              Sample(edits[2], "widgets").SequenceEqual(new[] { widget.Id }) &&
              Sample(edits[2], "properties").SequenceEqual(new[] { "Name" }) &&
              !edits[2].GetProperty("canvas").GetBoolean() &&
              edits[3].GetProperty("canvas").GetBoolean() &&
              Sample(edits[3], "widgets").Length == 0 &&
              Sample(edits[3], "properties").SequenceEqual(new[] { "Width" }) &&
              Sample(edits[4], "widgets").Order().SequenceEqual(new[] { firstId, widget.Id }.Order()) &&
              Sample(edits[4], "properties").SequenceEqual(new[] { "Name" }),
            "Add/duplicate/property checkpoints must identify the actual widget or Canvas and descriptor.");
        Check(edits.All(e => e.GetProperty("scope").GetString() == "memory-and-history") &&
              edits.Select(e => e.GetProperty("operationId").GetString()).Distinct().Count() == 5 &&
              !Text(log.Path).Contains(privateName) && !File.Exists(log.ErrorsPath) && messages.Count == 0,
            "Normal edit evidence must be independently identified, value-free and outside Errors.");
    });

    public static void GestureCheckpointAndNoOp() => WithEditor((root, editor, log, messages) =>
    {
        var widget = PrepareEditFixture(root, editor);
        var startX = widget.X;
        var startY = widget.Y;
        Set(editor, "_dragUndoSnapshot", Invoke(editor, "CaptureSnapshot"));
        Set(editor, "_dragging", true);
        Set(editor, "_dragStartLogical", new PointF(0, 0));
        Get<Dictionary<C.WidgetDefinition, (float X, float Y)>>(editor, "_dragOrigins")[widget] = (startX, startY);
        foreach (var offset in new[] { 20, 30, 40 })
            Invoke(editor, "CanvasMouseMove", null, new MouseEventArgs(MouseButtons.Left, 0, offset, offset, 0));
        Check(Edits(log).Length == 1 && (widget.X != startX || widget.Y != startY),
            "Actual mouse-move updates must not log per-motion commits.");
        Invoke(editor, "CanvasMouseUp", null, new MouseEventArgs(MouseButtons.Left, 0, 40, 40, 0));
        var move = Edits(log).Last();
        Check(Edits(log).Length == 2 && move.GetProperty("action").GetString() == "geometry.move" &&
              Sample(move, "widgets").SequenceEqual(new[] { widget.Id }) &&
              Sample(move, "properties").SequenceEqual(new[] { "X", "Y" }),
            "A completed drag must emit exactly one checkpoint with geometry field names.");
        Invoke(editor, "MoveSelectedWidgets", 0f, 0f);
        Invoke(editor, "PushUndo", Invoke(editor, "CaptureSnapshot"));
        Check(Edits(log).Length == 2, "Unchanged nudge/checkpoints must not invent committed edits.");
        Invoke(editor, "MoveSelectedWidgets", 1f, 0f);
        Check(Edits(log).Last().GetProperty("action").GetString() == "geometry.nudge" && Edits(log).Length == 3,
            "One keyboard nudge must emit one changed checkpoint.");
    });

    public static void HierarchyAndPasteCorrelation() => WithEditor((root, editor, log, messages) =>
    {
        var original = PrepareEditFixture(root, editor);
        var definition = Get<C.DashboardDefinition>(editor, "_definition");
        const string privateGroup = "private-fixture-group-do-not-log";
        var transfer = DashboardJson.ParseEditorSnapshot(DashboardJson.SerializeEditorSnapshot(definition),
            DashboardCatalog.GetDefinitionPath(root, "Edits"));
        transfer.Widgets.Clear();
        transfer.Widgets.Add(DashboardJson.CloneWidget(original));
        var package = new WidgetTransferPackage
        {
            Version = 1, SourceDirectory = root,
            SnapshotJson = DashboardJson.SerializeEditorSnapshot(transfer),
            Groups = [new WidgetTransferGroup { Id = "transfer-group", Name = privateGroup }],
            RootGroupIds = ["transfer-group"],
            WidgetGroupIds = new() { [original.Id!] = "transfer-group" }
        };
        Invoke(editor, "PasteTransferredWidgets", package);
        var paste = Edits(log).Last();
        var operation = Operations(Read(log.Path), "editor.clipboard.paste").Last();
        var groupId = Sample(paste, "groups").Single();
        var pastedId = Sample(paste, "widgets").Single();
        Check(paste.GetProperty("action").GetString() == "clipboard.paste" &&
              paste.GetProperty("parentOperationId").GetString() == operation.GetProperty("operationId").GetString() &&
              pastedId != original.Id && groupId != "transfer-group",
            "Paste must identify destination IDs and correlate with its real containing operation.");
        Invoke(editor, "DuplicateSelectedGroups");
        var duplicate = Edits(log).Last();
        var duplicateGroup = Sample(duplicate, "groups").Single();
        var duplicateWidget = Sample(duplicate, "widgets").Single();
        Check(duplicate.GetProperty("action").GetString() == "group.duplicate" &&
              duplicateGroup != groupId && duplicateWidget != pastedId,
            "Group duplication must report the newly created logical participants.");
        Invoke(editor, "UngroupSelectedLayerNodes");
        var ungroup = Edits(log).Last();
        Check(ungroup.GetProperty("action").GetString() == "group.ungroup" &&
              Sample(ungroup, "groups").SequenceEqual(new[] { duplicateGroup }) &&
              Sample(ungroup, "widgets").SequenceEqual(new[] { duplicateWidget }) &&
              Get<DashboardEditorMetadata>(editor, "_dashboardMetadata").GetGroup(duplicateGroup) is null &&
              !Text(log.Path).Contains(privateGroup) && messages.Count == 0,
            "Ungroup must retain affected identities after clearing selection, without group names.");
    });

    public static void EditEvidenceBounds() => WithEditor((root, editor, log, messages) =>
    {
        PrepareEditFixture(root, editor);
        var definition = Get<C.DashboardDefinition>(editor, "_definition");
        var before = Invoke(editor, "CaptureSnapshot");
        definition.Canvas.Width++;
        var identifiers = Enumerable.Range(0, 70).Select(i => i == 0 ? new string('x', 200) : "fixture-" + i).ToArray();
        Invoke(editor, "CommitEdit", before, "fixture.bounds", identifiers, identifiers, identifiers, true, null);
        var bounded = Edits(log).Last();
        foreach (var field in new[] { "widgets", "groups", "properties" })
        {
            var data = bounded.GetProperty(field);
            Check(data.GetProperty("count").GetInt32() == 70 &&
                  data.GetProperty("omitted").GetInt32() == 6 &&
                  data.GetProperty("truncated").GetBoolean() &&
                  Sample(bounded, field).Length == 64 && Sample(bounded, field).All(value => value!.Length <= 128),
                "Every edit identity field must expose bounded samples and honest omission counts.");
        }
        before = Invoke(editor, "CaptureSnapshot");
        definition.Canvas.Width++;
        var undoCount = Get<System.Collections.ICollection>(editor, "_undo").Count;
        Invoke(editor, "CommitEdit", before, "fixture.broken-observer", BrokenIdentifiers(), null, null, true, null);
        Check(Get<System.Collections.ICollection>(editor, "_undo").Count == undoCount + 1 &&
              Get<C.DashboardDefinition>(editor, "_definition").Canvas.Width == definition.Canvas.Width &&
              Read(log.ErrorsPath).Any(e => e.GetProperty("message").GetString() == "Editor edit checkpoint evidence unavailable."),
            "A diagnostic enumeration failure must retain the accepted history and report the evidence gap.");
    });

    private static IEnumerable<string?> BrokenIdentifiers()
    {
        yield return "fixture";
        throw new InvalidOperationException("fixture observation failure");
    }

    private static C.WidgetDefinition PrepareEditFixture(string root, EditorForm editor)
    {
        CreateDashboard(root, "Edits");
        Check((bool)Invoke(editor, "LoadDashboard", "Edits")!, "Edit fixture document did not load.");
        Invoke(editor, "AddWidget", WidgetTypeContract.Value);
        return Get<C.WidgetDefinition>(editor, "_selectedWidget");
    }

    private static JsonElement[] Edits(FileLogger log) => Read(log.Path)
        .Select(record => record.GetProperty("message").GetString())
        .Where(message => message is not null && message.StartsWith('{'))
        .Select(message => JsonSerializer.Deserialize<JsonElement>(message!))
        .Where(value => value.TryGetProperty("eventName", out var name) && name.GetString() == "editor.edit.committed")
        .ToArray();

    private static string?[] Sample(JsonElement edit, string field) => edit.GetProperty(field)
        .GetProperty("sample").EnumerateArray().Select(value => value.GetString()).ToArray();

    private static void Set(EditorForm editor, string field, object? value) =>
        (typeof(EditorForm).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing Editor field: " + field)).SetValue(editor, value);

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
            // A final record exposes any earlier sink failure, including cleanup.
            const string completed = "Issue105 Editor fixture completed.";
            log.Info(completed);
            var session = Read(log.Path);
            Check(session.Any(record => record.GetProperty("message").GetString() == completed) &&
                  session.Concat(Read(log.ErrorsPath)).All(record => record.GetProperty("loggingFailures").GetInt32() == 0),
                "Editor fixture must retain its final record without diagnostic write failures.");
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

    private static string Text(string path) => LogFileWriter.UnderGate(path, "write", () =>
    {
        var directory = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(directory)) return "";
        var archives = Directory.EnumerateFiles(directory, Path.GetFileName(path) + ".*.archive")
            .Order(StringComparer.Ordinal);
        return string.Concat(archives.Select(File.ReadAllText)) +
            (File.Exists(path) ? File.ReadAllText(path) : "");
    });

    private static JsonElement[] Read(string path) => Text(path)
        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(line =>
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
