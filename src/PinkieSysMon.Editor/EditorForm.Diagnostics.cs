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

    private void LogEditCommitted() => _log.Info(JsonSerializer.Serialize(new
    {
        eventName = "editor.edit.committed", operationId = Guid.NewGuid().ToString("N"),
        subject = _dashboardName, widgetCount = _definition?.Widgets.Count ?? 0,
        selectedWidgetIds = _selectedWidgets.Select(w => w.Id).Take(64).ToArray(),
        selectionCount = _selectedWidgets.Count
    }));
}
