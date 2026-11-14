using System.Text;
using PinkieSysMon;
using PinkieSysMon.Launcher;

namespace PinkieSysMon.RegressionTests;

internal static class Issue128SettingsDraftTests
{
    public static void PreservesUnrelatedApplicationFields()
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkie-issue128-app-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "config"));
        try
        {
            var app = Path.Combine(root, "config", "app.json");
            var original = "\uFEFF{\n\"Runtime\":{\"IdleTimeoutMinutes\":0},\n\"Renderer\":{\"RasterSharpness\":0}," +
                "\"MetricProviders\":{\"system\":true,\"unused-extension\":false}," +
                "\"Filesystem\":{\"Sources\":[]},\"Outputs\":{\"Targets\":[{\"Id\":\"opaque\",\"CustomValue\":7}]}," +
                "\"UnknownRoot\":{\"Kept\":\"yes\"}}\n";
            File.WriteAllText(app, original);
            var draft = LauncherSettingsDraft.Open(root, _ => false);
            if (draft.HasChanges || !draft.PrepareAppBytes().AsSpan().SequenceEqual(draft.OriginalAppBytes))
                throw new InvalidOperationException("Opening Settings changed app.json without an edit.");
            draft.Values.LogRetentionDays = 7;
            draft.Values.RasterSharpness = 45;
            draft.Values.IcueProviderEnabled = true;
            if (!draft.AppChanged || draft.EditorChanged || draft.StartupChanged)
                throw new InvalidOperationException("Settings draft change scope is incorrect.");
            var prepared = Encoding.UTF8.GetString(draft.PrepareAppBytes());
            using var json = System.Text.Json.JsonDocument.Parse(prepared);
            if (json.RootElement.GetProperty("Outputs").GetProperty("Targets")[0].GetProperty("CustomValue").GetInt32() != 7 ||
                json.RootElement.GetProperty("UnknownRoot").GetProperty("Kept").GetString() != "yes" ||
                json.RootElement.GetProperty("MetricProviders").GetProperty("unused-extension").GetBoolean() ||
                json.RootElement.GetProperty("Runtime").GetProperty("LogRetentionDays").GetInt32() != 7)
                throw new InvalidOperationException("Selective Settings patch modified an unrelated application field.");
            draft.WriteAppIfUnchanged(draft.PrepareAppBytes());
            var saved = File.ReadAllBytes(app);
            if (!LauncherSettingsDraft.RestoreIfUnchanged(app, saved, draft.OriginalAppBytes) ||
                !File.ReadAllBytes(app).AsSpan().SequenceEqual(draft.OriginalAppBytes))
                throw new InvalidOperationException("Settings app rollback failed to restore previous bytes.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    public static void RejectsConcurrentOrMalformedSourceChanges()
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkie-issue128-conflict-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "config"));
        try
        {
            var path = Path.Combine(root, "config", "app.json");
            File.WriteAllText(path, "{\"Outputs\":{\"Targets\":[]},\"Filesystem\":{\"Sources\":[]}}");
            var draft = LauncherSettingsDraft.Open(root, _ => false);
            draft.Values.LogRetentionDays = 9;
            File.AppendAllText(path, " ");
            var current = File.ReadAllBytes(path);
            try
            {
                draft.WriteAppIfUnchanged(draft.PrepareAppBytes());
                throw new InvalidOperationException("Stale application configuration was overwritten.");
            }
            catch (InvalidOperationException error) when (error.Message.StartsWith("Settings file changed", StringComparison.Ordinal))
            {
                if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(current))
                    throw new InvalidOperationException("Stale Settings write modified a newer file.");
            }
            File.WriteAllText(path, "{broken");
            try
            {
                LauncherSettingsDraft.Open(root, _ => false);
                throw new InvalidOperationException("Malformed application JSON admitted for Settings.");
            }
            catch (InvalidDataException) { }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
