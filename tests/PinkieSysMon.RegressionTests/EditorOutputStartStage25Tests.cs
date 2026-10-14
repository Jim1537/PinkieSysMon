using PinkieSysMon.Editor;

namespace PinkieSysMon.RegressionTests;

internal static class EditorOutputStartStage25Tests
{
    public static void DirtyDocumentDecisions()
    {
        var choices = 0;
        var saves = 0;
        Check(EditorOutputStartWorkflow.ConfirmSave(false,
            () => { choices++; return SaveChangesChoice.Cancel; }, () => { saves++; return false; }) && choices == 0 && saves == 0,
            "A clean document must not prompt or save.");
        foreach (var (choice, saveSucceeds, expected) in new[]
        {
            (SaveChangesChoice.Save, true, true), (SaveChangesChoice.Save, false, false),
            (SaveChangesChoice.DontSave, false, true), (SaveChangesChoice.Cancel, true, false)
        })
        {
            var f = new Runtime();
            var saveCalls = 0;
            var ready = EditorOutputStartWorkflow.ConfirmSave(true, () => choice, () => { saveCalls++; return saveSucceeds; });
            if (ready) f.Run("A", "a");
            Check(ready == expected && saveCalls == (choice == SaveChangesChoice.Save ? 1 : 0),
                "Save, Don't Save and Cancel must preserve the established editor decision contract.");
            Check(expected ? f.Calls.Contains("START:a") : f.Calls.Count == 0,
                "Canceled or failed Save must prevent even Runtime probing/launch; Don't Save must not force persistence.");
        }
    }

    public static void SelectedDashboardChoices()
    {
        var same = new Runtime();
        Check(same.Run("A", "a")?.Success == true && same.Calls.Contains("START:a") &&
              same.Calls.All(c => !c.StartsWith("CHOICE:", StringComparison.Ordinal) && !c.StartsWith("ASSIGN:", StringComparison.Ordinal)) &&
              same.Outputs.Single(o => o.TargetId == "b").Dashboard == "B",
            "Comparison must use A's assignment, ignore a misleading top-level dashboard and reload only A.");
        var use = new Runtime();
        use.Choices.Enqueue(OutputDashboardChoice.UseEditorDashboard);
        Check(use.Run("Editor", "a")?.Success == true && use.Calls.Contains("ASSIGN:a:Editor") && !use.Calls.Contains("START:a") &&
              use.Calls.All(c => c != "START:b") && use.Outputs.Single(o => o.TargetId == "a").Dashboard == "Editor" &&
              use.Outputs.Single(o => o.TargetId == "b").Dashboard == "B",
            "Use editor dashboard must assign and reload only the selected active target without a duplicate reload.");
        var keep = new Runtime();
        keep.Choices.Enqueue(OutputDashboardChoice.KeepAssignedDashboard);
        Check(keep.Run("Editor", "a")?.Success == true && keep.Calls.Contains("START:a") &&
              keep.Calls.All(c => !c.StartsWith("ASSIGN:", StringComparison.Ordinal)) &&
              keep.Outputs.Single(o => o.TargetId == "a").Dashboard == "A",
            "Keep assigned must reload the target's original board without changing its assignment.");
        var cancel = new Runtime();
        cancel.Choices.Enqueue(OutputDashboardChoice.Cancel);
        Check(cancel.Run("Editor", "a") is null && cancel.Mutations.Length == 0,
            "Mismatch cancellation must not assign, start or launch Runtime.");
        const string opaque = "panel\t\"quoted\" ";
        var ids = new Runtime();
        ids.Set("a", o => o with { TargetId = opaque });
        Check(ids.Run("A", opaque)?.Success == true && ids.Calls.Contains("START:" + opaque),
            "Opaque Output IDs must survive Editor planning and dispatch without trimming/delimiter shortcuts.");
    }

