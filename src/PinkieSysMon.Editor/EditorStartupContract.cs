namespace PinkieSysMon.Editor;

// Startup and theme-restart rules are independent of Runtime Output assignments.
internal static class EditorStartupContract
{
    public static string? ResolveInitialDashboard(
        string? requestedDashboard,
        Func<IReadOnlyList<string>> discoverDashboards)
    {
        // An ordinary launch must not even enumerate saved documents.
        if (string.IsNullOrWhiteSpace(requestedDashboard))
            return null;

        var dashboards = discoverDashboards();
        if (dashboards.Count == 0)
            throw new InvalidDataException("No dashboards were found.");

        // Preserve the established compatibility behavior for an explicit missing name.
        return dashboards.FirstOrDefault(name =>
            name.Equals(requestedDashboard, StringComparison.OrdinalIgnoreCase)) ?? dashboards[0];
    }

    public static string[] GetRestartArguments(string root, string? openDashboard) =>
        string.IsNullOrWhiteSpace(openDashboard)
            ? ["--root", root]
            : ["--root", root, "--dashboard", openDashboard];
}
