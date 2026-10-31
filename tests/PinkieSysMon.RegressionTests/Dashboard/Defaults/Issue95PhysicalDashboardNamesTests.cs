using PinkieSysMon;

namespace PinkieSysMon.RegressionTests;

internal static class Issue95PhysicalDashboardNamesTests
{
    public static void UserGeometryNaming()
    {
        var examples = new (int Width, int Height, string Name)[]
        {
            (1920, 480, "device_1920x480_default"),
            (1920, 720, "device_1920x720_default"),
            (640, 640, "device_640x640_default"),
            (5000, 10000, "device_5000x10000_default"),
            (10000, 5000, "device_10000x5000_default")
        };
        foreach (var example in examples)
        {
            var actual = OutputDefaultDashboardPolicy.NameForPhysicalGeometry(
                example.Width, example.Height);
            Check(actual == example.Name,
                "A physical default name must depend only on user-entered screen dimensions.");
            Check(WindowsPathComponentValidator.IsSafe(actual),
                "A generated physical default name must be a safe dashboard folder name.");
        }

        Check(OutputDefaultDashboardPolicy.NameForPhysicalGeometry(1920, 480) ==
              "device_1920x480_default" &&
              OutputDefaultDashboardPolicy.NameForPhysicalGeometry(1920, 720) ==
              "device_1920x720_default",
            "Existing physical board names must remain stable during generic onboarding.");
        foreach (var dimensions in new[] { (0, 480), (-1, 480), (1920, 0), (1920, -1) })
        {
            try
            {
                _ = OutputDefaultDashboardPolicy.NameForPhysicalGeometry(
                    dimensions.Item1, dimensions.Item2);
            }
            catch (ArgumentOutOfRangeException) { continue; }
            throw new InvalidOperationException("Non-positive physical dimensions must be rejected.");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