    public static void AbsentRuntimePolicy()
    {
        var cancel = new Runtime { Running = false };
        cancel.Choices.Enqueue(OutputDashboardChoice.Cancel);
        Check(cancel.Run("Editor", "a") is null && cancel.Calls.Contains("CONFIG") && cancel.Mutations.Length == 0,
            "Offline mismatch cancellation must leave Runtime absent and configuration unchanged.");
        var launch = new Runtime { Running = false };
        launch.Choices.Enqueue(OutputDashboardChoice.UseEditorDashboard);
        var launched = launch.Run("Editor", "a");
        Check(launched?.Success == true && launch.Mutations.SequenceEqual(new[] { "LAUNCH", "ASSIGN:a:Editor" }) &&
              launch.Calls.IndexOf("CHOICE:a") < launch.Calls.IndexOf("LAUNCH") &&
              launch.Outputs.Single(o => o.TargetId == "b").IsActive &&
              !launch.Outputs.Single(o => o.TargetId == "c").IsActive &&
              launched.Message.Contains("2 active of 3 configured Outputs", StringComparison.Ordinal),
            "Absent Runtime must launch normally after choice, report all saved-enabled startup and leave disabled hardware inactive.");
        var unresponsive = new Runtime { Running = false, ProcessRunning = true };
        Check(unresponsive.Run("Editor", "a")?.Success == false && !unresponsive.Calls.Contains("CONFIG") && unresponsive.Mutations.Length == 0,
            "An existing unresponsive Runtime must not trigger a duplicate launch or offline assignment guess.");
        var failedLaunch = new Runtime { Running = false, FailLaunch = true };
        Check(failedLaunch.Run("A", "a")?.Success == false && failedLaunch.Mutations.SequenceEqual(new[] { "LAUNCH" }),
            "An unconfirmed startup must prevent all Output commands.");
        var changedLaunch = new Runtime { Running = false, OnLaunch = runtime => runtime.Set("a", o => o with { Dashboard = "Changed" }) };
        var changed = changedLaunch.Run("A", "a");
        Check(changed?.Success == false && changedLaunch.Mutations.SequenceEqual(new[] { "LAUNCH" }) &&
              changed.Message.Contains("Runtime available: 2 active", StringComparison.Ordinal),
            "Post-launch drift must prevent target dispatch and still report the confirmed normal startup.");
    }

    public static void FailuresAndVerification()
    {
        var assign = new Runtime { FailAssignment = true };
        assign.Choices.Enqueue(OutputDashboardChoice.UseEditorDashboard);
        Check(assign.Run("Editor", "a")?.Success == false && !assign.Calls.Contains("START:a") &&
              assign.Outputs.Single(o => o.TargetId == "a").Dashboard == "A",
            "A rejected assignment must not be followed by Start or by any off-target command.");
        var failedStart = new Runtime { FailStartId = "a", StopAllOnAssignment = true };
        failedStart.Choices.Enqueue(OutputDashboardChoice.UseEditorDashboard);
        var partial = failedStart.Run("Editor", "a");
        Check(partial is { Success: false } && partial.Message.Contains("Completed actions:", StringComparison.Ordinal) &&
              partial.Message.Contains("Assigned 'Editor'", StringComparison.Ordinal) &&
              failedStart.Outputs.Single(o => o.TargetId == "a").Dashboard == "Editor" &&
              failedStart.Outputs.Single(o => o.TargetId == "b").Dashboard == "B",
            "Failure after a confirmed assignment must report that partial result without claiming a started Output.");
        var ackOnly = new Runtime { StartWithoutState = true };
        ackOnly.StopAllOutputs();
        Check(ackOnly.Run("A", "a")?.Success == false,
            "A successful IPC acknowledgement must not replace observed active/assigned state.");
        var assignAckOnly = new Runtime { AssignmentWithoutState = true };
        assignAckOnly.Choices.Enqueue(OutputDashboardChoice.UseEditorDashboard);
        var unconfirmed = assignAckOnly.Run("Editor", "a");
        Check(unconfirmed is { Success: false } && !unconfirmed.Message.Contains("Assigned 'Editor'", StringComparison.Ordinal) &&
              !assignAckOnly.Calls.Contains("START:a"),
            "An unobserved assignment must remain unconfirmed and must not be listed as completed.");
        var deviceError = new Runtime { StartedState = "ERROR" };
        Check(deviceError.Run("A", "a")?.Success == false,
            "Runtime-reported Output error must not become a green Start result.");
        var waiting = new Runtime { StartedState = "WAITING" };
        waiting.Set("a", o => o with { Kind = OutputTargetContract.TrofeoKind, DeviceEnabled = true, BoardEnabled = null });
        var pending = waiting.Run("A", "a");
        Check(pending is { Success: true } && pending.Message.Contains("state: WAITING", StringComparison.Ordinal),
            "A started lifecycle waiting for a device must report that state without claiming a connected display.");
    }

