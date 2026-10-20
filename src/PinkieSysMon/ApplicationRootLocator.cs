namespace PinkieSysMon;

internal static class ApplicationRootLocator
{
    public static string Find(string startDirectory)
    {
        DirectoryInfo? directory = new DirectoryInfo(Path.GetFullPath(startDirectory));

        while (directory is not null)
        {
            var configPath = Path.Combine(directory.FullName, "config", "app.json");
            // A clean checkout or fresh installation need not have a saved
            // dashboard folder yet. The persistent app config identifies its root.
            if (File.Exists(configPath))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"PinkieSysMon application root could not be located from '{startDirectory}'. " +
            "Expected to find config\\app.json in an ancestor application root.");
    }
}
