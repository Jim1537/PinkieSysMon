using PinkieSysMon.Launcher;

namespace PinkieSysMon.RegressionTests;

internal static class Issue128LauncherActivationTests
{
    public static void CommandRouting()
    {
        if (LauncherCommandLine.Parse([]).Tab != LauncherTab.Application)
            throw new InvalidOperationException("Default Launcher route must select Application.");
        foreach (var tab in Enum.GetValues<LauncherTab>())
        {
            var route = LauncherCommandLine.Parse([LauncherCommandLine.TabArgument(tab)]);
            if (route.Tab != tab || route.IsDirectAction)
                throw new InvalidOperationException("Incorrect Launcher tab route: " + tab);
        }
        foreach (var action in new[] { "--editor", "--runtime", "--both" })
            if (LauncherCommandLine.Parse([action]) is not { Tab: null, DirectAction: { } accepted } ||
                accepted != action)
                throw new InvalidOperationException("Existing one-shot Launcher command changed.");
        foreach (var invalid in new[] {
            new[] { "--tab" }, new[] { "--tab=" }, new[] { "--tab=unknown" },
            new[] { "--settings" }, new[] { "--editor", "--tab=common" },
            new[] { "--tab=3" }, new[] { "--tab=editor", "--runtime" }
        })
        {
            try
            {
                LauncherCommandLine.Parse(invalid);
                throw new InvalidOperationException("Invalid Launcher route accepted: " + string.Join(' ', invalid));
            }
            catch (ArgumentException) { }
        }
    }

    public static void OwnerContentionAndHandoff()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "pinkie-issue128-" + Guid.NewGuid().ToString("N"));
        var one = LauncherUiLease.NamesFor(root, "S-1-5-21-TEST");
        var equal = LauncherUiLease.NamesFor(root + Path.DirectorySeparatorChar, "S-1-5-21-TEST");
        var other = LauncherUiLease.NamesFor(root + "-other", "S-1-5-21-TEST");
        if (one != equal || one == other)
            throw new InvalidOperationException("Launcher identity must preserve same-root and separate-root ownership.");
        using var owner = LauncherUiLease.TryAcquire(one.MutexName, one.PipeName)
            ?? throw new InvalidOperationException("Fresh Launcher mutex was unexpectedly busy.");
        var second = Task.Run(() => {
            using var competing = LauncherUiLease.TryAcquire(one.MutexName, one.PipeName);
            return competing is null;
        });
        if (!second.GetAwaiter().GetResult())
            throw new InvalidOperationException("A competing thread entered the active Launcher lease.");
        var selected = LauncherTab.Application;
        owner.Listen(tab => selected = tab);
        LauncherUiLease.ActivateAsync(one.PipeName, LauncherTab.Providers, 3000).GetAwaiter().GetResult();
        if (selected != LauncherTab.Providers)
            throw new InvalidOperationException("Activation acknowledgement preceded tab selection.");
    }
}
