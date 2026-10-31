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
    private static void OutputDeviceDiscoveryIdentity()
    {
        const string pathA = @"\\?\usb#vid_0416&pid_5408#SERIAL_A#{dee824ef-729b-4a0e-9c14-b7117d33a817}";
        const string pathB = @"\\?\usb#vid_0416&pid_5408#SERIAL_B#{dee824ef-729b-4a0e-9c14-b7117d33a817}";
        const string unrelated = @"\\?\usb#vid_1234&pid_5678#OTHER#{dee824ef-729b-4a0e-9c14-b7117d33a817}";

        var devices = DeviceDiscovery.CreateTrofeoDescriptors([pathA, pathA.ToUpperInvariant(), pathB, unrelated]);
        Assert(devices.Count == 2, "Trofeo discovery must enumerate every unique compatible interface instead of returning the first match.");
        Assert(devices.All(device => device.Transport == OutputTargetContract.LyBulkTransport),
            "Discovered Trofeo devices must expose the Trofeo transport identity.");
        Assert(devices.Select(device => device.DeviceId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2,
            "Each physical interface must receive a distinct stable DeviceId.");
        Assert(DeviceDiscovery.CreateStableDeviceId(pathA) == DeviceDiscovery.CreateStableDeviceId(pathA.ToUpperInvariant()),
            "Trofeo DeviceId generation must be insensitive to Windows device-path casing.");
        Assert(devices.All(device => !device.SelectionLabel.Contains("vid_0416", StringComparison.OrdinalIgnoreCase) &&
                                     !device.SelectionLabel.Contains("pid_5408", StringComparison.OrdinalIgnoreCase)),
            "User-facing output-device labels must not expose raw VID/PID transport identity.");
    }

}