    public static void AllPlanningAndPartialResults()
    {
        var cancel = new Runtime { Running = false };
        cancel.Choices.Enqueue(OutputDashboardChoice.UseEditorDashboard);
        cancel.Choices.Enqueue(OutputDashboardChoice.Cancel);
        Check(cancel.Run("Editor", null) is null && cancel.Mutations.Length == 0,
            "Canceling the second All decision must not apply the first choice or launch Runtime.");
        var all = new Runtime();
        all.Choices.Enqueue(OutputDashboardChoice.UseEditorDashboard);
        all.Choices.Enqueue(OutputDashboardChoice.KeepAssignedDashboard);
        Check(all.Run("Editor", null)?.Success == true && all.Calls.Contains("ASSIGN:a:Editor") &&
              !all.Calls.Contains("START:a") && all.Calls.Contains("START:b") &&
              all.Mutations.All(c => !c.Contains(":c", StringComparison.Ordinal)) &&
              all.Calls.IndexOf("CHOICE:b") < all.Calls.IndexOf("ASSIGN:a:Editor") &&
              all.Outputs.Single(o => o.TargetId == "b").Dashboard == "B" &&
              all.Outputs.Single(o => o.TargetId == "c").OutputEnabled == false,
            "All must collect every eligible choice first, address only those IDs and retain disabled provider-only hardware.");
        var failed = new Runtime { FailStartId = "b" };
        failed.Choices.Enqueue(OutputDashboardChoice.KeepAssignedDashboard);
        failed.Choices.Enqueue(OutputDashboardChoice.KeepAssignedDashboard);
        var partial = failed.Run("Editor", null);
        Check(partial is { Success: false } && partial.Message.Contains("Output 'A' loaded 'A'", StringComparison.Ordinal) &&
              !partial.Message.Contains("Output 'B' loaded", StringComparison.Ordinal),
            "Sequential All failure must report the confirmed earlier Output without inventing all-or-nothing completion.");
        var none = new Runtime();
        foreach (var id in new[] { "a", "b" })
            none.Set(id, o => o with { OutputEnabled = false, BoardEnabled = false, IsActive = false, IsConnected = false, UsbState = "DISABLED" });
        Check(none.Run("Editor", null)?.Success == false && none.Mutations.Length == 0,
            "No eligible Output must mean no implicit Board/Device enable or start.");
    }

