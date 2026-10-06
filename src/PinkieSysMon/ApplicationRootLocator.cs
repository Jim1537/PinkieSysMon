namespace PinkieSysMon;

internal static class ApplicationRootLocator
{
    public static string Find(string startDirectory)
    {
        DirectoryInfo? directory = new DirectoryInfo(Path.GetFullPath(startDirectory));

        while (directory is not null)
        {
            var configPath = Path.Combine(directory.FullName, "config", "app.json");
            var dashboardsPath = Path.Combine(directory.FullName, "dashboards");

            if (File.Exists(configPath) && Directory.Exists(dashboardsPath))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"PinkieSysMon application root could not be located from '{startDirectory}'. " +
            "Expected to find config\\app.json and dashboards\\ in the same root directory.");
    }
}
