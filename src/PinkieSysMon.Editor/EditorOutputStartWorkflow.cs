using PinkieSysMon;

namespace PinkieSysMon.Editor;

internal enum OutputDashboardChoice
{
    UseEditorDashboard,
    KeepAssignedDashboard,
    Cancel
}

internal sealed record EditorOutputStartActions(
    Func<Task<RuntimeIpcStatusResponse>> GetStatus,
    Func<bool> ProcessPresent,
    Func<RuntimeStatusSnapshot> ReadConfiguration,
    Func<Task<RuntimeIpcStatusResponse>> LaunchRuntime,
    Func<RuntimeOutputStatus, OutputDashboardChoice> ChooseDashboard,
    Func<string, string, Task<RuntimeIpcResponse>> AssignDashboard,
    Func<string, Task<RuntimeIpcResponse>> StartOrReload,
    Func<Task<RuntimeIpcResponse>> ReloadAll);

// UI callbacks stay on the caller's context. Tests supply only file/IPC/dialog fakes.
internal static class EditorOutputStartWorkflow
{
    public static bool ConfirmSave(bool dirty, Func<SaveChangesChoice> choose, Func<bool> save) =>
        !dirty || (choose() switch
        {
            SaveChangesChoice.Save => save(),
            SaveChangesChoice.DontSave => true,
            _ => false
        });

    public static RuntimeStatusSnapshot FromConfiguration(AppConfig config) => new(
        "(Runtime not running)", config.Outputs.Targets.Select(target =>
        {
            var desktop = target.Kind == OutputTargetContract.DesktopKind;
            return new RuntimeOutputStatus(target.Id, target.Name, false, false, "NOT_RUNNING")
            {
                Kind = target.Kind, Dashboard = target.Dashboard,
                OutputEnabled = OutputTargetContract.IsEnabled(target),
                ProviderEnabled = target.ProviderEnabled,
                BoardEnabled = desktop ? target.Desktop?.BoardEnabled : null,
                DeviceEnabled = desktop ? null : target.DeviceEnabled
            };
        }).ToArray());

