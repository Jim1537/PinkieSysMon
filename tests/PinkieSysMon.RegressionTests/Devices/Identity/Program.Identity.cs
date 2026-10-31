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
    private static void OutputTargetSelection()
    {
        const string pathA = @"\\?\usb#vid_0416&pid_5408#SERIAL_A#{dee824ef-729b-4a0e-9c14-b7117d33a817}";
        const string pathB = @"\\?\usb#vid_0416&pid_5408#SERIAL_B#{dee824ef-729b-4a0e-9c14-b7117d33a817}";
        var devices = DeviceDiscovery.CreateTrofeoDescriptors([pathA, pathB]);

        var target = new OutputTargetConfig { Transport = OutputTargetContract.LyBulkTransport };
        var ambiguous = OutputDeviceSelection.Resolve(target, devices);
        Assert(ambiguous.Status == OutputDeviceResolutionStatus.Missing && ambiguous.Device is null,
            "Even multiple compatible devices must leave an unbound physical Output unresolved until exact selection.");

        var single = OutputDeviceSelection.Resolve(target, [devices[0]]);
        Assert(single.Status == OutputDeviceResolutionStatus.Missing && single.Device is null,
            "A single attached device must not authorize an unbound physical Output.");

        target.DeviceId = devices[1].DeviceId;
        var explicitMatch = OutputDeviceSelection.Resolve(target, devices);
        Assert(explicitMatch.Status == OutputDeviceResolutionStatus.Found && explicitMatch.Device?.DeviceId == devices[1].DeviceId,
            "An explicitly bound output target must resolve only its configured physical device.");

        target.DeviceId = "usb-interface:" + new string('0', 64);
        var missing = OutputDeviceSelection.Resolve(target, devices);
        Assert(missing.Status == OutputDeviceResolutionStatus.Missing && missing.Device is null,
            "A missing explicitly configured output device must not fall back to another compatible device.");
    }

}
