using System.Text.Json;
using PinkieSysMon.Editor;

namespace PinkieSysMon.RegressionTests;

internal static class Issue70ClipboardTests
{
    public static void PackageRoundTrip()
    {
        var package = new WidgetTransferPackage
        {
            Version = 1,
            SourceDirectory = @"C:\\boards\\source",
            SnapshotJson = "{\"schemaVersion\":1}",
            Groups = [new WidgetTransferGroup { Id = "group", Name = "Indicators" }],
            RootGroupIds = ["group"],
            WidgetGroupIds = new Dictionary<string, string> { ["widget"] = "group" }
        };
        var restored = EditorWidgetClipboard.Parse(EditorWidgetClipboard.Serialize(package));
        if (restored.Version != 1 || restored.Groups.Count != 1 ||
            restored.RootGroupIds.Single() != "group" ||
            restored.WidgetGroupIds["widget"] != "group")
            throw new InvalidOperationException("Widget clipboard package failed its serialization round trip.");

        var json = JsonSerializer.Serialize(package);
        var unsupported = JsonSerializer.Deserialize<WidgetTransferPackage>(json)!;
        unsupported.Version = 2;
        try
        {
            _ = EditorWidgetClipboard.Parse(EditorWidgetClipboard.Serialize(unsupported));
            throw new InvalidOperationException("Unsupported widget clipboard versions must be rejected.");
        }
        catch (InvalidDataException)
        {
        }
    }
}