    // Null target means the explicitly requested All action. All decisions precede
    // any launch/assignment/reload. All applies saved common settings once after
    // confirmed per-ID assignments; it is not a series of target-only reloads.
    public static async Task<RuntimeIpcResponse?> RunAsync(
        string editorDashboard, string? targetId, EditorOutputStartActions actions)
    {
        var completed = new List<string>();
        try
        {
            var initial = await actions.GetStatus();
            var running = initial.Success && initial.Status is not null;
            if (!running && actions.ProcessPresent())
                return Failed("A Runtime process exists but its status is unavailable. A duplicate Runtime was not launched.", completed);
            var snapshot = running ? initial.Status! : actions.ReadConfiguration();
            if (targetId is null && snapshot.Outputs.Any(o => o.OutputEnabled is null))
                return Failed("Runtime does not report per-Output enablement. Update Runtime before Reload All.", completed);
            var selected = targetId is null
                ? snapshot.Outputs.Where(o => o.OutputEnabled == true).ToArray()
                : snapshot.Outputs.Where(o => o.TargetId.Equals(targetId, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (targetId is not null && selected.Length != 1)
                return Failed("The selected Output is missing or ambiguous.", completed);
            if (selected.Select(o => o.TargetId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != selected.Length)
                return Failed("Runtime returned ambiguous Output IDs.", completed);

            var plans = new List<StartPlan>();
            foreach (var output in selected)
            {
                if (output.OutputEnabled is null)
                    return Failed($"Runtime does not report enablement for '{output.TargetName}'. Update Runtime first.", completed);
                if (output.OutputEnabled != true)
                    return Failed($"Output '{output.TargetName}' is disabled. Turn on Enabled for this Output in Settings first.", completed);
                if (string.IsNullOrWhiteSpace(output.Dashboard) ||
                    output.Kind != OutputTargetContract.DesktopKind && !OutputTargetContract.IsPhysicalKind(output.Kind))
                    return Failed($"Runtime does not report the assignment/type for '{output.TargetName}'. Update Runtime first.", completed);
                var differs = !editorDashboard.Equals(output.Dashboard, StringComparison.OrdinalIgnoreCase);
                var choice = differs ? actions.ChooseDashboard(output) : OutputDashboardChoice.KeepAssignedDashboard;
                if (choice == OutputDashboardChoice.Cancel) return null;
                plans.Add(new(output, choice == OutputDashboardChoice.UseEditorDashboard ? editorDashboard : output.Dashboard!,
                    choice == OutputDashboardChoice.UseEditorDashboard && differs));
            }

            if (!running)
            {
                var launched = await actions.LaunchRuntime();
                if (!launched.Success || launched.Status is null)
                    return Failed(launched.Message, completed);
                completed.Add($"Runtime available: {launched.Status.Outputs.Count(output => output.IsActive)} active of {launched.Status.Outputs.Length} configured Outputs.");
            }

            // Recheck the actual Runtime after dialogs/launch; disk planning never
            // substitutes for the running process's assignment.
            var ready = await actions.GetStatus();
            if (!ready.Success || ready.Status is null)
                return Failed("Runtime status could not be confirmed before dispatch: " + ready.Message, completed);
            if (targetId is null && !HasEnabledSet(ready.Status, plans))
                return Failed("Enabled Outputs changed while choosing dashboards. Retry Reload All.", completed);
            foreach (var plan in plans)
                if (!Matches(Find(ready.Status, plan.Original.TargetId), plan.Original, plan.Original.Dashboard!))
                    return Failed($"Output '{plan.Original.TargetName}' changed while choosing its dashboard. Retry the action.", completed);

            foreach (var plan in plans)
            {
                var before = await actions.GetStatus();
                if (!before.Success || before.Status is null ||
                    !Matches(Find(before.Status, plan.Original.TargetId), plan.Original, plan.Original.Dashboard!))
                    return Failed($"Output '{plan.Original.TargetName}' changed before dispatch. Refresh status and retry.", completed);
                var assignedActive = false;
                if (plan.Assign)
                {
                    var assignment = await actions.AssignDashboard(plan.Original.TargetId, plan.Dashboard);
                    if (!assignment.Success)
                        return Failed($"Assignment for '{plan.Original.TargetName}' was not confirmed: {assignment.Message}", completed);
                    // Assignment already reloads an active target; do not reload it twice.
                    var afterAssign = await actions.GetStatus();
                    var output = afterAssign.Status is null ? null : Find(afterAssign.Status, plan.Original.TargetId);
                    if (!afterAssign.Success || !Matches(output, plan.Original, plan.Dashboard))
                        return Failed($"Assignment returned success for '{plan.Original.TargetName}', but its status could not be confirmed.", completed);
                    completed.Add($"Assigned '{plan.Dashboard}' to '{plan.Original.TargetName}'.");
                    assignedActive = output!.IsActive;
                }
                if (targetId is null) continue;
                if (!assignedActive)
                {
                    var start = await actions.StartOrReload(plan.Original.TargetId);
                    if (!start.Success)
                        return Failed($"Reload for '{plan.Original.TargetName}' was not confirmed: {start.Message}", completed);
                }
                var observed = await actions.GetStatus();
                var actual = observed.Status is null ? null : Find(observed.Status, plan.Original.TargetId);
                if (!observed.Success || actual is null || !Matches(actual, plan.Original, plan.Dashboard) || !actual.IsActive)
                    return Failed($"Runtime did not confirm an active '{plan.Dashboard}' on '{plan.Original.TargetName}'. Refresh status before retrying.", completed);
                if (actual.UsbState.Equals("ERROR", StringComparison.OrdinalIgnoreCase) ||
                    actual.UsbState.Equals("AMBIGUOUS", StringComparison.OrdinalIgnoreCase))
                    return Failed($"Output '{actual.TargetName}' reports {actual.UsbState} after the request.", completed);
                completed.Add($"Output '{actual.TargetName}' loaded '{actual.Dashboard}'; state: {actual.UsbState}.");
            }
            if (targetId is null)
            {
                var beforeAll = await actions.GetStatus();
                if (!beforeAll.Success || beforeAll.Status is null ||
                    !MatchesAll(beforeAll.Status, plans))
                    return Failed("Output enablement/assignments changed before Reload All. Refresh status and retry.", completed);
                var reload = await actions.ReloadAll();
                if (!reload.Success)
                    return Failed("Reload All was not confirmed: " + reload.Message, completed);
                var afterAll = await actions.GetStatus();
                if (!afterAll.Success || afterAll.Status is null)
                    return Failed("Reload All returned success, but Runtime status is unavailable. Applied changes are unknown.", completed);
                completed.Add($"Runtime reports {afterAll.Status.Outputs.Count(o => o.IsActive)} active of {afterAll.Status.Outputs.Length} configured Outputs after Reload All.");
                if (!MatchesAll(afterAll.Status, plans) ||
                    afterAll.Status.Outputs.Any(o => o.OutputEnabled == false && o.IsActive))
                    return Failed("Output enablement/assignments changed during Reload All; changes may be partially applied. Refresh status before retrying.", completed);
                foreach (var plan in plans)
                {
                    var actual = Find(afterAll.Status, plan.Original.TargetId)!;
                    if (!actual.IsActive || actual.UsbState.Equals("ERROR", StringComparison.OrdinalIgnoreCase) ||
                        actual.UsbState.Equals("AMBIGUOUS", StringComparison.OrdinalIgnoreCase))
                        return Failed($"Output '{actual.TargetName}' reports {actual.UsbState} after Reload All; an active error-free session is not confirmed.", completed);
                    completed.Add($"Output '{actual.TargetName}' loaded '{actual.Dashboard}'; state: {actual.UsbState}.");
                }
                completed.Add(plans.Count == 0
                    ? "Common settings reloaded; no enabled Output was reloaded."
                    : "Common settings and enabled Output dashboards reloaded.");
            }
            return new RuntimeIpcResponse(true, string.Join(" ", completed));
        }
        catch (Exception ex)
        {
            return Failed(ex.Message, completed);
        }
    }

    private sealed record StartPlan(RuntimeOutputStatus Original, string Dashboard, bool Assign);
    private static bool HasEnabledSet(RuntimeStatusSnapshot status, List<StartPlan> plans) =>
        status.Outputs.All(o => o.OutputEnabled is not null) &&
        status.Outputs.Select(o => o.TargetId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == status.Outputs.Length &&
        status.Outputs.Where(o => o.OutputEnabled == true).Select(o => o.TargetId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(plans.Select(p => p.Original.TargetId));
    private static bool MatchesAll(RuntimeStatusSnapshot status, List<StartPlan> plans) =>
        HasEnabledSet(status, plans) &&
        plans.All(p => Matches(Find(status, p.Original.TargetId), p.Original, p.Dashboard));
    private static RuntimeOutputStatus? Find(RuntimeStatusSnapshot status, string id)
    {
        var matches = status.Outputs.Where(o => o.TargetId.Equals(id, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
    private static bool Matches(RuntimeOutputStatus? current, RuntimeOutputStatus original, string dashboard) =>
        current is not null && current.OutputEnabled == true &&
        string.Equals(current.Kind, original.Kind, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(current.Dashboard, dashboard, StringComparison.OrdinalIgnoreCase);
    private static RuntimeIpcResponse Failed(string message, List<string> completed) => new(false,
        completed.Count == 0 ? message : message + " Completed actions: " + string.Join(" ", completed));
}
