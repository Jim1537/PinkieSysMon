using PinkieSysMon.Launcher;

namespace PinkieSysMon.RegressionTests;

internal static class Issue128UnifiedLauncherTests
{
    public static void SixTabsAndFilesystemSourcesPlacement()
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkie-issue128-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "config"));
        try
        {
            File.WriteAllText(Path.Combine(root, "config", "app.json"),
                "{\"Outputs\":{\"Targets\":[]},\"Filesystem\":{\"Sources\":[]}}");
            var draft = LauncherSettingsDraft.Open(root, _ => false);
            using var form = new UnifiedLauncherWindow(root, "2.88.10", draftForTests: draft);
            var names = form.Tabs.TabPages.Cast<TabPage>().Select(tab => tab.Text).ToArray();
            if (!names.SequenceEqual(new[] { "Application", "Common", "Runtime", "Editor", "Providers", "About" }))
                throw new InvalidOperationException("Unified Launcher must contain exactly six canonical tabs.");
            var providers = form.Tabs.TabPages[4];
            var controls = FindControls(providers).ToArray();
            if (controls.OfType<Button>().Count(x => x.Text == "Sources...") != 1 ||
                controls.OfType<CheckBox>().Count(x => x.Text == "Filesystem") != 1)
                throw new InvalidOperationException("Filesystem Sources must be adjacent to its single provider switch.");
            form.SelectTab(LauncherTab.About);
            if (form.Tabs.SelectedIndex != 5)
                throw new InvalidOperationException("About tab activation did not reach the shared window.");
            form.SelectTab(LauncherTab.Common);
            if (form.Tabs.SelectedIndex != 1 || form.Draft != draft)
                throw new InvalidOperationException("Tab handoff must retain the same Settings draft and Form.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static IEnumerable<Control> FindControls(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var nested in FindControls(child)) yield return nested;
        }
    }
}
