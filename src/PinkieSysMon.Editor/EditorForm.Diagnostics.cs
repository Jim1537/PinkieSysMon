using System.Text.Json;
using PinkieSysMon;

namespace PinkieSysMon.Editor;

internal sealed partial class EditorForm
{
    private readonly Action<string, string, MessageBoxIcon>? _documentMessagePresenter;

    // Presentation can be supplied by the host; persistence/logging remains real.
    private void ShowDocumentMessage(string message, string title, MessageBoxIcon icon)
    {
        if (_documentMessagePresenter is { } present) present(message, title, icon);
        else MessageBox.Show(message, title, MessageBoxButtons.OK, icon);
    }

    private void LogEditCommitted(string action, IEnumerable<string?>? widgetIds,
        IEnumerable<string?>? groupIds, IEnumerable<string?>? properties, bool canvas,
        string? parentOperationId)
    {
        try
        {
            _log.Info(JsonSerializer.Serialize(new
            {
                eventName = "editor.edit.committed", operationId = Guid.NewGuid().ToString("N"),
                parentOperationId, subject = _dashboardName, action, scope = "memory-and-history",
                widgetCount = _definition?.Widgets.Count ?? 0, canvas,
                widgets = BoundEditIdentifiers(widgetIds), groups = BoundEditIdentifiers(groupIds),
                properties = BoundEditIdentifiers(properties)
            }));
        }
        catch (Exception error)
        {
            // Evidence failure must not turn an accepted history checkpoint into
            // an edit failure or trigger the caller's document rollback.
            _log.Error("Editor edit checkpoint evidence unavailable.", error);
        }
    }

    private static object BoundEditIdentifiers(IEnumerable<string?>? values)
    {
        const int maximumEntries = 64;
        const int maximumCharacters = 128;
        var sample = new List<string>(maximumEntries);
        var count = 0;
        var missing = 0;
        var truncated = false;
        foreach (var value in values ?? [])
        {
            count++;
            if (string.IsNullOrEmpty(value)) { missing++; continue; }
            if (sample.Count == maximumEntries) continue;
            if (value.Length > maximumCharacters)
            {
                sample.Add(value[..(maximumCharacters - 14)] + "...[truncated]");
                truncated = true;
            }
            else sample.Add(value);
        }
        return new { count, sample, missing, omitted = count - missing - sample.Count, truncated };
    }
}
