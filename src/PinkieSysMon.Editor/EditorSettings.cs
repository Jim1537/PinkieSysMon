using System.Text.Json;

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

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, true);
    }
}

internal sealed class DashboardViewSettings
{
    public string Zoom { get; set; } = "50%";
    public int ScrollX { get; set; }
    public int ScrollY { get; set; }
}
