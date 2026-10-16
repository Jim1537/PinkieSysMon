using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace PinkieSysMon.Editor;

internal sealed class EditorSettings
{
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public int? WindowWidth { get; set; }
    public int? WindowHeight { get; set; }
    public bool Maximized { get; set; }
    public int? LeftPanelWidth { get; set; }
    public int? RightPanelWidth { get; set; }
    public bool GridEnabled { get; set; }
    public int GridStep { get; set; } = 25;
    public int ToolbarIconSize { get; set; } = 32;
    public string Theme { get; set; } = EditorThemeContract.SystemSetting;
    public Dictionary<string, DashboardViewSettings> Dashboards { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static EditorSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new EditorSettings();

            return JsonSerializer.Deserialize<EditorSettings>(File.ReadAllText(path), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            }) ?? new EditorSettings();
        }
        catch
        {
            return new EditorSettings();
        }
    }

    public DashboardViewSettings GetDashboard(string name)
    {
        if (!Dashboards.TryGetValue(name, out var state))
        {
            state = new DashboardViewSettings();
            Dashboards[name] = state;
        }
        return state;
    }

    public void Save(string path, string? activeDashboard = null)
    {
        var fullPath = Path.GetFullPath(path);
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullPath.ToUpperInvariant())));
        using var mutex = new Mutex(false, @"Local\PinkieSysMon.EditorSettings." + identity);
        var acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(TimeSpan.FromSeconds(15));
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
                throw new IOException("Timed out waiting for another Editor to save shared settings.");

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            var latest = Load(fullPath);
            if (activeDashboard is null)
            {
                foreach (var pair in Dashboards)
                    latest.Dashboards[pair.Key] = pair.Value;
            }
            else if (Dashboards.TryGetValue(activeDashboard, out var view))
            {
                latest.Dashboards[activeDashboard] = view;
            }

            // Shared preferences follow the last Editor to save; independent
            // dashboard views are merged under the cross-process file mutex.
            Dashboards = latest.Dashboards;
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
            var temp = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temp, json);
                File.Move(temp, fullPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
        }
        finally
        {
            if (acquired)
                mutex.ReleaseMutex();
        }
    }
}

internal sealed class DashboardViewSettings
{
    public string Zoom { get; set; } = "50%";
    public int ScrollX { get; set; }
    public int ScrollY { get; set; }
}
