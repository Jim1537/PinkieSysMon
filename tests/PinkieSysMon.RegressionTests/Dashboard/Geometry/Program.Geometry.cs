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
    private static void DashboardUniversalSizeSchema()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-size-schema-regression");
        try
        {
            var dashboardPath = Path.Combine(tempRoot, "dashboards", "size-schema.json");

            var missingCanvasHeightRejected = false;
            try
            {
                _ = DashboardDefinition.Parse(
                    $$"""
                    {
                      "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                      "canvas": { "width": 1920, "orientation": 0, "backgroundColor": "#000000" },
                      "widgets": []
                    }
                    """,
                    dashboardPath);
            }
            catch (InvalidDataException)
            {
                missingCanvasHeightRejected = true;
            }
            Assert(missingCanvasHeightRejected,
                "Current dashboard schema must require explicit Canvas Width and Height.");

            var missingHeightRejected = false;
            try
            {
                _ = DashboardDefinition.Parse(
                    $$"""
                    {
                      "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                      "canvas": { "width": 1920, "height": 480, "orientation": 0, "backgroundColor": "#000000" },
                      "widgets": [
                        { "type": "bar", "id": "bar-missing-height", "width": 20, "metric": "system.output.desktop-1.fps", "min": 0, "max": 100, "color": "#FFFFFFFF" }
                      ]
                    }
                    """,
                    dashboardPath);
            }
            catch (InvalidDataException)
            {
                missingHeightRejected = true;
            }
            Assert(missingHeightRejected,
                "Current dashboard schema must require explicit Width and Height for every widget.");

            var obsoleteDiameterRejected = false;
            try
            {
                _ = DashboardDefinition.Parse(
                    $$"""
                    {
                      "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                      "canvas": { "width": 1920, "height": 480, "orientation": 0, "backgroundColor": "#000000" },
                      "widgets": [
                        { "type": "bar", "id": "bar-obsolete-diameter", "width": 20, "height": 20, "diameter": 20, "metric": "system.output.desktop-1.fps", "min": 0, "max": 100, "color": "#FFFFFFFF" }
                      ]
                    }
                    """,
                    dashboardPath);
            }
            catch (InvalidDataException)
            {
                obsoleteDiameterRejected = true;
            }
            Assert(obsoleteDiameterRejected,
                "Current dashboard schema must reject obsolete Diameter geometry.");

            var gauge = new WidgetDefinition
            {
                Type = WidgetTypeContract.Gauge,
                Id = "gauge-mismatched-size",
                Metric = FixtureOutputFps,
                Width = 100f,
                Height = 99f,
                Min = 0,
                Max = 100
            };
            var mismatchedGaugeRejected = false;
            try
            {
                gauge.Validate(tempRoot);
            }
            catch (InvalidDataException)
            {
                mismatchedGaugeRejected = true;
            }
            Assert(mismatchedGaugeRejected,
                "Gauge Height must be constrained to Width by the universal size contract.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

}