    public static void LegacyDriftAndMenu()
    {
        var legacy = new Runtime();
        legacy.Set("a", o => new RuntimeOutputStatus(o.TargetId, o.TargetName, false, false, "STOPPED"));
        Check(legacy.Run("A", "a")?.Success == false && legacy.Mutations.Length == 0,
            "Missing additive metadata must fail closed instead of guessing from global Dashboard or disk.");
        var changed = new Runtime();
        changed.OnChoice = runtime => runtime.Set("a", o => o with { Dashboard = "Changed" });
        Check(changed.Run("Editor", "a")?.Success == false && changed.Mutations.Length == 0,
            "Assignment drift while a choice dialog is open must be detected before commands.");
        var added = new Runtime();
        added.OnChoice = runtime => runtime.Set("c", o => o with { OutputEnabled = true });
        Check(added.Run("Editor", null)?.Success == false && added.Mutations.Length == 0,
            "All must not silently include a newly enabled Output that received no dashboard decision.");
        var f = new Runtime();
        var config = new AppConfig { Outputs = new OutputConfig { Targets =
        [
            new() { Id = "a", Name = "A", Kind = OutputTargetContract.DesktopKind,
                Transport = OutputTargetContract.DesktopTransport, Dashboard = "A", Desktop = new() },
            new() { Id = "c", Name = "C", Dashboard = "C", ProviderEnabled = true, DeviceEnabled = false }
        ] } };
        var state = ScreenMenuContract.Build(config.Outputs.Targets, true, f.Snapshot(), false);
        Check(state.Targets[0].CanStartOrReload && state.Targets[0].StatusText.Contains("Dashboard: A", StringComparison.Ordinal) &&
              !state.Targets[1].CanStartOrReload,
            "Menu must show per-target assignment and deny disabled physical activation independently of Provider Enabled.");
        var offline = EditorOutputStartWorkflow.FromConfiguration(config);
        Check(offline.Outputs.All(o => !o.IsActive && !o.IsConnected) && offline.Outputs[0].Dashboard == "A" &&
              offline.Outputs[1].OutputEnabled == false && offline.Outputs[1].ProviderEnabled == true,
            "Offline planning may expose configured assignments but must never fabricate observed sessions/connections.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Runtime
    {
        public bool Running = true, ProcessRunning, FailLaunch, FailAssignment, StartWithoutState, AssignmentWithoutState, StopAllOnAssignment;
        public string? FailStartId;
        public string StartedState = "DISPLAYING";
        public Action<Runtime>? OnChoice, OnLaunch;
        public readonly Queue<OutputDashboardChoice> Choices = new();
        public readonly List<string> Calls = [];
        public readonly List<RuntimeOutputStatus> Outputs =
        [
            Output("a", "A", true), Output("b", "B", true),
            new("c", "C", false, false, "DISABLED")
            {
                Kind = OutputTargetContract.TrofeoKind, Dashboard = "C", OutputEnabled = false, ProviderEnabled = true, DeviceEnabled = false
            }
        ];
        public string[] Mutations => Calls.Where(c => c == "LAUNCH" ||
            c.StartsWith("ASSIGN:", StringComparison.Ordinal) || c.StartsWith("START:", StringComparison.Ordinal)).ToArray();
        public RuntimeStatusSnapshot Snapshot() => new("Misleading global board", Outputs.ToArray());
        public void Set(string id, Func<RuntimeOutputStatus, RuntimeOutputStatus> edit)
        {
            var index = Outputs.FindIndex(o => o.TargetId == id);
            Outputs[index] = edit(Outputs[index]);
        }
        // Simulate a concurrent global shutdown after a confirmed IPC response.
        public void StopAllOutputs()
        {
            for (var i = 0; i < Outputs.Count; i++)
                Outputs[i] = Outputs[i] with { IsActive = false, IsConnected = false,
                    UsbState = Outputs[i].OutputEnabled == true ? "STOPPED" : "DISABLED" };
        }
        public RuntimeIpcResponse? Run(string board, string? target) =>
            EditorOutputStartWorkflow.RunAsync(board, target, Actions()).GetAwaiter().GetResult();
        private EditorOutputStartActions Actions() => new(
            () =>
            {
                Calls.Add("STATUS");
                return Task.FromResult(new RuntimeIpcStatusResponse(Running, Running ? "" : "Not running", Running ? Snapshot() : null));
            },
            () => { Calls.Add("PROCESS"); return ProcessRunning; },
            () => { Calls.Add("CONFIG"); return new RuntimeStatusSnapshot("Configured only", Outputs.Select(o => o with { IsActive = false, IsConnected = false, UsbState = "NOT_RUNNING" }).ToArray()); },
            () =>
            {
                Calls.Add("LAUNCH");
                if (FailLaunch) return Task.FromResult(new RuntimeIpcStatusResponse(false, "Synthetic startup failure", null));
                Running = true;
                for (var i = 0; i < Outputs.Count; i++)
                    Outputs[i] = Outputs[i] with { IsActive = Outputs[i].OutputEnabled == true,
                        IsConnected = Outputs[i].OutputEnabled == true,
                        UsbState = Outputs[i].OutputEnabled == true ? "DISPLAYING" : "DISABLED" };
                OnLaunch?.Invoke(this);
                return Task.FromResult(new RuntimeIpcStatusResponse(true, "", Snapshot()));
            },
            output =>
            {
                Calls.Add("CHOICE:" + output.TargetId);
                var edit = OnChoice;
                OnChoice = null;
                edit?.Invoke(this);
                return Choices.Count == 0 ? OutputDashboardChoice.KeepAssignedDashboard : Choices.Dequeue();
            },
            (id, board) =>
            {
                Calls.Add($"ASSIGN:{id}:{board}");
                if (!FailAssignment && !AssignmentWithoutState) Set(id, o => o with { Dashboard = board });
                if (!FailAssignment && StopAllOnAssignment) StopAllOutputs();
                return Task.FromResult(new RuntimeIpcResponse(!FailAssignment, FailAssignment ? "Synthetic assignment failure" : "Assigned"));
            },
            id =>
            {
                Calls.Add("START:" + id);
                var failed = id == FailStartId;
                if (!failed && !StartWithoutState) Set(id, o => o with { IsActive = true, IsConnected = StartedState == "DISPLAYING", UsbState = StartedState });
                return Task.FromResult(new RuntimeIpcResponse(!failed, failed ? "Synthetic start failure" : "Started"));
            });
        private static RuntimeOutputStatus Output(string id, string board, bool active) => new(id, board, active, active, active ? "DISPLAYING" : "STOPPED")
        {
            Kind = OutputTargetContract.DesktopKind, Dashboard = board, OutputEnabled = true, BoardEnabled = true, ProviderEnabled = false
        };
    }
}
