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

        if (ProjectPresentation.LauncherExe != "Launcher.exe")
            throw new InvalidOperationException("Portable root launcher filename contract changed.");

        if (ProjectPresentation.Website != "https://jim1537.github.io/PinkieSysMon")
            throw new InvalidOperationException("Product website contract changed.");

        const string expectedDescription =
            "Pinkie's System Monitor is a fully local Windows application for building telemetry dashboards and " +
            "displaying them on independent Windows Desktop panels or supported USB devices.";
        if (ProjectPresentation.Description != expectedDescription ||
            ProjectPresentation.SupportedDevicesText != "supported USB devices" ||
            ProjectPresentation.SupportedDevicesUrl != "https://jim1537.github.io/PinkieSysMon/supported-devices/")
            throw new InvalidOperationException("About copy or supported-devices destination changed.");

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
            var description = FindControls<LinkLabel>(form)
                .SingleOrDefault(link => link.Text == ProjectPresentation.Description)
                ?? throw new InvalidOperationException("The shared product description must be a link label.");
            var link = description.Links.Cast<LinkLabel.Link>().SingleOrDefault();
            if (link is null ||
                link.Start != ProjectPresentation.Description.IndexOf(ProjectPresentation.SupportedDevicesText, StringComparison.Ordinal) ||
                link.Length != ProjectPresentation.SupportedDevicesText.Length ||
                link.LinkData is not string destination ||
                destination != ProjectPresentation.SupportedDevicesUrl)
                throw new InvalidOperationException("The supported-devices hyperlink is missing or incorrect.");

            var website = FindControls<LinkLabel>(form).Single(label => label.Text == ProjectPresentation.Website);
            var advanced = FindControls<Button>(form).Single(button => button.Text == "Advanced ▼");
            if (description.Bottom > website.Top || website.Bottom > advanced.Top ||
                advanced.Bottom > form.ClientSize.Height)
                throw new InvalidOperationException("About content overlaps or exceeds the window.");

            if (ReferenceEquals(form, launcher))
            {
                foreach (var button in launcherButtons.Where(button => button.Text.StartsWith("Launch ", StringComparison.Ordinal)))
                {
                    if (website.Bottom > button.Top || button.Bottom > advanced.Top)
                        throw new InvalidOperationException("Launcher controls overlap adjacent content.");
                }
            }
            if (FindControls<Button>(form).Count(button => button.Text == "Advanced ▼") != 1)
                throw new InvalidOperationException("Advanced is missing or expanded by default.");
            if (FindControls<Button>(form).Count(button => button.Text == "Create Desktop shortcuts") != 1 ||
                FindControls<Button>(form).Count(button => button.Text == "Create Start Menu group") != 1)
                throw new InvalidOperationException("Both modes must expose identical Advanced actions.");
        }
    }

    public static void ClosesAfterSuccessfulLaunchOnly()
    {
        var expectedRoot = Path.GetFullPath(Path.GetTempPath());
        foreach (var command in new[] { "--editor", "--runtime", "--both" })
        {
            using var launcher = new ProjectInfoWindow(expectedRoot, true, "test-version");
            var calls = 0;
            launcher.LaunchAndClose(command, (root, action) =>
            {
                calls++;
                if (root != expectedRoot || action != command)
                    throw new InvalidOperationException("Launcher did not dispatch the requested action.");
            });
            if (calls != 1 || !launcher.IsDisposed)
                throw new InvalidOperationException("Launcher must close after a successful launch: " + command);
        }

        using var failedLauncher = new ProjectInfoWindow(expectedRoot, true, "test-version");
        try
        {
            failedLauncher.LaunchAndClose("--editor", (_, _) =>
                throw new InvalidOperationException("Simulated launch failure."));
            throw new InvalidOperationException("Launcher unexpectedly accepted a failed start.");
        }
        catch (InvalidOperationException ex) when (ex.Message == "Simulated launch failure.")
        {
            // A failed process start must keep the Launcher open for retry.
        }
        if (failedLauncher.IsDisposed)
            throw new InvalidOperationException("Launcher closed after a failed start.");
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
