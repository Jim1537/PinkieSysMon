using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.Editor;

internal static class ShortcutTargetEditing
{
    public static void SetTarget(C.ShortcutWidgetDefinition shortcut, string target)
    {
        if (string.Equals(shortcut.Target, target, StringComparison.Ordinal))
            return;

        shortcut.Target = target;
        if (!string.IsNullOrEmpty(shortcut.Label))
            return;

        // This is an Editor edit, not a model/load/render fallback. Keep the
        // Shell payload literal and do not resolve, import or launch the target.
        try
        {
            shortcut.Label = Path.GetFileNameWithoutExtension(target);
        }
        catch (ArgumentException)
        {
            // A Shell parsing name need not be a filesystem path.
        }
    }
}
