using System.ComponentModel;
using System.Drawing.Design;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using PinkieSysMon;
using PinkieSysMon.Editor;
using C = PinkieSysMon.DashboardModel;

namespace PinkieSysMon.RegressionTests;

internal static partial class Program
{
    private static void GraphCanonicalTypeAndObsoleteFields()
    {
        Assert(WidgetTypeContract.KnownTypes.Contains("graph", StringComparer.OrdinalIgnoreCase),
            "Schema-23 Graph must be registered in the runtime type contract.");
        Assert(WidgetTypeContract.EditorAddableTypes.Contains("graph", StringComparer.OrdinalIgnoreCase),
            "Schema-23 Graph must be addable in the Editor.");

        var widgetProperties = TypeDescriptor.GetProperties(typeof(WidgetDefinition));
        Assert(widgetProperties["History"] is null &&
               widgetProperties["SampleIntervalMs"] is null &&
               widgetProperties["StrokeWidth"] is null,
            "Graph-only WidgetDefinition properties must be removed from the model.");
    }

}
