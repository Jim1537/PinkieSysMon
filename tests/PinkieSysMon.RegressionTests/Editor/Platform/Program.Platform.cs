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
    private static void EditorPropertyTabIconPresentationContract()
    {
        using var tabs = new EditorThemedTabControl();
        using var generalIcon = new Bitmap(32, 32);
        using var appearanceIcon = new Bitmap(32, 32);
        using var general = new TabPage
        {
            Name = "General",
            ToolTipText = "General",
            AccessibleName = "General"
        };
        using var appearance = new TabPage
        {
            Name = "Appearance",
            ToolTipText = "Appearance",
            AccessibleName = "Appearance"
        };
        tabs.TabPages.Add(general);
        tabs.TabPages.Add(appearance);

        tabs.SetIconHeaders(32,
        [
            (general, generalIcon),
            (appearance, appearanceIcon)
        ]);

        Assert(tabs.DrawMode == TabDrawMode.Normal && tabs.SizeMode == TabSizeMode.Fixed,
            "Property tabs must retain the native themed renderer while using fixed icon-only headers.");
        Assert(tabs.ShowToolTips,
            "Icon-only property tabs must expose their text names through native tab tooltips.");
        Assert(tabs.HeaderIconSize == 32 && tabs.ItemSize == new Size(42, 40),
            "Property tab header geometry must follow the shared 32 px interface-icon setting.");
        Assert(tabs.ImageList is not null && tabs.ImageList.ImageSize == new Size(32, 32) &&
               tabs.ImageList.ColorDepth == ColorDepth.Depth32Bit,
            "Property tabs must use a 32-bit native ImageList sized to the shared interface-icon setting.");
        Assert(general.Text.Length == 0 && general.ToolTipText == "General" && general.AccessibleName == "General" &&
               appearance.Text.Length == 0 && appearance.ToolTipText == "Appearance" && appearance.AccessibleName == "Appearance",
            "Property tab headers must remain icon-only while preserving names for tooltips/accessibility.");
        Assert(general.ImageIndex == 0 && appearance.ImageIndex == 1,
            "Every property tab must hold a stable native ImageList index.");

        // Dynamic property-tab sets remove/re-add TabPage instances. The numeric image index
        // must already be valid while a page is detached so its first native insertion is
        // laid out with the icon present, rather than relying on a second selection cycle.
        tabs.TabPages.Clear();
        Assert(general.ImageIndex == 0 && appearance.ImageIndex == 1,
            "Detached property tabs must retain their stable native image indices.");
        tabs.TabPages.Add(general);
        tabs.TabPages.Add(appearance);
        tabs.RefreshIconHeaders();
        Assert(general.ImageIndex == 0 && appearance.ImageIndex == 1 && tabs.ImageList is not null,
            "Property tab icons must survive the first dynamic TabPages reconfiguration.");

        tabs.SetIconHeaders(48,
        [
            (general, generalIcon),
            (appearance, appearanceIcon)
        ]);
        Assert(tabs.HeaderIconSize == 48 && tabs.ItemSize == new Size(58, 56) &&
               tabs.ImageList is not null && tabs.ImageList.ImageSize == new Size(48, 48),
            "Property tab headers must resize with the same 16/32/48 interface icon setting as the toolbar.");
    }

    private static void EditorDwmNonClientForwardingContract()
    {
        var editorAssembly = typeof(EditorForm).Assembly;
        Assert(editorAssembly.GetType("PinkieSysMon.Editor.EditorWindowChromeNative") is null,
            "PASS 9C must not own WM_NCCALCSIZE, WM_NCHITTEST, DwmDefWindowProc, or resize-border emulation.");

        var chromeSourceType = typeof(EditorForm);
        var fields = chromeSourceType
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .ToArray();
        Assert(fields.All(field => field.Name is not "_chromeResizeBorder" and not "_chromeTitleBarHeight" and not "_chromeDpi"),
            "Manual resize-border and caption-metric state must be removed; Windows/AppWindow owns these metrics.");
    }


    private static void WinFormsFatalExceptionPolicyContract()
    {
        Assert(WinFormsFatalExceptionPolicy.Mode == UnhandledExceptionMode.ThrowException,
            "Runtime and Editor must use the WinForms fatal unhandled-exception mode instead of catch-and-continue ThreadException behavior.");
    }

    private static void Pass8ShellModernizationAudit()
    {
        var editorFields = typeof(EditorForm)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        var menuFields = editorFields
            .Where(field => typeof(MenuStrip).IsAssignableFrom(field.FieldType))
            .ToArray();
        Assert(menuFields.Length == 1 &&
               menuFields[0].Name == "_mainMenuStrip" &&
               menuFields[0].FieldType == typeof(EditorTitleBarMenuStrip),
            "PASS 8 must leave exactly one main MenuStrip and it must be the title-bar-integrated EditorTitleBarMenuStrip.");

        Assert(editorFields.Any(field => field.Name == "_titleBarHost" && field.FieldType == typeof(EditorTitleBarHost)),
            "PASS 8/9C must retain only a client-content title-bar host; Windows App SDK owns frame, caption buttons, dragging, and resizing.");
        Assert(editorFields.Any(field => field.Name == "_appWindowTitleBar" && field.FieldType.FullName == "Microsoft.UI.Windowing.AppWindowTitleBar"),
            "PASS 9C must use Windows App SDK AppWindowTitleBar as the title-bar/frame integration authority.");
        Assert(editorFields.All(field => !field.Name.StartsWith("_groupMenu", StringComparison.Ordinal) &&
                                         !field.Name.StartsWith("_groupMove", StringComparison.Ordinal) &&
                                         !field.Name.StartsWith("_groupBring", StringComparison.Ordinal) &&
                                         !field.Name.StartsWith("_groupSend", StringComparison.Ordinal)),
            "PASS 8 must not retain obsolete dedicated Group-menu wiring after command consolidation into Edit/context surfaces.");

        var runtimeAssembly = typeof(RuntimeNotificationContract).Assembly;
        var runtimeMetadata = Encoding.UTF8.GetString(File.ReadAllBytes(runtimeAssembly.Location));
        Assert(!runtimeMetadata.Contains("ShowBalloonTip", StringComparison.Ordinal),
            "PASS 8 must not retain the legacy tray-balloon notification API anywhere in the runtime assembly.");
        Assert(typeof(IDisposable).IsAssignableFrom(typeof(WindowsAppNotificationService)),
            "The modern notification service must own an explicit disposable registration lifecycle.");

        var notificationField = typeof(PinkieApplicationContext)
            .GetField("_notifications", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert(notificationField?.FieldType == typeof(WindowsAppNotificationService),
            "PinkieApplicationContext must own the normalized WindowsAppNotificationService instead of ad-hoc notification calls.");

        using var menu = new MenuStrip();
        var fileMenu = new ToolStripMenuItem(EditorMainMenuContract.FileMenuText);
        var editMenu = new ToolStripMenuItem(EditorMainMenuContract.EditMenuText);
        var desktopMenu = new ToolStripMenuItem(EditorMainMenuContract.DesktopMenuText);
        var devicesMenu = new ToolStripMenuItem(EditorMainMenuContract.DevicesMenuText);
        var toolsMenu = new ToolStripMenuItem(EditorMainMenuContract.ToolsMenuText);
        var helpMenu = new ToolStripMenuItem(EditorMainMenuContract.HelpMenuText);
        EditorMainMenuContract.Populate(menu, fileMenu, editMenu, desktopMenu, devicesMenu, toolsMenu, helpMenu);

        Assert(menu.Items.Cast<ToolStripItem>().All(item => !string.Equals(item.Text, "Group", StringComparison.OrdinalIgnoreCase)),
            "PASS 8 final audit must keep the redundant top-level Group menu removed.");
        Assert(fileMenu.Alignment == ToolStripItemAlignment.Left &&
               editMenu.Alignment == ToolStripItemAlignment.Left &&
               desktopMenu.Alignment == ToolStripItemAlignment.Left &&
               devicesMenu.Alignment == ToolStripItemAlignment.Left &&
               toolsMenu.Alignment == ToolStripItemAlignment.Left &&
               helpMenu.Alignment == ToolStripItemAlignment.Right,
            "Final shell contract must keep File/Edit/Desktop/Devices/Tools left and Help right.");
    }

    private static void WindowsApplicationIdentityMetadata()
    {
        AssertApplicationIdentity(
            typeof(RuntimeVersion).Assembly,
            expectedTitle: "PinkieSysMon",
            expectedDescription: "PinkieSysMon workstation telemetry runtime");

        AssertApplicationIdentity(
            typeof(EditorMainMenuContract).Assembly,
            expectedTitle: "PinkieSysMon Dashboard Editor",
            expectedDescription: "PinkieSysMon dashboard editor");
    }

    private static void RuntimeModernAppNotificationContract()
    {
        Assert(RuntimeNotificationContract.GetTitle(RuntimeNotificationKind.Information) == "Information" &&
               RuntimeNotificationContract.GetTitle(RuntimeNotificationKind.Warning) == "Warning" &&
               RuntimeNotificationContract.GetTitle(RuntimeNotificationKind.Error) == "Error",
            "Native notification titles must express severity without repeating the Windows application identity.");

        var runtimeAssemblyPath = typeof(RuntimeNotificationContract).Assembly.Location;
        var metadata = Encoding.UTF8.GetString(File.ReadAllBytes(runtimeAssemblyPath));
        Assert(!metadata.Contains("ShowBalloonTip", StringComparison.Ordinal),
            "Runtime must not retain the legacy NotifyIcon.ShowBalloonTip notification path.");
        Assert(metadata.Contains("WindowsAppNotificationService", StringComparison.Ordinal) &&
               metadata.Contains("AppNotificationManager", StringComparison.Ordinal),
            "Runtime must contain the Windows App SDK app-notification service and manager contract.");
    }


    private static void RuntimeNotificationSelfContainedFallbackContract()
    {
        const string missingResourceMessage =
            "The specified module could not be found. Unable to load resource dll. Microsoft.WindowsAppRuntime.Insights.Resource.dll";

        var knownFailure = new System.Runtime.InteropServices.COMException(
            missingResourceMessage,
            unchecked((int)0x8007007E));
        Assert(WindowsAppNotificationService.IsKnownSelfContainedRegistrationFailure(knownFailure),
            "The known Windows App SDK self-contained Resource.dll registration failure must enable the display-only notification fallback.");

        var unrelatedMissingModule = new System.Runtime.InteropServices.COMException(
            "The specified module could not be found. Some.Other.Module.dll",
            unchecked((int)0x8007007E));
        Assert(!WindowsAppNotificationService.IsKnownSelfContainedRegistrationFailure(unrelatedMissingModule),
            "The display-only notification fallback must not swallow unrelated missing-module registration failures.");

        var unrelatedHResult = new System.Runtime.InteropServices.COMException(
            missingResourceMessage,
            unchecked((int)0x80004005));
        Assert(!WindowsAppNotificationService.IsKnownSelfContainedRegistrationFailure(unrelatedHResult),
            "The display-only notification fallback must remain specific to ERROR_MOD_NOT_FOUND (0x8007007E).");

        var fields = typeof(WindowsAppNotificationService)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert(fields.Any(field => field.Name == "_registeredForActivation" && field.FieldType == typeof(bool)) &&
               fields.Any(field => field.Name == "_canShow" && field.FieldType == typeof(bool)),
            "Notification delivery availability must remain separate from activation-registration state so the known Windows App SDK defect cannot disable display unnecessarily.");
    }

    private static void RejectedPropertyGridLabelBoldHackAbsent()
    {
        var editorAssemblyPath = typeof(EditorMainMenuContract).Assembly.Location;
        var metadata = Encoding.UTF8.GetString(File.ReadAllBytes(editorAssemblyPath));

        Assert(!metadata.Contains("TrySetFirstLevelPropertyLabelsBold", StringComparison.Ordinal) &&
               !metadata.Contains("TrySetGridItemLabelBold", StringComparison.Ordinal) &&
               !metadata.Contains("LabelBold", StringComparison.Ordinal),
            "The rejected private PropertyGrid LabelBold reflection path must not return to the Editor assembly.");
    }

    private static void EmptyEditorGroupIsValid()
    {
        var definition = new PinkieSysMon.DashboardModel.DashboardDefinition();
        var metadata = DashboardEditorMetadata.FromJson(
            """
            {
              "groupDefinitions": [
                { "id": "group-1", "name": "Empty", "parentGroupId": null }
              ],
              "widgetGroupIds": {}
            }
            """);

        var hierarchy = new EditorLayerHierarchy(definition, metadata);
        Assert(hierarchy.GetChildren(null).Count == 1, "Root empty group is missing.");
        Assert(hierarchy.GetChildren("group-1").Count == 0, "Empty group unexpectedly has children.");
        Assert(hierarchy.FlattenWidgets().Count == 0, "Empty group produced phantom widgets.");
        Assert(hierarchy.GetPreOrderNodes().Count == 1, "Empty group pre-order traversal is invalid.");
    }

    private static void NestedEmptyEditorGroupsAreValid()
    {
        var definition = new PinkieSysMon.DashboardModel.DashboardDefinition();
        var metadata = DashboardEditorMetadata.FromJson(
            """
            {
              "groupDefinitions": [
                { "id": "parent", "name": "Parent", "parentGroupId": null },
                { "id": "child", "name": "Child", "parentGroupId": "parent" }
              ],
              "widgetGroupIds": {}
            }
            """);

        var hierarchy = new EditorLayerHierarchy(definition, metadata);
        Assert(hierarchy.GetChildren(null).Count == 1, "Parent group is missing from root.");
        Assert(hierarchy.GetChildren("parent").Count == 1, "Child group is missing from parent.");
        Assert(hierarchy.GetChildren("child").Count == 0, "Nested empty group unexpectedly has children.");
        Assert(hierarchy.FlattenWidgets().Count == 0, "Nested empty groups produced phantom widgets.");
    }

    private static void LayerCommandHomogeneousSelection()
    {
        Assert(EditorLayerSelection.GetHomogeneousKind(Array.Empty<EditorLayerNodeRef>()) is null,
            "Empty selection must not resolve to a layer kind.");

        var widgets = new[]
        {
            EditorLayerNodeRef.Widget("widget-1"),
            EditorLayerNodeRef.Widget("widget-2")
        };
        Assert(EditorLayerSelection.GetHomogeneousKind(widgets) == EditorLayerNodeKind.Widget,
            "Homogeneous widget selection must resolve as widgets.");

        var groups = new[]
        {
            EditorLayerNodeRef.Group("group-1"),
            EditorLayerNodeRef.Group("group-2")
        };
        Assert(EditorLayerSelection.GetHomogeneousKind(groups) == EditorLayerNodeKind.Group,
            "Homogeneous group selection must resolve as groups.");

        var mixed = new[]
        {
            EditorLayerNodeRef.Widget("widget-1"),
            EditorLayerNodeRef.Group("group-1")
        };
        Assert(EditorLayerSelection.GetHomogeneousKind(mixed) is null,
            "Mixed widget/group selection must not resolve to a homogeneous command kind.");
    }

    private static void LhmDuplicateSensorIdDisambiguation()
    {
        const string rawSensorId = "/gpu-nvidia/0/load/3";
        const string baseMetricId = "lhm.gpu-nvidia.0.load.3";
        const string gpuBusMetricId = baseMetricId + "~gpu-bus";
        const string gpuMemoryMetricId = baseMetricId + "~gpu-memory";

        var tree = new LibreHardwareMonitorTreeSnapshot(
            new LibreHardwareMonitorJsonNode
            {
                Text = "PINKIEPIEPC",
                Children =
                [
                    new LibreHardwareMonitorJsonNode
                    {
                        Text = "NVIDIA GeForce RTX 5080",
                        HardwareId = "/gpu-nvidia/0",
                        Children =
                        [
                            new LibreHardwareMonitorJsonNode
                            {
                                Text = "Load",
                                Children =
                                [
                                    new LibreHardwareMonitorJsonNode
                                    {
                                        Text = "GPU Bus",
                                        SensorId = rawSensorId,
                                        SensorType = "Load",
                                        RawValue = JsonSerializer.SerializeToElement(52.0)
                                    },
                                    new LibreHardwareMonitorJsonNode
                                    {
                                        Text = "GPU Memory",
                                        SensorId = rawSensorId,
                                        SensorType = "Load",
                                        RawValue = JsonSerializer.SerializeToElement(7.9)
                                    }
                                ]
                            }
                        ]
                    }
                ]
            });

        var catalog = LibreHardwareMonitorMetricCatalog.Build(tree);
        Assert(catalog.AmbiguousMetricIds.Count == 0,
            "Distinctly named duplicate LHM sensors must be disambiguated instead of suppressed.");
        Assert(!catalog.Metrics.ContainsKey(baseMetricId),
            "The colliding raw LHM metric ID must not be published as an arbitrary winner.");
        Assert(catalog.Metrics.TryGetValue(gpuBusMetricId, out var gpuBus) &&
               gpuBus.ReadFromTreeSnapshot &&
               gpuBus.TreeValue == 52.0,
            "GPU Bus duplicate was not published as a stable tree-backed synthetic metric.");
        Assert(catalog.Metrics.TryGetValue(gpuMemoryMetricId, out var gpuMemory) &&
               gpuMemory.ReadFromTreeSnapshot &&
               gpuMemory.TreeValue == 7.9,
            "GPU Memory duplicate was not published as a stable tree-backed synthetic metric.");

        var unsafeTree = new LibreHardwareMonitorTreeSnapshot(
            new LibreHardwareMonitorJsonNode
            {
                Children =
                [
                    new LibreHardwareMonitorJsonNode
                    {
                        HardwareId = "/gpu-nvidia/0",
                        Text = "NVIDIA GeForce RTX 5080",
                        Children =
                        [
                            new LibreHardwareMonitorJsonNode
                            {
                                Text = "Load",
                                Children =
                                [
                                    new LibreHardwareMonitorJsonNode
                                    {
                                        Text = "Same Name",
                                        SensorId = rawSensorId,
                                        SensorType = "Load",
                                        RawValue = JsonSerializer.SerializeToElement(1.0)
                                    },
                                    new LibreHardwareMonitorJsonNode
                                    {
                                        Text = "Same Name",
                                        SensorId = rawSensorId,
                                        SensorType = "Load",
                                        RawValue = JsonSerializer.SerializeToElement(2.0)
                                    }
                                ]
                            }
                        ]
                    }
                ]
            });

        var unsafeCatalog = LibreHardwareMonitorMetricCatalog.Build(unsafeTree);
        Assert(unsafeCatalog.AmbiguousMetricIds.Contains(baseMetricId),
            "Indistinguishable duplicate LHM sensors must remain suppressed as ambiguous.");
        Assert(unsafeCatalog.Metrics.Count == 0,
            "Unsafe duplicate LHM sensors must not be published using tree order or node IDs.");

        var formattedNode = new LibreHardwareMonitorJsonNode
        {
            RawValue = JsonSerializer.SerializeToElement("7.9 %")
        };
        Assert(formattedNode.TryGetNumericValue(out var formattedValue) && Math.Abs(formattedValue - 7.9) < 0.0001,
            "LHM formatted RawValue fallback must preserve duplicate sensor readings.");
    }

    private static void LhmThroughputUnitSemantics()
    {
        var download = LibreHardwareMonitorMetricContract.CreateDescriptor(
            "lhm.network.0.throughput.8",
            "Throughput",
            "Download Speed");
        Assert(download.BaseUnit == MetricUnit.BytesPerSecond,
            "Ordinary LHM Throughput sensors must be interpreted as bytes per second.");

        var connection = LibreHardwareMonitorMetricContract.CreateDescriptor(
            "lhm.network.0.throughput.0",
            "Throughput",
            "Connection Speed");
        Assert(connection.BaseUnit == MetricUnit.BitsPerSecond,
            "LHM Connection Speed is a special Throughput sensor whose raw value is bits per second.");

        var culture = System.Globalization.CultureInfo.InvariantCulture;
        Assert(MetricValueFormatter.Format(125_000_000d, download, "Mbit/s", "0.0", culture) == "1000.0",
            "125,000,000 B/s must convert to 1000.0 Mbit/s.");
        Assert(MetricValueFormatter.Format(1_048_576d, download, "MiB/s", "0.00", culture) == "1.00",
            "1,048,576 B/s must convert to exactly 1.00 MiB/s.");
        Assert(MetricValueFormatter.Format(1_048_576d, download, "MB/s", "0.000000", culture) == "1.048576",
            "MiB/s and decimal MB/s must remain distinct.");
        Assert(MetricValueFormatter.Format(1_000_000_000d, connection, "Gbit/s", "0.00", culture) == "1.00",
            "A 1,000,000,000 bit/s connection speed must display as 1.00 Gbit/s.");
        Assert(MetricValueFormatter.Format(1_000_000_000d, connection, "MB/s", "0.00", culture) == "125.00",
            "A 1 Gbit/s connection speed must convert to 125.00 MB/s when byte units are requested.");

        var unitTokens = MetricContract.GetUnits(download).Select(option => option.Token).ToArray();
        Assert(unitTokens.Contains("Mbit/s", StringComparer.Ordinal),
            "Data-rate unit selector must expose Mbit/s.");
        Assert(unitTokens.Contains("MiB/s", StringComparer.Ordinal),
            "Data-rate unit selector must preserve binary byte-rate units.");
    }

    private static void QuantitativeWidgetUnitConversion()
    {
        const string metricId = "test-units.network.download";
        var descriptor = new MetricDescriptor(metricId, MetricValueKind.Number, MetricUnit.BytesPerSecond);
        MetricContract.ReplaceProviderDescriptors("test-units", [descriptor]);

        Assert(MetricValueConverter.TryConvertNumeric(125_000_000d, descriptor, "Mbit/s", out var converted) &&
               Math.Abs(converted - 1000d) < 0.000001,
            "Central numeric converter must convert 125,000,000 B/s to 1000 Mbit/s.");

        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-unit-conversion-regression");
        try
        {
            var widget = new WidgetDefinition
            {
                Type = WidgetTypeContract.Gauge,
                Id = "unit-gauge",
                Metric = metricId,
                Unit = "Mbit/s"
            };
            var currentWidget = (C.QuantitativeWidgetDefinition)CurrentWidget(widget);
            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(
                tempRoot,
                Array.Empty<C.WidgetDefinition>());
            using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    [metricId] = 125_000_000d
                },
                antialias: false,
                typefaces);

            Assert(context.TryGetMetricDouble(currentWidget, out var widgetValue) &&
                   Math.Abs(widgetValue - 1000d) < 0.000001,
                "Quantitative widget metric access must apply the widget-selected unit before renderer logic.");

            Assert(BinarySignalContract.TryResolve(
                    widgetValue,
                    BinarySignalContract.EvaluationModeSetpoint,
                    "900",
                    BinarySignalContract.TrueIfGreaterThan,
                    out var binaryState) && binaryState,
                "Binary numeric setpoints must be evaluated in the converted quantitative unit.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }

        var durationDescriptor = new MetricDescriptor(
            "test-units.duration",
            MetricValueKind.Duration,
            MetricUnit.Milliseconds);
        Assert(MetricValueConverter.TryConvertNumeric(90_000d, durationDescriptor, "minutes", out var minutes) &&
               Math.Abs(minutes - 1.5d) < 0.000001,
            "Central numeric conversion must also support non-throughput quantitative units.");
        Assert(!MetricValueConverter.TryConvertNumeric(90_000d, durationDescriptor, "auto", out _),
            "Textual duration auto-format must not be accepted as a quantitative numeric unit.");
    }

    private static void QuantitativeWidgetUnitPropertyVisibility()
    {
        const string metricId = "test-units.network.download";
        var expectedDescriptor = new MetricDescriptor(metricId, MetricValueKind.Number, MetricUnit.BytesPerSecond);
        MetricContract.ReplaceProviderDescriptors("test-units", [expectedDescriptor]);
        var descriptor = MetricContract.GetDescriptor(metricId);
        Assert(descriptor is not null, "Quantitative unit test descriptor is missing.");

        foreach (var type in new[]
                 {
                     WidgetTypeContract.Bar,
                     WidgetTypeContract.Gauge,
                 })
        {
            var widget = new WidgetDefinition
            {
                Type = type,
                Metric = metricId
            };
            var properties = CurrentPropertyView(widget, section: EditorPropertySection.Data).GetProperties();
            Assert(GetNestedProperty(properties, "Value", nameof(C.QuantitativeWidgetDefinition.Unit)) is not null,
                $"{type} must expose Unit for a convertible numeric metric.");
        }

        var binaryAuto = new WidgetDefinition
        {
            Type = WidgetTypeContract.Binary,
            Metric = metricId,
            EvaluationMode = BinarySignalContract.EvaluationModeAuto
        };
        Assert(GetNestedProperty(
                   CurrentPropertyView(binaryAuto, section: EditorPropertySection.Data).GetProperties(),
                   "Value",
                   nameof(C.BinaryWidgetDefinition.Unit)) is null,
            "Binary Auto icon-only mode must not expose Unit because no converted numeric consumer exists.");
        var binaryAutoData = CurrentPropertyView(binaryAuto, section: EditorPropertySection.Data).GetProperties();
        var autoTrueIf = GetNestedProperty(binaryAutoData, "Evaluation", nameof(WidgetDefinition.TrueIf));
        var autoSetpoint = GetNestedProperty(binaryAutoData, "Evaluation", nameof(WidgetDefinition.Setpoint));
        Assert(binaryAutoData.Count == 1 &&
               GetNestedProperty(binaryAutoData, "Evaluation", nameof(WidgetDefinition.EvaluationMode)) is { DisplayName: "Mode" } &&
               autoTrueIf is { IsReadOnly: true } &&
               autoSetpoint is { IsReadOnly: true } &&
               autoTrueIf.Description == "Not applicable for selected Mode." &&
               autoSetpoint.Description == "Not applicable for selected Mode.",
            "Binary Auto must keep comparison rows stable and disable them while Mode is Auto.");

        var binarySetpoint = new WidgetDefinition
        {
            Type = WidgetTypeContract.Binary,
            Metric = metricId,
            EvaluationMode = BinarySignalContract.EvaluationModeSetpoint
        };
        Assert(GetNestedProperty(
                   CurrentPropertyView(binarySetpoint, section: EditorPropertySection.Data).GetProperties(),
                   "Value",
                   nameof(C.BinaryWidgetDefinition.Unit)) is not null,
            "Binary Setpoint mode must expose Unit for a convertible numeric metric.");
        var binarySetpointData = CurrentPropertyView(binarySetpoint, section: EditorPropertySection.Data).GetProperties();
        var setpointFormat = GetNestedProperty(binarySetpointData, "Value", nameof(WidgetDefinition.Format));
        Assert(GetNestedProperty(binarySetpointData, "Value", nameof(WidgetDefinition.SourceUnit)) is { IsReadOnly: true } &&
               GetNestedProperty(binarySetpointData, "Value", nameof(WidgetDefinition.Unit)) is { IsReadOnly: false } &&
               setpointFormat is { IsReadOnly: true } &&
               setpointFormat.Description == "Not applicable for selected Source Type." &&
               binarySetpointData["Display"] is null &&
               GetNestedProperty(binarySetpointData, "Evaluation", nameof(WidgetDefinition.EvaluationMode)) is { DisplayName: "Mode" } &&
               GetNestedProperty(binarySetpointData, "Evaluation", nameof(WidgetDefinition.TrueIf)) is { DisplayName: "True If", IsReadOnly: false } &&
               GetNestedProperty(binarySetpointData, "Evaluation", nameof(WidgetDefinition.Setpoint)) is { DisplayName: "Setpoint", IsReadOnly: false },
            "Binary numeric Setpoint must retain contextual Value rows while keeping text Format disabled for icon-only presentation.");

        var numericUnits = MetricContract.GetNumericUnits(descriptor!);
        Assert(numericUnits.Any(option => option.Token == "Mbit/s"),
            "Quantitative unit selector must include Mbit/s for a byte-rate metric.");

        var durationDescriptor = new MetricDescriptor(
            "test-units.duration",
            MetricValueKind.Duration,
            MetricUnit.Milliseconds);
        var durationUnits = MetricContract.GetNumericUnits(durationDescriptor);
        Assert(durationUnits.All(option => !string.Equals(option.Token, "auto", StringComparison.OrdinalIgnoreCase)),
            "Quantitative unit selector must exclude textual duration auto-format.");
    }

}
