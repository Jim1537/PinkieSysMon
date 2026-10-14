using System.Windows.Forms;
using PinkieSysMon.Launcher;

namespace PinkieSysMon.RegressionTests;

internal static class LauncherPackagingContractTests
{
    public static void SharedWindowAndShortcuts()
    {
        var expected = new[]
        {
            ("Pinkie's System Monitor", ""),
            ("PinkieSysMon Control Center", "--editor"),
            ("PinkieSysMon Runtime", "--runtime")
        };
        if (!ProjectPresentation.Shortcuts.SequenceEqual(expected))
            throw new InvalidOperationException("Launcher shortcut contract changed.");

        if (ProjectPresentation.Website != "https://jim1537.github.io/PinkieSysMon")
            throw new InvalidOperationException("Product website contract changed.");

        // Construct both modes from the same compiled source; do not show or
        // launch external processes or create Windows desktop shortcuts.
        using var launcher = new ProjectInfoWindow(Path.GetTempPath(), true, "test-version");
        using var about = new ProjectInfoWindow(Path.GetTempPath(), false, "test-version");

        var launcherButtons = FindControls<Button>(launcher).ToArray();
        var aboutButtons = FindControls<Button>(about).ToArray();
        foreach (var command in new[] { "Launch Control Center", "Launch Runtime", "Launch Both" })
        {
            if (launcherButtons.Count(button => button.Text == command) != 1)
                throw new InvalidOperationException("Launcher action is missing or duplicated: " + command);
            if (aboutButtons.Any(button => button.Text == command))
                throw new InvalidOperationException("About mode unexpectedly exposes a launch action.");
        }

        foreach (var form in new[] { launcher, about })
        {
            if (FindControls<LinkLabel>(form).Count(link => link.Text == ProjectPresentation.Website) != 1)
                throw new InvalidOperationException("Product website is missing or duplicated.");
            if (FindControls<Label>(form).Count(label => label.Text == ProjectPresentation.Description) != 1)
                throw new InvalidOperationException("The shared product description is missing.");
            if (FindControls<Button>(form).Count(button => button.Text == "Advanced ▼") != 1)
                throw new InvalidOperationException("Advanced is missing or expanded by default.");
            if (FindControls<Button>(form).Count(button => button.Text == "Create Desktop shortcuts") != 1 ||
                FindControls<Button>(form).Count(button => button.Text == "Create Start Menu group") != 1)
                throw new InvalidOperationException("Both modes must expose identical Advanced actions.");
        }
    }

    private static IEnumerable<T> FindControls<T>(Control parent) where T : Control
    {
        foreach (Control child in parent.Controls)
        {
            if (child is T matching)
                yield return matching;
            foreach (var descendant in FindControls<T>(child))
                yield return descendant;
        }
    }
}
