using System.Windows.Forms;
using PinkieSysMon;
using PinkieSysMon.Editor;

namespace PinkieSysMon.RegressionTests;

internal static partial class Program
{
    private static void Issue120AddWidgetMenuContract()
    {
        var expected = new (string? Type, string Label)[]
        {
            (WidgetTypeContract.Binary, "Binary Indicator"),
            (WidgetTypeContract.Calculator, "Calculator"),
            (WidgetTypeContract.Gauge, "Gauge"),
            (WidgetTypeContract.Graph, "History Graph"),
            (WidgetTypeContract.Image, "Image / Icon"),
            (WidgetTypeContract.Bar, "Progress Bar"),
            (WidgetTypeContract.Value, "Text / Value"),
            (null, string.Empty),
            (WidgetTypeContract.MediaSystem, "Audio Devices"),
            (WidgetTypeContract.MediaPlayer, "Playback Status"),
            (WidgetTypeContract.Power, "Power Status")
        };

        using var toolbar = new ToolStripDropDownButton("Add widget");
        using var editMenu = new ToolStripMenuItem("Add Widget");
        var toolbarDispatch = new List<string>();
        var editDispatch = new List<string>();

        EditorAddWidgetMenuContract.Populate(toolbar.DropDownItems, toolbarDispatch.Add);
        EditorAddWidgetMenuContract.Populate(editMenu.DropDownItems, editDispatch.Add);

        CheckMenu(toolbar.DropDownItems, "Toolbar");
        CheckMenu(editMenu.DropDownItems, "Edit");

        var expectedTypes = expected.Where(entry => entry.Type is not null)
                                    .Select(entry => entry.Type!)
                                    .ToArray();
        Assert(expectedTypes.Distinct(StringComparer.Ordinal).Count() == expectedTypes.Length &&
               expectedTypes.OrderBy(type => type, StringComparer.Ordinal)
                            .SequenceEqual(WidgetTypeContract.KnownTypes.OrderBy(type => type, StringComparer.Ordinal)),
            "Every existing canonical widget must be explicitly classified and added exactly once.");

        foreach (var item in toolbar.DropDownItems.OfType<ToolStripMenuItem>())
            item.PerformClick();
        foreach (var item in editMenu.DropDownItems.OfType<ToolStripMenuItem>())
            item.PerformClick();

        Assert(toolbarDispatch.SequenceEqual(expectedTypes) && editDispatch.SequenceEqual(expectedTypes),
            "Both Add Widget entry points must dispatch unchanged canonical type IDs in display order.");

        void CheckMenu(ToolStripItemCollection items, string location)
        {
            Assert(items.Count == expected.Length,
                $"{location} Add Widget must contain ten widgets and exactly one separator.");
            for (var index = 0; index < expected.Length; index++)
            {
                var (type, label) = expected[index];
                if (type is null)
                {
                    Assert(items[index] is ToolStripSeparator { CanSelect: false },
                        $"{location} Add Widget must have one nonselectable native separator after the basic group.");
                    continue;
                }

                Assert(items[index] is ToolStripMenuItem item &&
                       item.Text == label &&
                       string.Equals(item.Tag as string, type, StringComparison.Ordinal),
                    $"{location} Add Widget entry {index} must expose the approved label and canonical type ID.");
            }
        }
    }
}
