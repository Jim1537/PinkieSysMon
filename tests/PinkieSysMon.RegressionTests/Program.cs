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

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("Telemetry provider failure isolation", TelemetryProviderFailureIsIsolated),
            ("Telemetry reconfiguration clears deactivated metrics", TelemetryReconfigurationClearsDeactivatedMetrics),
            ("Telemetry reconfiguration prepare/commit isolation", TelemetryReconfigurationPrepareCommitIsolation),
            ("Output device discovery identity", OutputDeviceDiscoveryIdentity),
            ("Output target selection", OutputTargetSelection),
            ("Output config legacy default and persistence", OutputConfigLegacyDefaultAndPersistence),
            ("Output config multi-target safety", OutputConfigMultiTargetSafety),
            ("Trofeo wire framing contract", TrofeoWireFramingContract),
            ("Trofeo USB transfer segmentation contract", TrofeoUsbTransferSegmentationContract),
            ("USB diagnostic config contract", UsbDiagnosticConfigContract),
            ("USB diagnostic journal contract", UsbDiagnosticJournalContract),
            ("Trofeo overlapped wait completed", TrofeoOverlappedWaitCompleted),
            ("Trofeo overlapped wait canceled and drained", TrofeoOverlappedWaitCanceledAndDrained),
            ("Trofeo overlapped wait cancellation failure drains", TrofeoOverlappedWaitCancellationFailureDrains),
            ("Frame pump suspend during blocked USB open", FramePumpSuspendDuringBlockedOpen),
            ("Frame pump suspend during blocked USB send", FramePumpSuspendDuringBlockedSend),
            ("Frame pump reconfigure during blocked USB send", FramePumpReconfigureDuringBlockedSend),
            ("Output session manager lifecycle", OutputSessionManagerLifecycle),
            ("Output session manager reconfiguration", OutputSessionManagerReconfiguration),
            ("Output session manager transactional preparation", OutputSessionManagerTransactionalPreparation),
            ("Output session commit rollback", OutputSessionCommitRollback),
            ("Output session startup rollback", OutputSessionStartupRollback),
            ("Output session Start All startup rollback", OutputSessionStartAllStartupRollback),
            ("Output session live reconfigure startup rollback", OutputSessionLiveReconfigureStartupRollback),
            ("Output session lifecycle failure isolation", OutputSessionLifecycleFailureIsolation),
            ("Runtime IPC output command protocol", RuntimeIpcOutputCommandProtocol),
            ("Runtime IPC output status payload", RuntimeIpcOutputStatusPayload),
            ("Runtime IPC output control isolation", RuntimeIpcOutputControlIsolation),
            ("Runtime IPC shutdown cancellation contract", RuntimeIpcShutdownCancellationContract),
            ("Runtime output-stopped startup mode", RuntimeOutputStoppedStartupMode),
            ("Output menu device command model", ScreenMenuDeviceCommandModel),
            ("Editor main-menu Group cleanup", EditorMainMenuGroupCleanup),
            ("Editor native title-bar layout contract", EditorWindowChromeHitTestContract),
            ("Editor title-bar native ownership contract", EditorTitleBarCaptionVisualExclusion),
            ("Editor title-bar menu integration", EditorTitleBarMenuIntegration),
            ("Editor title-bar vertical alignment", EditorTitleBarVerticalAlignment),
            ("Editor title-bar menu theme contract", EditorTitleBarMenuThemeContract),
            ("Editor theme settings contract", EditorThemeSettingsContract),
            ("Editor property-tab icon presentation", EditorPropertyTabIconPresentationContract),
            ("Editor Windows App SDK title-bar ownership contract", EditorDwmNonClientForwardingContract),
            ("Windows application identity metadata", WindowsApplicationIdentityMetadata),
            ("Runtime modern app notification contract", RuntimeModernAppNotificationContract),
            ("Runtime notification self-contained fallback contract", RuntimeNotificationSelfContainedFallbackContract),
            ("WinForms fatal exception policy", WinFormsFatalExceptionPolicyContract),
            ("PASS 8 shell modernization audit", Pass8ShellModernizationAudit),
            ("Rejected PropertyGrid LabelBold hack absent", RejectedPropertyGridLabelBoldHackAbsent),
            ("Empty editor group hierarchy", EmptyEditorGroupIsValid),
            ("Nested empty editor group hierarchy", NestedEmptyEditorGroupsAreValid),
            ("Layer command homogeneous selection", LayerCommandHomogeneousSelection),
            ("LHM duplicate sensor ID disambiguation", LhmDuplicateSensorIdDisambiguation),
            ("LHM throughput unit semantics", LhmThroughputUnitSemantics),
            ("Quantitative widget unit conversion", QuantitativeWidgetUnitConversion),
            ("Quantitative widget unit property visibility", QuantitativeWidgetUnitPropertyVisibility),
            ("Windows internet connectivity metric", WindowsInternetConnectivityMetric),
            ("Binary numeric truth semantics", BinaryNumericTruthSemantics),
            ("Binary setpoint semantics", BinarySetpointSemantics),
            ("State image color fidelity", StateImageColorFidelity),
            ("State image property visibility", StateImagePropertyVisibility),
            ("Binary value state visual", BinaryValueStateVisual),
            ("Binary value container layout", BinaryValueContainerLayout),
            ("Binary value container height clip", BinaryValueContainerHeightClip),
            ("Binary value auto-width geometry", BinaryValueAutoWidthGeometry),
            ("Binary value-only geometry validation", BinaryValueOnlyGeometryValidation),
            ("Binary value property visibility", BinaryValuePropertyVisibility),
            ("Binary value unit options", BinaryValueUnitOptions),
            ("Metric unit options follow metric changes", MetricUnitOptionsFollowMetricChanges),
            ("Text/value source semantics", TextValueSourceSemantics),
            ("Text/value property visibility", TextValuePropertyVisibility),
            ("Literal numeric data semantics", LiteralNumericDataSemantics),
            ("Literal numeric unit options follow source unit", LiteralNumericUnitOptionsFollowSourceUnit),
            ("Literal numeric dashboard normalization", LiteralNumericDashboardNormalization),
            ("Text overflow state transitions", TextOverflowStateTransitions),
            ("Text overflow dashboard normalization", TextOverflowDashboardNormalization),
            ("Widget source property hierarchy", WidgetSourcePropertyHierarchy),
            ("Universal widget property hierarchy", UniversalWidgetPropertyHierarchy),
            ("Appearance property hierarchy", AppearancePropertyHierarchy),
            ("Data property hierarchy", DataPropertyHierarchy),
            ("Gauge property hierarchy", GaugePropertyHierarchy),
            ("States property hierarchy", StatesPropertyHierarchy),
            ("Image property hierarchy", ImagePropertyHierarchy),
            ("Binary state image schema", BinaryStateImageSchema),
            ("Binary state animation playback", BinaryStateAnimationPlayback),
            ("Binary state text schema", BinaryStateTextSchema),
            ("Binary state text independence", BinaryStateTextIndependence),
            ("Binary state text rendering", BinaryStateTextRendering),
            ("Binary schema 10 migration", BinarySchema10Migration),
            ("Power state property hierarchy", PowerStatePropertyHierarchy),
            ("Power state presentation schema", PowerStatePresentationSchema),
            ("Power state text rendering", PowerStateTextRendering),
            ("Power schema 13 migration", PowerSchema13Migration),
            ("Media Player state property hierarchy", MediaPlayerStatePropertyHierarchy),
            ("Media Player state presentation schema", MediaPlayerStatePresentationSchema),
            ("Media Player state text rendering", MediaPlayerStateTextRendering),
            ("Media Player schema 14 migration", MediaPlayerSchema14Migration),
            ("Media System state property hierarchy", MediaSystemStatePropertyHierarchy),
            ("Media System state presentation schema", MediaSystemStatePresentationSchema),
            ("Media System state text rendering", MediaSystemStateTextRendering),
            ("Media System schema 15 migration", MediaSystemSchema15Migration),
            ("Removed legacy widget types", RemovedLegacyWidgetTypes),
            ("Text property hierarchy", TextPropertyHierarchy),
            ("Property applicability contract", PropertyApplicabilityContract),
            ("Context-disabled custom editors", ContextDisabledCustomEditors),
            ("Date/time format editor", DateTimeFormatEditorContract),
            ("Global icon format discovery and capabilities", GlobalIconFormatDiscoveryAndCapabilities),
            ("Global icon Windows-safe logical names", GlobalIconWindowsSafeLogicalNames),
            ("Windows path-component validation contract", WindowsPathComponentValidationContract),
            ("Global icon rendering and applicability", GlobalIconRenderingAndApplicability),
            ("Multi-selection common property editing", MultiSelectionCommonPropertyEditing),
            ("Multi-selection applicability and context", MultiSelectionApplicabilityAndContext),
            ("Canvas normalized property hierarchy", CanvasNormalizedPropertyHierarchy),
            ("Canvas image source/presentation normalization", CanvasImageSourcePresentationNormalization),
            ("Canvas schema 16 geometry migration", CanvasSchema16GeometryMigration),
            ("Canvas schema 17 image-layer migration", CanvasSchema17ImageLayerMigration),
            ("Property-model schema 18 to 19 migration", PropertyModelSchema18To19Migration),
            ("Property-model full migration chain to schema 19", PropertyModelFullMigrationChain),
            ("Canonical dashboard JSON strict unknown-field rejection", CanonicalDashboardJsonStrictUnknownFieldRejection),
            ("Canonical dashboard JSON round trip", CanonicalDashboardJsonRoundTrip),
            ("Property-model schema 19 migration idempotence", PropertyModelSchema19MigrationIdempotence),
            ("Runtime dashboard load uses canonical schema 19 model", RuntimeDashboardLoadUsesCanonicalModel),
            ("Canonical dashboard metric usage semantics", CanonicalDashboardMetricUsageSemantics),
            ("Canonical current-model ownership", CanonicalCurrentModelOwnership),
            ("Canonical typed widget serialization", CanonicalTypedWidgetSerialization),
            ("Canonical shared text-presentation parity", CanonicalSharedTextPresentationParity),
            ("Canonical image source-kind transition clears source", CanonicalImageSourceKindTransitionClearsSource),
            ("Canvas image-layer applicability", CanvasImageLayerApplicability),
            ("Canvas image-layer render order", CanvasImageLayerRenderOrder),
            ("Canvas image-layer fit modes", CanvasImageLayerFitModes),
            ("Canvas global icon image-layer rendering", CanvasGlobalIconImageLayerRendering),
            ("Canvas animated image-layer refresh", CanvasAnimatedImageLayerRefresh),
            ("Shared Image visual preserves file fallback", SharedImageVisualPreservesFileFallback),
            ("Canvas current schema rejects legacy paths", CanvasCurrentSchemaRejectsLegacyPaths),
            ("Canvas orientation render matrix", CanvasOrientationRenderMatrix),
            ("Canvas runtime/editor image-layer parity", CanvasRuntimeEditorImageLayerParity),
            ("Canvas large logical geometry hit testing", CanvasLargeLogicalGeometryHitTesting),
            ("Configurable Canvas geometry", ConfigurableCanvasGeometry),
            ("Configurable Canvas render surfaces", ConfigurableCanvasRenderSurfaces),
            ("All widget properties assigned to explicit tabs", AllWidgetPropertiesAssignedToExplicitTabs),
            ("Property expansion state persistence", PropertyExpansionStatePersistence),
            ("Editor input focus ownership", EditorInputFocusOwnership),
            ("Layer tree pointer focus and inactive selection contract", LayerTreePointerFocusAndInactiveSelectionContract),
            ("Editor geometry whole-pixel normalization", EditorGeometryWholePixelNormalization),
            ("Layer tree viewport anchor persistence", LayerTreeViewportAnchorPersistence),
            ("Grid split-button behavior", GridSplitButtonBehavior),
            ("Dashboard universal size schema", DashboardUniversalSizeSchema),
            ("Quantitative foreground color schema", QuantitativeForegroundColorSchema),
            ("Bar foreground color rendering", BarForegroundColorRendering),
            ("Removed Graph widget type", RemovedGraphWidgetType),
            ("Bar image property visibility", BarImagePropertyVisibility),
            ("Bar image progress modes", BarImageProgressModes),
            ("Bar image fit modes", BarImageFitModes),
            ("Gauge universal container effects", GaugeUsesUniversalContainerEffects),
            ("Gauge needle offsets and pointer", GaugeNeedleOffsetsAndPointer),
            ("Gauge needle above track", GaugeNeedleRendersAboveTrack),
            ("Static state-file animation classification", StaticStateFileAnimationClassification),
            ("Stateful widget shadow-cache animation contract", StatefulWidgetShadowCacheAnimationContract),
            ("PASS 9F stateful renderer unification contract", Pass9FStatefulRendererUnificationContract),
            ("Runtime JPEG zero-copy transport contract", RuntimeJpegZeroCopyTransportContract),
            ("Stable widget shadow cache", StableWidgetShadowIsReused),
            ("Numeric widget shadow cache invalidation", NumericWidgetShadowInvalidatesOnValueChange),
            ("Cached widget shadow follows translation", CachedWidgetShadowFollowsTranslation),
            ("Resize local-axis projection", ResizeLocalAxisProjection),
            ("Gauge resize geometry clamp", GaugeResizeGeometryClamp),
            ("Preview resize geometry invalidation", PreviewResizeGeometryInvalidation)
        };

        var failed = 0;
        foreach (var test in tests)
        {
            try
            {
                test.Run();
                Console.WriteLine($"PASS: {test.Name}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine($"FAIL: {test.Name}");
                Console.Error.WriteLine(ex);
            }
        }

        Console.WriteLine($"Regression checks: {tests.Length - failed}/{tests.Length} passed.");
        return failed == 0 ? 0 : 1;
    }


    private static EditorPropertyView CurrentPropertyView(
        object target,
        string? applicationRoot = null,
        string? dashboardDirectory = null,
        IReadOnlyList<WindowsMediaTelemetrySource.MediaEndpointInfo>? mediaEndpoints = null,
        IDictionary<string, string>? endpointTypeOverrides = null,
        Func<object, (float Width, float Height)?>? effectiveSizeAccessor = null,
        EditorPropertySection section = EditorPropertySection.All)
    {
        object current = target switch
        {
            WidgetDefinition legacyWidget => PropertyModelNormalizationMigration.MapLegacyWidget(legacyWidget),
            CanvasDefinition legacyCanvas => PropertyModelNormalizationMigration.MapLegacyDefinition(new DashboardDefinition
            {
                SchemaVersion = DashboardSchemaMigration.CanvasImageLayerSchemaVersion,
                Canvas = legacyCanvas,
                Widgets = []
            }).Canvas,
            _ => target
        };

        return new EditorPropertyView(
            current,
            applicationRoot,
            dashboardDirectory,
            mediaEndpoints,
            endpointTypeOverrides,
            effectiveSizeAccessor,
            section);
    }

    private static C.WidgetDefinition CurrentWidget(WidgetDefinition legacy) =>
        PropertyModelNormalizationMigration.MapLegacyWidget(legacy);

    private static C.DashboardDefinition CurrentDashboard(DashboardDefinition legacy) =>
        PropertyModelNormalizationMigration.MapLegacyDefinition(legacy);

    private static C.WidgetDefinition CreateCanonicalPropertyTestWidget(
        string type,
        string id,
        float width = 100f,
        float height = 100f)
    {
        C.WidgetDefinition widget = type switch
        {
            WidgetTypeContract.Value => new C.ValueWidgetDefinition
            {
                Id = id,
                SourceKind = C.ValueSourceKind.Text,
                Text = string.Empty,
                Width = width,
                Height = height
            },
            WidgetTypeContract.Binary => new C.BinaryWidgetDefinition
            {
                Id = id,
                Metric = RuntimeMetricContract.Fps,
                Width = width,
                Height = height
            },
            WidgetTypeContract.Gauge => new C.GaugeWidgetDefinition
            {
                Id = id,
                Metric = RuntimeMetricContract.Fps,
                Width = width,
                Height = width,
                Min = 0,
                Max = 100
            },
            WidgetTypeContract.Bar => new C.BarWidgetDefinition
            {
                Id = id,
                Metric = RuntimeMetricContract.Fps,
                Width = width,
                Height = height,
                Min = 0,
                Max = 100
            },
            WidgetTypeContract.Image => new C.ImageWidgetDefinition
            {
                Id = id,
                Width = width,
                Height = height
            },
            WidgetTypeContract.Power => new C.PowerWidgetDefinition
            {
                Id = id,
                PowerSource = PowerMetricContract.UpsSource,
                Width = width,
                Height = height
            },
            WidgetTypeContract.MediaSystem => new C.MediaSystemWidgetDefinition
            {
                Id = id,
                MediaSource = MediaMetricContract.OutputSource,
                Width = width,
                Height = height
            },
            WidgetTypeContract.MediaPlayer => new C.MediaPlayerWidgetDefinition
            {
                Id = id,
                Width = width,
                Height = height
            },
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported canonical property-test widget type.")
        };

        if (widget is C.StateVisualWidgetDefinition stateWidget)
            TextOverflowStateContract.EnsureProfiles(stateWidget);
        return widget;
    }

    private static void OutputDeviceDiscoveryIdentity()
    {
        const string pathA = @"\\?\usb#vid_0416&pid_5408#SERIAL_A#{dee824ef-729b-4a0e-9c14-b7117d33a817}";
        const string pathB = @"\\?\usb#vid_0416&pid_5408#SERIAL_B#{dee824ef-729b-4a0e-9c14-b7117d33a817}";
        const string unrelated = @"\\?\usb#vid_1234&pid_5678#OTHER#{dee824ef-729b-4a0e-9c14-b7117d33a817}";

        var devices = DeviceDiscovery.CreateTrofeoDescriptors([pathA, pathA.ToUpperInvariant(), pathB, unrelated]);
        Assert(devices.Count == 2, "Trofeo discovery must enumerate every unique compatible interface instead of returning the first match.");
        Assert(devices.All(device => device.Transport == OutputTargetContract.TrofeoTransport),
            "Discovered Trofeo devices must expose the Trofeo transport identity.");
        Assert(devices.Select(device => device.DeviceId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2,
            "Each physical interface must receive a distinct stable DeviceId.");
        Assert(DeviceDiscovery.CreateStableDeviceId(pathA) == DeviceDiscovery.CreateStableDeviceId(pathA.ToUpperInvariant()),
            "Trofeo DeviceId generation must be insensitive to Windows device-path casing.");
        Assert(devices.All(device => !device.SelectionLabel.Contains("vid_0416", StringComparison.OrdinalIgnoreCase) &&
                                     !device.SelectionLabel.Contains("pid_5408", StringComparison.OrdinalIgnoreCase)),
            "User-facing output-device labels must not expose raw VID/PID transport identity.");
    }

    private static void OutputTargetSelection()
    {
        const string pathA = @"\\?\usb#vid_0416&pid_5408#SERIAL_A#{dee824ef-729b-4a0e-9c14-b7117d33a817}";
        const string pathB = @"\\?\usb#vid_0416&pid_5408#SERIAL_B#{dee824ef-729b-4a0e-9c14-b7117d33a817}";
        var devices = DeviceDiscovery.CreateTrofeoDescriptors([pathA, pathB]);

        var target = new OutputTargetConfig();
        var ambiguous = OutputDeviceSelection.Resolve(target, devices);
        Assert(ambiguous.Status == OutputDeviceResolutionStatus.Ambiguous && ambiguous.Device is null,
            "An unbound target must not silently select the first device when multiple compatible devices are present.");

        var single = OutputDeviceSelection.Resolve(target, [devices[0]]);
        Assert(single.Status == OutputDeviceResolutionStatus.Found && single.Device?.DeviceId == devices[0].DeviceId,
            "Legacy unbound configuration must retain single-device behavior when exactly one compatible device is present.");

        target.DeviceId = devices[1].DeviceId;
        var explicitMatch = OutputDeviceSelection.Resolve(target, devices);
        Assert(explicitMatch.Status == OutputDeviceResolutionStatus.Found && explicitMatch.Device?.DeviceId == devices[1].DeviceId,
            "An explicitly bound output target must resolve only its configured physical device.");

        target.DeviceId = "trofeo:" + new string('0', 64);
        var missing = OutputDeviceSelection.Resolve(target, devices);
        Assert(missing.Status == OutputDeviceResolutionStatus.Missing && missing.Device is null,
            "A missing explicitly configured output device must not fall back to another compatible device.");
    }

    private static void OutputConfigLegacyDefaultAndPersistence()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"PinkieSysMon-output-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var path = Path.Combine(tempRoot, "app.json");
            File.WriteAllText(path, "{\"Dashboard\":{\"Active\":\"Default\"}}");
            var log = new FileLogger(Path.Combine(tempRoot, "regression.log"));

            var config = AppConfig.Load(path, log);
            var target = OutputTargetContract.GetPrimary(config);
            Assert(target.Id == OutputTargetContract.DefaultTargetId &&
                   target.Name == OutputTargetContract.DefaultTargetName &&
                   target.Transport == OutputTargetContract.TrofeoTransport &&
                   target.DeviceId.Length == 0,
                "Legacy app.json must acquire one compatible default logical output target without requiring a manual migration.");

            target.DeviceId = "trofeo:" + new string('a', 64);
            config.Save(path);
            var reloaded = AppConfig.Load(path, log);
            var persistedTarget = OutputTargetContract.GetPrimary(reloaded);
            Assert(persistedTarget.DeviceId == target.DeviceId,
                "Explicit output-device binding must persist through app.json save/load.");
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    private static void OutputConfigMultiTargetSafety()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"PinkieSysMon-output-multi-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var path = Path.Combine(tempRoot, "app.json");
            var log = new FileLogger(Path.Combine(tempRoot, "regression.log"));
            var config = CreateMultiOutputConfig();

            config.Save(path);
            var reloaded = AppConfig.Load(path, log);
            Assert(reloaded.Outputs.Targets.Count == 2,
                "The runtime configuration must persist more than one explicit output target.");
            Assert(reloaded.Outputs.Targets.Select(target => target.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2,
                "Configured output target IDs must remain unique after persistence.");

            var unbound = CreateMultiOutputConfig();
            unbound.Outputs.Targets[1].DeviceId = string.Empty;
            AssertThrows<InvalidDataException>(() => unbound.Save(path),
                "Multiple configured output targets must require explicit physical-device bindings.");

            var duplicateDevice = CreateMultiOutputConfig();
            duplicateDevice.Outputs.Targets[1].DeviceId = duplicateDevice.Outputs.Targets[0].DeviceId;
            AssertThrows<InvalidDataException>(() => duplicateDevice.Save(path),
                "Two logical output targets must not be allowed to bind to the same physical device.");
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    private static void TrofeoWireFramingContract()
    {
        var layouts = new[]
        {
            (Payload: 0, Chunks: 1, Padded: 4, Frame: 2048, Last: 0),
            (Payload: 1, Chunks: 1, Padded: 4, Frame: 2048, Last: 1),
            (Payload: 495, Chunks: 1, Padded: 4, Frame: 2048, Last: 495),
            (Payload: 496, Chunks: 2, Padded: 4, Frame: 2048, Last: 0),
            (Payload: 497, Chunks: 2, Padded: 4, Frame: 2048, Last: 1),
            (Payload: 1983, Chunks: 4, Padded: 4, Frame: 2048, Last: 495),
            (Payload: 1984, Chunks: 5, Padded: 8, Frame: 4096, Last: 0)
        };

        foreach (var expected in layouts)
        {
            var actual = TrofeoWireProtocol.GetFrameLayout(expected.Payload);
            Assert(
                actual.ChunkCount == expected.Chunks &&
                actual.PaddedChunkCount == expected.Padded &&
                actual.FrameLength == expected.Frame &&
                actual.LastDataLength == expected.Last,
                $"Trofeo layout mismatch for {expected.Payload} bytes: " +
                $"chunks={actual.ChunkCount}, padded={actual.PaddedChunkCount}, " +
                $"frame={actual.FrameLength}, last={actual.LastDataLength}.");
        }

        var payload = Enumerable.Range(0, 497)
            .Select(index => (byte)(index % 251))
            .ToArray();
        var layout = TrofeoWireProtocol.GetFrameLayout(payload.Length);
        var frame = Enumerable.Repeat((byte)0xA5, layout.FrameLength).ToArray();

        var written = TrofeoWireProtocol.WriteFrame(payload, frame);
        Assert(written == layout.FrameLength,
            "Trofeo frame writer must return the padded wire-frame length.");

        var first = frame.AsSpan(0, TrofeoWireProtocol.ChunkSize);
        Assert(first[0] == 0x01 && first[1] == 0xFF && first[8] == 0x01,
            "Trofeo first record markers must remain 01 FF / 01.");
        Assert(
            System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(first.Slice(2, 4)) == (uint)payload.Length &&
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(first.Slice(6, 2)) == TrofeoWireProtocol.DataSize &&
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(first.Slice(9, 2)) == 2 &&
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(first.Slice(11, 2)) == 0,
            "Trofeo first record header must preserve total size, data length, chunk count and index.");
        Assert(first.Slice(13, 3).ToArray().All(value => value == 0),
            "Trofeo reserved header bytes must be zeroed.");
        Assert(payload.AsSpan(0, TrofeoWireProtocol.DataSize).SequenceEqual(
                   first.Slice(TrofeoWireProtocol.HeaderSize, TrofeoWireProtocol.DataSize)),
            "Trofeo first record payload bytes must be preserved verbatim.");

        var second = frame.AsSpan(TrofeoWireProtocol.ChunkSize, TrofeoWireProtocol.ChunkSize);
        Assert(
            System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(second.Slice(2, 4)) == (uint)payload.Length &&
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(second.Slice(6, 2)) == 1 &&
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(second.Slice(9, 2)) == 2 &&
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(second.Slice(11, 2)) == 1,
            "Trofeo terminal record header must preserve total size, residual length, chunk count and index.");
        Assert(second[TrofeoWireProtocol.HeaderSize] == payload[^1],
            "Trofeo terminal record must contain the final payload byte.");
        Assert(second.Slice(TrofeoWireProtocol.HeaderSize + 1, TrofeoWireProtocol.DataSize - 1)
                .ToArray()
                .All(value => value == 0),
            "Trofeo unused bytes in the terminal record must be zero padded.");
        Assert(frame.AsSpan(TrofeoWireProtocol.ChunkSize * 2).ToArray().All(value => value == 0),
            "Trofeo padding records must be completely zeroed.");

        var exactPayload = new byte[TrofeoWireProtocol.DataSize];
        var exactLayout = TrofeoWireProtocol.GetFrameLayout(exactPayload.Length);
        var exactFrame = new byte[exactLayout.FrameLength];
        TrofeoWireProtocol.WriteFrame(exactPayload, exactFrame);
        var exactTerminal = exactFrame.AsSpan(TrofeoWireProtocol.ChunkSize, TrofeoWireProtocol.ChunkSize);
        Assert(
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(exactTerminal.Slice(6, 2)) == 0 &&
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(exactTerminal.Slice(9, 2)) == 2 &&
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(exactTerminal.Slice(11, 2)) == 1,
            "An exact 496-byte payload must retain the proven extra zero-length terminal record.");
    }

    private static void TrofeoUsbTransferSegmentationContract()
    {
        static int[] Segment(int frameLength)
        {
            var result = new List<int>();
            for (var remaining = frameLength; remaining > 0;)
            {
                var count = TrofeoWireProtocol.GetTransferLength(remaining);
                Assert(count > 0 && count <= TrofeoWireProtocol.TransferBlockSize,
                    "Trofeo transfer segmentation must always make bounded forward progress.");
                result.Add(count);
                remaining -= count;
            }

            return result.ToArray();
        }

        Assert(Segment(2048).SequenceEqual(new[] { 2048 }),
            "A 2048-byte Trofeo wire frame must be sent as one transfer.");
        Assert(Segment(4096).SequenceEqual(new[] { 4096 }),
            "A 4096-byte Trofeo wire frame must be sent as one transfer.");
        Assert(Segment(6144).SequenceEqual(new[] { 4096, 2048 }),
            "A 6144-byte Trofeo wire frame must preserve 4096 + 2048 transfer segmentation.");
        Assert(Segment(8192).SequenceEqual(new[] { 4096, 4096 }),
            "An 8192-byte Trofeo wire frame must preserve two 4096-byte transfers.");
        Assert(Segment(10240).SequenceEqual(new[] { 4096, 4096, 2048 }),
            "A 10240-byte Trofeo wire frame must preserve repeated 4096-byte transfers plus the tail.");
    }

    private static void UsbDiagnosticConfigContract()
    {
        var config = new AppConfig();
        Assert(!config.Usb.DiagnosticJournalEnabled,
            "USB diagnostic journal must remain opt-in by default.");

        var tempRoot = Path.Combine(Path.GetTempPath(), "PinkieSysMon-usb-diagnostic-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var configPath = Path.Combine(tempRoot, "app.json");
            var log = new FileLogger(Path.Combine(tempRoot, "runtime.log"));

            config.Usb.DiagnosticJournalEnabled = true;
            config.Save(configPath);
            var persisted = AppConfig.Load(configPath, log);
            Assert(persisted.Usb.DiagnosticJournalEnabled,
                "USB diagnostic journal opt-in must persist through AppConfig save/load.");

            File.WriteAllText(
                configPath,
                """
                {
                  "Usb": {
                    "RetryIntervalMs": 3000,
                    "TransferTimeoutMs": 3000
                  }
                }
                """);
            var legacy = AppConfig.Load(configPath, log);
            Assert(!legacy.Usb.DiagnosticJournalEnabled,
                "Existing config without DiagnosticJournalEnabled must keep diagnostics disabled.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void UsbDiagnosticJournalContract()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "PinkieSysMon-usb-diagnostic-journal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var mainLogPath = Path.Combine(tempRoot, "runtime.log");
            var targetId = "screen:one/unsafe-for-filename";
            var derived = TrofeoDiagnosticJournal.GetPath(mainLogPath, targetId);
            Assert(
                string.Equals(Path.GetDirectoryName(derived), tempRoot, StringComparison.OrdinalIgnoreCase) &&
                !Path.GetFileName(derived).Contains(targetId, StringComparison.Ordinal),
                "USB diagnostic journal path must stay beside the runtime log without embedding raw target identity.");

            var journalPath = Path.Combine(tempRoot, "usb-test.journal.log");
            using (var journal = new TrofeoDiagnosticJournal(
                       journalPath,
                       maxBytes: 512,
                       maxArchiveCount: 2,
                       durableFlushInterval: TimeSpan.Zero))
            {
                for (var i = 0; i < 20; i++)
                    journal.Write("FILL", ("index", i), ("payload", new string('x', 80)));

                journal.Write(
                    "FRAME_TEST",
                    ("seq", 7),
                    ("text", "row\twith\ncontrol"),
                    ("duration", 12.5));
            }

            Assert(File.Exists(journalPath),
                "USB diagnostic journal must keep an active log after rotation.");
            Assert(File.Exists(journalPath + ".1"),
                "USB diagnostic journal must rotate when its bounded size is exceeded.");
            Assert(!File.Exists(journalPath + ".3"),
                "USB diagnostic journal must not exceed its configured archive count.");

            var allText = string.Join(
                Environment.NewLine,
                new[] { journalPath, journalPath + ".1", journalPath + ".2" }
                    .Where(File.Exists)
                    .Select(File.ReadAllText));
            Assert(allText.Contains("FRAME_TEST", StringComparison.Ordinal),
                "USB diagnostic journal must persist event names.");
            Assert(allText.Contains("text=row\\twith\\ncontrol", StringComparison.Ordinal),
                "USB diagnostic journal must escape control characters into one-line records.");
            Assert(allText.Contains("duration=12.5", StringComparison.Ordinal),
                "USB diagnostic journal must format numeric fields with invariant culture.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void TrofeoOverlappedWaitCompleted()
    {
        using var completion = new ManualResetEvent(initialState: true);
        using var cts = new CancellationTokenSource();
        var cancelRequests = 0;
        var drainCalls = 0;

        var canceled = TrofeoOverlappedCompletionWait.Wait(
            completion,
            cts.Token,
            () => cancelRequests++,
            () => drainCalls++);

        Assert(!canceled && cancelRequests == 0 && drainCalls == 0,
            "A completed overlapped operation must not request cancellation or an abort drain.");
    }

    private static void TrofeoOverlappedWaitCanceledAndDrained()
    {
        using var completion = new ManualResetEvent(initialState: false);
        using var cts = new CancellationTokenSource();
        using var waiting = new ManualResetEventSlim();
        var cancelRequests = 0;
        var drainCalls = 0;

        var worker = Task.Run(() =>
        {
            waiting.Set();
            return TrofeoOverlappedCompletionWait.Wait(
                completion,
                cts.Token,
                () =>
                {
                    Interlocked.Increment(ref cancelRequests);
                    completion.Set();
                },
                () =>
                {
                    Assert(completion.WaitOne(0),
                        "Cancel callback must run before the terminal-completion drain.");
                    Interlocked.Increment(ref drainCalls);
                });
        });

        Assert(waiting.Wait(TimeSpan.FromSeconds(2)),
            "Overlapped wait regression worker did not start.");
        cts.Cancel();
        Assert(worker.Wait(TimeSpan.FromSeconds(2)) && worker.Result,
            "Cancellation must unblock the pending overlapped wait.");
        Assert(cancelRequests == 1 && drainCalls == 1,
            "Cancel must be requested once and terminal completion must be drained exactly once.");
    }

    private static void TrofeoOverlappedWaitCancellationFailureDrains()
    {
        using var completion = new ManualResetEvent(initialState: false);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var drainCalls = 0;

        AssertThrows<InvalidOperationException>(
            () => TrofeoOverlappedCompletionWait.Wait(
                completion,
                cts.Token,
                () => throw new InvalidOperationException("Synthetic cancel request failure."),
                () => drainCalls++),
            "A native cancellation request failure must remain observable.");

        Assert(drainCalls == 1,
            "Even failed cancellation requests must drain pending OVERLAPPED completion before releasing memory.");
    }

    private static void FramePumpSuspendDuringBlockedOpen()
    {
        var root = CreateTemporaryApplicationRoot("PinkieSysMon-pump-open-ownership");
        using var openStarted = new ManualResetEventSlim();
        using var allowOpen = new ManualResetEventSlim();
        var fake = new BlockingTrofeoTestTransport();
        var pump = new FramePump(
            new AppConfig(),
            new OutputTargetConfig(),
            new C.DashboardDefinition { BaseDirectory = Path.Combine(root, "dashboards") },
            new MetricStore(),
            new FileLogger(Path.Combine(root, "ownership.log")),
            (target, timeout, journal, log) =>
            {
                openStarted.Set();
                if (!allowOpen.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("Test open was not released.");
                return fake;
            });

        try
        {
            pump.Start();
            Assert(openStarted.Wait(TimeSpan.FromSeconds(5)),
                "Worker must enter the injected blocking USB open.");

            var suspension = Task.Run(pump.Suspend);
            Assert(suspension.Wait(TimeSpan.FromSeconds(2)),
                "Suspend must not wait for in-progress USB discovery/handshake under the general session lock.");
            Assert(pump.UsbState == "SUSPENDED" && !pump.IsConnected,
                "Suspend must immediately expose SUSPENDED/unavailable even while USB open is blocked.");

            allowOpen.Set();
            Assert(SpinWait.SpinUntil(() => fake.DisposeCount == 1, TimeSpan.FromSeconds(5)),
                "A USB handle opened after suspend must be closed by the worker before any transfer.");
            Assert(fake.SendCount == 0,
                "No frame may be sent on a connection invalidated during USB open.");
        }
        finally
        {
            allowOpen.Set();
            pump.Dispose();
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void FramePumpSuspendDuringBlockedSend()
    {
        var root = CreateTemporaryApplicationRoot("PinkieSysMon-pump-send-ownership");
        using var sendStarted = new ManualResetEventSlim();
        using var allowSend = new ManualResetEventSlim();
        var fake = new BlockingTrofeoTestTransport(sendStarted, allowSend);
        var pump = new FramePump(
            new AppConfig(),
            new OutputTargetConfig(),
            new C.DashboardDefinition { BaseDirectory = Path.Combine(root, "dashboards") },
            new MetricStore(),
            new FileLogger(Path.Combine(root, "ownership.log")),
            (target, timeout, journal, log) => fake);

        try
        {
            pump.Start();
            Assert(sendStarted.Wait(TimeSpan.FromSeconds(5)),
                "Worker must reach the injected blocking frame transfer.");

            var suspension = Task.Run(pump.Suspend);
            Assert(suspension.Wait(TimeSpan.FromSeconds(2)),
                "Suspend must return without taking the USB sender's general state lock.");
            Assert(pump.UsbState == "SUSPENDED" && !pump.IsConnected,
                "In-flight transfer must not leave the public connection state CONNECTED after suspend.");
            Assert(fake.DisposeCount == 0,
                "Suspend must never dispose a transport while an in-flight send owns it.");

            allowSend.Set();
            Assert(SpinWait.SpinUntil(() => fake.DisposeCount == 1, TimeSpan.FromSeconds(5)),
                "Worker must release the in-flight USB handle after the blocked send completes.");
            Assert(fake.SendCount == 1 && pump.UsbState == "SUSPENDED",
                "Suspend must prevent any subsequent send or stale CONNECTED status.");
        }
        finally
        {
            allowSend.Set();
            pump.Dispose();
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void FramePumpReconfigureDuringBlockedSend()
    {
        var root = CreateTemporaryApplicationRoot("PinkieSysMon-pump-reconfig-ownership");
        using var sendStarted = new ManualResetEventSlim();
        using var allowSend = new ManualResetEventSlim();
        var original = new BlockingTrofeoTestTransport(sendStarted, allowSend);
        var replacement = new BlockingTrofeoTestTransport();
        var opens = 0;
        var config = new AppConfig();
        var target = new OutputTargetConfig();
        var pump = new FramePump(
            config, target, new C.DashboardDefinition { BaseDirectory = Path.Combine(root, "dashboards") }, new MetricStore(),
            new FileLogger(Path.Combine(root, "ownership.log")),
            (selectedTarget, timeout, journal, log) =>
                Interlocked.Increment(ref opens) == 1 ? original : replacement);

        try
        {
            pump.Start();
            Assert(sendStarted.Wait(TimeSpan.FromSeconds(5)),
                "Worker must enter the first device's blocked transfer.");

            var newConfig = new AppConfig();
            newConfig.Usb.TransferTimeoutMs = config.Usb.TransferTimeoutMs + 500;
            using var prepared = pump.PrepareReconfiguration(
                newConfig, new OutputTargetConfig(), new C.DashboardDefinition { BaseDirectory = Path.Combine(root, "dashboards") });

            var reconfigure = Task.Run(() => pump.CommitReconfiguration(prepared));
            Assert(reconfigure.Wait(TimeSpan.FromSeconds(2)),
                "Transport-affecting reconfigure must not wait for active USB I/O under the general lock.");
            Assert(original.DisposeCount == 0,
                "Reconfigure must not close a transport whose send has not finished.");

            allowSend.Set();
            Assert(SpinWait.SpinUntil(() => original.DisposeCount == 1, TimeSpan.FromSeconds(5)),
                "Old transport must be closed by its owning worker after reconfiguration.");

            Assert(SpinWait.SpinUntil(() => Volatile.Read(ref opens) >= 2, TimeSpan.FromSeconds(5)),
                "Worker must open a fresh transport after the in-flight old transfer has retired.");
        }
        finally
        {
            allowSend.Set();
            pump.Dispose();
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void OutputSessionManagerLifecycle()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"PinkieSysMon-output-sessions-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var log = new FileLogger(Path.Combine(tempRoot, "regression.log"));
            var metrics = new MetricStore();
            var created = new List<FakeOutputSession>();
            var config = CreateMultiOutputConfig();
            using var manager = new OutputSessionManager(
                config,
                new C.DashboardDefinition(),
                metrics,
                log,
                (app, target, dashboard, store, logger) =>
                {
                    var session = new FakeOutputSession(target.Id, target.Name);
                    created.Add(session);
                    return session;
                });

            manager.Start();
            Assert(created.Count == 2 && created.All(session => session.StartCount == 1),
                "Starting the runtime must create and start one independent session per configured output target.");
            Assert(created.Count(session => session.PublishesRuntimeMetrics) == 1 && created[0].PublishesRuntimeMetrics,
                "Exactly the first active configured output session must own the legacy global runtime metrics.");

            var statuses = manager.GetStatuses();
            Assert(statuses.Count == 2 && statuses.All(status => status.IsActive),
                "Every configured target must report an independent active session after startup.");

            Assert(manager.StopTarget("screen-a"), "Stopping an active target must report that a session was removed.");
            Assert(created[0].DisposeCount == 1 && created[1].DisposeCount == 0,
                "Stopping one output target must dispose only that target session.");
            Assert(created[1].PublishesRuntimeMetrics,
                "Runtime-metric ownership must move to the next active target when the previous publisher stops.");

            manager.StartOrReloadTarget("screen-a");
            Assert(created.Count == 3 && created[2].StartCount == 1,
                "Starting a stopped target must create a fresh session without restarting unrelated targets.");
            Assert(created[2].PublishesRuntimeMetrics && !created[1].PublishesRuntimeMetrics,
                "Restarting the first configured target must restore deterministic runtime-metric ownership.");

            manager.Suspend();
            Assert(created[1].SuspendCount == 1 && created[2].SuspendCount == 1,
                "Suspend must be propagated independently to every active output session.");
            manager.Resume();
            Assert(created[1].ResumeCount == 1 && created[2].ResumeCount == 1,
                "Resume must be propagated independently to every active output session.");

            manager.StopAll();
            statuses = manager.GetStatuses();
            Assert(statuses.All(status => !status.IsActive && status.UsbState == "STOPPED"),
                "StopAll must unload every configured output session without removing target configuration.");
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    private static void OutputSessionManagerReconfiguration()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"PinkieSysMon-output-reconfigure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var log = new FileLogger(Path.Combine(tempRoot, "regression.log"));
            var created = new List<FakeOutputSession>();
            var initial = CreateMultiOutputConfig();
            using var manager = new OutputSessionManager(
                initial,
                new C.DashboardDefinition(),
                new MetricStore(),
                log,
                (app, target, dashboard, store, logger) =>
                {
                    var session = new FakeOutputSession(target.Id, target.Name);
                    created.Add(session);
                    return session;
                });

            manager.Start();
            var firstA = created.Single(session => session.TargetId == "screen-a");
            var firstB = created.Single(session => session.TargetId == "screen-b");

            var replacement = new AppConfig
            {
                Outputs = new OutputConfig
                {
                    Targets =
                    [
                        new OutputTargetConfig
                        {
                            Id = "screen-b",
                            Name = "Right Trofeo",
                            DeviceId = "trofeo:" + new string('b', 64)
                        },
                        new OutputTargetConfig
                        {
                            Id = "screen-c",
                            Name = "Aux Trofeo",
                            DeviceId = "trofeo:" + new string('c', 64)
                        }
                    ]
                }
            };

            manager.Reconfigure(replacement, new C.DashboardDefinition());
            var statuses = manager.GetStatuses();
            Assert(firstA.DisposeCount == 1,
                "Removing a configured target must unload only its existing session.");
            Assert(firstB.CommitCount == 1 && firstB.TargetName == "Right Trofeo",
                "A same-ID target must be reconfigured in place rather than destroyed and recreated.");
            Assert(created.Any(session => session.TargetId == "screen-c" && session.StartCount == 1),
                "A newly configured target must acquire and start its own session during live reconfiguration.");
            Assert(statuses.Select(status => status.TargetId).SequenceEqual(new[] { "screen-b", "screen-c" }, StringComparer.OrdinalIgnoreCase),
                "Session status order must follow configured target order after reconciliation.");

            manager.StopTarget("screen-b");
            manager.Reconfigure(replacement, new C.DashboardDefinition());
            statuses = manager.GetStatuses();
            Assert(statuses.Single(status => status.TargetId == "screen-b").IsActive == false,
                "A manually stopped existing target must remain stopped across ordinary configuration reloads.");
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    private static void OutputSessionManagerTransactionalPreparation()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"PinkieSysMon-output-transaction-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var log = new FileLogger(Path.Combine(tempRoot, "regression.log"));
            var created = new List<FakeOutputSession>();
            var config = CreateMultiOutputConfig();
            using var manager = new OutputSessionManager(
                config,
                new C.DashboardDefinition(),
                new MetricStore(),
                log,
                (app, target, dashboard, store, logger) =>
                {
                    var session = new FakeOutputSession(target.Id, target.Name);
                    created.Add(session);
                    return session;
                });

            manager.Start();
            var first = created.Single(session => session.TargetId == "screen-a");
            var second = created.Single(session => session.TargetId == "screen-b");
            second.ThrowOnPrepare = true;

            AssertThrows<InvalidOperationException>(
                () => manager.Reconfigure(config, new C.DashboardDefinition()),
                "A failed session preparation must reject the whole output-set reconfiguration.");
            Assert(first.CommitCount == 0 && second.CommitCount == 0,
                "No active output session may be committed when any sibling session fails preparation.");
            Assert(manager.GetStatuses().All(status => status.IsActive),
                "Failed output-set reconfiguration must leave the previous active session set intact.");
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    private static void OutputSessionCommitRollback()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"PinkieSysMon-output-commit-rollback-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var log = new FileLogger(Path.Combine(tempRoot, "regression.log"));
            var created = new List<FakeOutputSession>();
            var initial = CreateMultiOutputConfig();
            using var manager = new OutputSessionManager(
                initial,
                new C.DashboardDefinition(),
                new MetricStore(),
                log,
                (app, target, dashboard, store, logger) =>
                {
                    var session = new FakeOutputSession(target.Id, target.Name);
                    created.Add(session);
                    return session;
                });

            manager.Start();
            var first = created.Single(session => session.TargetId == "screen-a");
            var second = created.Single(session => session.TargetId == "screen-b");
            second.ThrowOnNextCommit = true;

            var replacement = CreateMultiOutputConfig();
            replacement.Outputs.Targets[0].Name = "Left Trofeo New";
            replacement.Outputs.Targets[1].Name = "Right Trofeo New";

            AssertThrows<InvalidOperationException>(
                () => manager.Reconfigure(replacement, new C.DashboardDefinition()),
                "A commit failure must reject the whole output-set reconfiguration.");

            Assert(first.TargetName == "Left Trofeo" && second.TargetName == "Right Trofeo",
                "A sibling commit failure must roll every existing output session back to its previous configuration.");
            Assert(first.CommitCount == 2 && second.CommitCount == 2,
                "Commit rollback must restore both already-committed and partially-mutated sibling sessions.");
            Assert(manager.GetStatuses().Select(status => status.TargetName)
                    .SequenceEqual(new[] { "Left Trofeo", "Right Trofeo" }, StringComparer.Ordinal),
                "Failed output commit must keep the manager's externally visible session state on the previous configuration.");
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    private static void OutputSessionStartupRollback()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"PinkieSysMon-output-start-rollback-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var log = new FileLogger(Path.Combine(tempRoot, "regression.log"));
            var created = new List<FakeOutputSession>();
            var failSecondTarget = true;
            using var manager = new OutputSessionManager(
                CreateMultiOutputConfig(),
                new C.DashboardDefinition(),
                new MetricStore(),
                log,
                (app, target, dashboard, store, logger) =>
                {
                    var session = new FakeOutputSession(target.Id, target.Name)
                    {
                        ThrowOnStart = failSecondTarget && target.Id.Equals("screen-b", StringComparison.OrdinalIgnoreCase)
                    };
                    created.Add(session);
                    return session;
                });

            AssertThrows<InvalidOperationException>(
                manager.Start,
                "A startup failure in one output session must fail the initial output-set activation.");
            Assert(manager.GetStatuses().All(status => !status.IsActive),
                "Failed initial output startup must not leave any partially active sessions registered.");
            Assert(created.Count == 2 && created.All(session => session.DisposeCount == 1),
                "Every session created for a failed initial startup must be disposed exactly once.");

            failSecondTarget = false;
            manager.Start();
            Assert(manager.GetStatuses().All(status => status.IsActive),
                "A failed initial output startup must leave the manager retryable.");
            Assert(created.Skip(2).Count() == 2 && created.Skip(2).All(session => session.StartCount == 1),
                "Retry after startup rollback must create and start a fresh session set.");
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    private static void OutputSessionStartAllStartupRollback()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"PinkieSysMon-output-start-all-rollback-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var log = new FileLogger(Path.Combine(tempRoot, "regression.log"));
            var created = new List<FakeOutputSession>();
            var failRestartA = false;
            using var manager = new OutputSessionManager(
                CreateMultiOutputConfig(),
                new C.DashboardDefinition(),
                new MetricStore(),
                log,
                (app, target, dashboard, store, logger) =>
                {
                    var session = new FakeOutputSession(target.Id, target.Name)
                    {
                        ThrowOnStart = failRestartA && target.Id.Equals("screen-a", StringComparison.OrdinalIgnoreCase)
                    };
                    created.Add(session);
                    return session;
                });

            manager.Start();
            var liveB = created.Single(session => session.TargetId == "screen-b");
            manager.StopTarget("screen-a");
            failRestartA = true;

            AssertThrows<InvalidOperationException>(
                manager.StartOrReloadAll,
                "Start / Reload All must fail cleanly when a newly required session cannot start.");
            var failedStatuses = manager.GetStatuses();
            Assert(!failedStatuses.Single(status => status.TargetId == "screen-a").IsActive &&
                   failedStatuses.Single(status => status.TargetId == "screen-b").IsActive,
                "Failed Start / Reload All must preserve the previous active/stopped session split.");
            Assert(liveB.CommitCount == 0,
                "An already-active sibling must not be reconfigured when another target fails before startup commit.");
            var failedA = created.Last(session => session.TargetId == "screen-a");
            Assert(failedA.DisposeCount == 1,
                "The failed replacement session from Start / Reload All must be disposed.");

            failRestartA = false;
            manager.StartOrReloadAll();
            Assert(manager.GetStatuses().All(status => status.IsActive),
                "Start / Reload All must remain retryable after a startup rollback.");
            Assert(liveB.CommitCount == 1,
                "The existing sibling should be reconfigured only after all newly required sessions start successfully.");
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    private static void OutputSessionLiveReconfigureStartupRollback()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"PinkieSysMon-output-reconfigure-start-rollback-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var log = new FileLogger(Path.Combine(tempRoot, "regression.log"));
            var created = new List<FakeOutputSession>();
            var failNewTarget = true;
            var initial = CreateMultiOutputConfig();
            using var manager = new OutputSessionManager(
                initial,
                new C.DashboardDefinition(),
                new MetricStore(),
                log,
                (app, target, dashboard, store, logger) =>
                {
                    var session = new FakeOutputSession(target.Id, target.Name)
                    {
                        ThrowOnStart = failNewTarget && target.Id.Equals("screen-c", StringComparison.OrdinalIgnoreCase)
                    };
                    created.Add(session);
                    return session;
                });

            manager.Start();
            var liveA = created.Single(session => session.TargetId == "screen-a");
            var liveB = created.Single(session => session.TargetId == "screen-b");
            var replacement = new AppConfig
            {
                Outputs = new OutputConfig
                {
                    Targets =
                    [
                        new OutputTargetConfig
                        {
                            Id = "screen-b",
                            Name = "Right Trofeo",
                            DeviceId = "trofeo:" + new string('b', 64)
                        },
                        new OutputTargetConfig
                        {
                            Id = "screen-c",
                            Name = "Aux Trofeo",
                            DeviceId = "trofeo:" + new string('c', 64)
                        }
                    ]
                }
            };

            AssertThrows<InvalidOperationException>(
                () => manager.Reconfigure(replacement, new C.DashboardDefinition()),
                "A newly configured output session that cannot start must reject the live output-set reconfiguration.");
            var failedStatuses = manager.GetStatuses();
            Assert(failedStatuses.Select(status => status.TargetId)
                    .SequenceEqual(new[] { "screen-a", "screen-b" }, StringComparer.OrdinalIgnoreCase) &&
                   failedStatuses.All(status => status.IsActive),
                "Failed live reconfiguration startup must preserve the previous configured and active output set.");
            Assert(liveA.DisposeCount == 0 && liveB.CommitCount == 0,
                "Failed new-target startup must not unload a removed target or commit an existing sibling.");
            var failedC = created.Last(session => session.TargetId == "screen-c");
            Assert(failedC.DisposeCount == 1,
                "The failed newly configured session must be disposed without entering the active session table.");

            failNewTarget = false;
            manager.Reconfigure(replacement, new C.DashboardDefinition());
            Assert(manager.GetStatuses().Select(status => status.TargetId)
                    .SequenceEqual(new[] { "screen-b", "screen-c" }, StringComparer.OrdinalIgnoreCase),
                "Live output reconfiguration must remain retryable after a new-session startup rollback.");
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    private static void OutputSessionLifecycleFailureIsolation()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"PinkieSysMon-output-lifecycle-isolation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var log = new FileLogger(Path.Combine(tempRoot, "regression.log"));
            var created = new List<FakeOutputSession>();
            using var manager = new OutputSessionManager(
                CreateMultiOutputConfig(),
                new C.DashboardDefinition(),
                new MetricStore(),
                log,
                (app, target, dashboard, store, logger) =>
                {
                    var session = new FakeOutputSession(target.Id, target.Name);
                    created.Add(session);
                    return session;
                });

            manager.Start();
            var first = created.Single(session => session.TargetId == "screen-a");
            var second = created.Single(session => session.TargetId == "screen-b");
            first.ThrowOnSuspend = true;
            first.ThrowOnResume = true;
            first.ThrowOnDispose = true;

            manager.Suspend();
            Assert(first.SuspendCount == 1 && second.SuspendCount == 1,
                "A suspend failure in one output session must not prevent sibling sessions from receiving suspend.");

            manager.Resume();
            Assert(first.ResumeCount == 1 && second.ResumeCount == 1,
                "A resume failure in one output session must not prevent sibling sessions from receiving resume.");

            manager.StopAll();
            Assert(first.DisposeCount == 1 && second.DisposeCount == 1,
                "A disposal failure in one output session must not prevent sibling cleanup during Stop All.");
            Assert(manager.GetStatuses().All(status => !status.IsActive),
                "Lifecycle cleanup failures must not leave disposed output sessions registered as active.");
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    private static void RuntimeIpcOutputCommandProtocol()
    {
        const string targetId = "screen\t\"alpha\" ";

        var start = RuntimeIpcProtocol.ParseCommand(
            RuntimeIpcProtocol.BuildTargetCommand(RuntimeIpcProtocol.StartOrReloadTargetCommand, targetId));
        Assert(start.Kind == RuntimeIpcCommandKind.StartOrReloadTarget && start.TargetId == targetId,
            "Target-scoped IPC commands must preserve opaque output target IDs without delimiter ambiguity.");

        var stop = RuntimeIpcProtocol.ParseCommand(
            RuntimeIpcProtocol.BuildTargetCommand(RuntimeIpcProtocol.StopTargetCommand, targetId));
        Assert(stop.Kind == RuntimeIpcCommandKind.StopTarget && stop.TargetId == targetId,
            "Stop-target IPC must address the exact configured target ID.");

        Assert(RuntimeIpcProtocol.ParseCommand(RuntimeIpcProtocol.StartOrReloadAllCommand).Kind == RuntimeIpcCommandKind.StartOrReloadAll,
            "IPC must expose a distinct Start/Reload All output command.");
        Assert(RuntimeIpcProtocol.ParseCommand(RuntimeIpcProtocol.StopAllCommand).Kind == RuntimeIpcCommandKind.StopAll,
            "IPC must expose a distinct Stop All output command.");
        Assert(RuntimeIpcProtocol.ParseCommand(RuntimeIpcProtocol.StatusCommand).Kind == RuntimeIpcCommandKind.Status,
            "IPC must expose output-session status independently from the legacy process ping.");
        Assert(RuntimeIpcProtocol.ParseCommand(RuntimeIpcProtocol.PingCommand).Kind == RuntimeIpcCommandKind.Ping,
            "Legacy PING protocol compatibility must remain even when unused client helpers are removed.");
        Assert(RuntimeIpcProtocol.ParseCommand(RuntimeIpcProtocol.ExitCommand).Kind == RuntimeIpcCommandKind.Exit,
            "Legacy EXIT protocol compatibility must remain even when unused client helpers are removed.");

        var malformed = RuntimeIpcProtocol.ParseCommand(RuntimeIpcProtocol.StopTargetCommand + "\tnot-json");
        Assert(malformed.Kind == RuntimeIpcCommandKind.Invalid && !string.IsNullOrWhiteSpace(malformed.Error),
            "Malformed target-scoped IPC commands must be rejected deterministically.");
    }

    private static void RuntimeIpcOutputStatusPayload()
    {
        var expected = new RuntimeStatusSnapshot(
            "PinkiePie_Vertical",
            [
                new RuntimeOutputStatus("screen-a", "Left Trofeo", true, true, "CONNECTED"),
                new RuntimeOutputStatus("screen-b", "Right Trofeo", false, false, "STOPPED")
            ]);

        var payload = RuntimeIpcProtocol.SerializeStatus(expected);
        var actual = RuntimeIpcProtocol.DeserializeStatus(payload);

        Assert(actual.Dashboard == expected.Dashboard && actual.Outputs.Length == 2,
            "Runtime IPC status must preserve dashboard identity and every configured output target.");
        Assert(actual.Outputs[0] == expected.Outputs[0] && actual.Outputs[1] == expected.Outputs[1],
            "Runtime IPC status must preserve target identity, active state, connection state, and USB state.");
    }

    private static void RuntimeIpcOutputControlIsolation()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"PinkieSysMon-output-ipc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var log = new FileLogger(Path.Combine(tempRoot, "regression.log"));
            var created = new List<FakeOutputSession>();
            using var manager = new OutputSessionManager(
                CreateMultiOutputConfig(),
                new C.DashboardDefinition(),
                new MetricStore(),
                log,
                (app, target, dashboard, store, logger) =>
                {
                    var session = new FakeOutputSession(target.Id, target.Name);
                    created.Add(session);
                    return session;
                });

            manager.Start();
            var control = new RuntimeOutputControl(manager, () => "PinkiePie_Vertical");

            var stopACommand = new RuntimeIpcCommand(RuntimeIpcCommandKind.StopTarget, "screen-a");
            var stopA = control.Execute(stopACommand);
            Assert(stopA.Success &&
                   !control.GetStatus().Outputs.Single(output => output.TargetId == "screen-a").IsActive &&
                   !control.ShouldExitRuntimeAfter(stopACommand),
                "A target-scoped Stop command must unload only the addressed output session and keep the runtime alive while a sibling output remains active.");
            Assert(control.GetStatus().Outputs.Single(output => output.TargetId == "screen-b").IsActive,
                "Stopping one target through IPC control must leave sibling output sessions active.");

            var stopBCommand = new RuntimeIpcCommand(RuntimeIpcCommandKind.StopTarget, "screen-b");
            var stopB = control.Execute(stopBCommand);
            Assert(stopB.Success &&
                   control.GetStatus().Outputs.All(output => !output.IsActive) &&
                   control.ShouldExitRuntimeAfter(stopBCommand),
                "Stopping the last active output must request runtime-process shutdown after the IPC response is delivered.");

            var startAll = control.Execute(new RuntimeIpcCommand(RuntimeIpcCommandKind.StartOrReloadAll));
            Assert(startAll.Success && control.GetStatus().Outputs.All(output => output.IsActive),
                "Start/Reload All must reactivate every configured output after a full stop.");

            var stopAllCommand = new RuntimeIpcCommand(RuntimeIpcCommandKind.StopAll);
            var stopAll = control.Execute(stopAllCommand);
            Assert(stopAll.Success &&
                   control.GetStatus().Outputs.All(output => !output.IsActive) &&
                   control.ShouldExitRuntimeAfter(stopAllCommand),
                "Stop All must unload every configured output session and request runtime-process shutdown.");

            Assert(RuntimeIpcProtocol.ResponseIndicatesRuntimeStopping(
                       new RuntimeIpcResponse(true, $"Outputs stopped. {RuntimeIpcProtocol.RuntimeStoppingMessage}")) &&
                   !RuntimeIpcProtocol.ResponseIndicatesRuntimeStopping(
                       new RuntimeIpcResponse(true, "One output stopped; sibling output remains active.")),
                "The Editor/runtime shutdown handshake must distinguish a response that requires waiting for process exit.");
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    private static void RuntimeIpcShutdownCancellationContract()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"PinkieSysMon-ipc-shutdown-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var log = new FileLogger(Path.Combine(tempRoot, "regression.log"));
            var server = new RuntimeIpcServer(
                static (_, cancellationToken) =>
                    cancellationToken.IsCancellationRequested
                        ? Task.FromCanceled<RuntimeIpcResponse>(cancellationToken)
                        : Task.FromResult(new RuntimeIpcResponse(true, "OK")),
                log);

            server.Dispose();
            server.Dispose();
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void RuntimeOutputStoppedStartupMode()
    {
        Assert(RuntimeStartupOptions.ShouldStartOutputs([]),
            "Normal runtime startup must preserve automatic output-session startup.");
        Assert(RuntimeStartupOptions.ShouldStartOutputs(["--unrelated"]),
            "Unrelated runtime arguments must not suppress output-session startup.");
        Assert(!RuntimeStartupOptions.ShouldStartOutputs([RuntimeStartupOptions.OutputsStoppedArgument]),
            "Editor-launched runtime startup must support an explicit outputs-stopped mode.");
        Assert(!RuntimeStartupOptions.ShouldStartOutputs(["--OUTPUTS-STOPPED"]),
            "The outputs-stopped startup switch must be case-insensitive like the rest of the command surface.");
    }

    private static void ScreenMenuDeviceCommandModel()
    {
        var targets = CreateMultiOutputConfig().Outputs.Targets;

        var runtimeStopped = ScreenMenuContract.Build(targets, false, null, commandInProgress: false);
        Assert(runtimeStopped.Targets.Length == 2 && runtimeStopped.Targets[0].TargetName == "Left Trofeo" && runtimeStopped.Targets[1].TargetName == "Right Trofeo",
            "Output first-level items must be based on configured user-facing output names.");
        Assert(runtimeStopped.Targets.All(target => target.CanStartOrReload && !target.CanStop) &&
               runtimeStopped.CanStartOrReloadAll && !runtimeStopped.CanStopAll,
            "When the runtime is stopped, Start / Reload must remain available while stop actions remain disabled.");
        Assert(runtimeStopped.Targets.All(target => target.StatusText == "Status: Runtime not running"),
            "Output target status must explicitly represent a stopped runtime.");

        var snapshot = new RuntimeStatusSnapshot(
            "PinkiePie_Vertical",
            [
                new RuntimeOutputStatus("screen-a", "Left Trofeo", true, false, "WAITING"),
                new RuntimeOutputStatus("screen-b", "Right Trofeo", false, false, "STOPPED")
            ]);
        var running = ScreenMenuContract.Build(targets, true, snapshot, commandInProgress: false);
        Assert(running.Targets[0].CanStartOrReload && running.Targets[0].CanStop &&
               running.Targets[0].StatusText == "Status: Waiting for device",
            "An active target with no physical connection must remain addressable and visibly report the missing-device state.");
        Assert(running.Targets[1].CanStartOrReload && !running.Targets[1].CanStop &&
               running.Targets[1].StatusText == "Status: Stopped",
            "A stopped target must offer Start / Reload without pretending that a physical session is active.");
        Assert(running.CanStartOrReloadAll && running.CanStopAll,
            "Global Output commands must reflect the aggregate configured-session state.");
        Assert(ScreenMenuContract.IsTargetActive(snapshot, "screen-a") &&
               !ScreenMenuContract.IsTargetActive(snapshot, "screen-b"),
            "Start / Reload planning must distinguish active from stopped target sessions.");
        Assert(!ScreenMenuContract.AreAllTargetsActive(targets, snapshot),
            "Start / Reload All planning must detect when any configured target still needs to be started.");
        var allActiveSnapshot = new RuntimeStatusSnapshot(
            "PinkiePie_Vertical",
            [
                new RuntimeOutputStatus("screen-a", "Left Trofeo", true, true, "CONNECTED"),
                new RuntimeOutputStatus("screen-b", "Right Trofeo", true, true, "CONNECTED")
            ]);
        Assert(ScreenMenuContract.AreAllTargetsActive(targets, allActiveSnapshot),
            "Start / Reload All planning must recognize when configuration reload alone already refreshes every active target.");

        var failureSnapshot = new RuntimeStatusSnapshot(
            "PinkiePie_Vertical",
            [
                new RuntimeOutputStatus("screen-a", "Left Trofeo", true, false, "AMBIGUOUS"),
                new RuntimeOutputStatus("screen-b", "Right Trofeo", true, false, "ERROR")
            ]);
        var failureState = ScreenMenuContract.Build(targets, true, failureSnapshot, commandInProgress: false);
        Assert(failureState.Targets[0].StatusText == "Status: Device selection required" &&
               failureState.Targets[1].StatusText == "Status: Output error",
            "Output target status must distinguish device-selection ambiguity from a generic output connection error.");

        var busy = ScreenMenuContract.Build(targets, true, snapshot, commandInProgress: true);
        Assert(busy.Targets.All(target => !target.CanStartOrReload && !target.CanStop) &&
               !busy.CanStartOrReloadAll && !busy.CanStopAll,
            "A pending Output command must disable every competing per-target and global Output action.");

        Assert(ScreenMenuContract.StartReloadText == "Start / Reload" &&
               ScreenMenuContract.StopText == "Stop / Unload Output" &&
               ScreenMenuContract.StartReloadAllText == "Start / Reload All" &&
               ScreenMenuContract.StopAllText == "Stop / Unload All Outputs",
            "Output command labels must explicitly identify output/session scope while runtime shutdown remains a consequence of the final output stopping.");
    }


    private static void EditorMainMenuGroupCleanup()
    {
        using var menu = new MenuStrip();
        var fileMenu = new ToolStripMenuItem(EditorMainMenuContract.FileMenuText);
        var editMenu = new ToolStripMenuItem(EditorMainMenuContract.EditMenuText);
        var toolsMenu = new ToolStripMenuItem(EditorMainMenuContract.ToolsMenuText);
        var helpMenu = new ToolStripMenuItem(EditorMainMenuContract.HelpMenuText);
        var outputMenu = new ToolStripMenuItem(EditorMainMenuContract.OutputMenuText) { Alignment = ToolStripItemAlignment.Right };

        EditorMainMenuContract.Populate(menu, fileMenu, editMenu, toolsMenu, helpMenu, outputMenu);
        var labels = menu.Items.Cast<ToolStripItem>().Select(item => item.Text).ToArray();
        Assert(labels.SequenceEqual(new[] { "File", "Edit", "Tools", "Help", "Output" }),
            "Editor top-level menu collection must keep Help immediately after Tools and the Output menu at the right; the redundant Group menu must not return.");
        Assert(helpMenu.Alignment == ToolStripItemAlignment.Left && outputMenu.Alignment == ToolStripItemAlignment.Right,
            "Help must participate in the left menu group while Output remains the right-aligned device-control menu.");
        Assert(!labels.Contains("Group", StringComparer.OrdinalIgnoreCase),
            "Editor top-level Group menu must remain removed.");

        var fields = typeof(EditorForm)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Select(field => field.Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert(!fields.Contains("_groupMenu") &&
               !fields.Contains("_groupGroupsMenuItem") &&
               !fields.Contains("_ungroupGroupsMenuItem") &&
               !fields.Contains("_groupMoveUpMenuItem") &&
               !fields.Contains("_groupMoveDownMenuItem") &&
               !fields.Contains("_groupBringToFrontMenuItem") &&
               !fields.Contains("_groupSendToBackMenuItem"),
            "Obsolete top-level Group-menu wiring must be removed rather than merely hidden or disabled.");
        Assert(fields.Contains("_groupSelectedMenuItem") &&
               fields.Contains("_ungroupSelectedMenuItem") &&
               fields.Contains("_treeGroupSelectedMenuItem") &&
               fields.Contains("_treeUngroupSelectedMenuItem") &&
               fields.Contains("_toolbarGroupButton") &&
               fields.Contains("_toolbarUngroupButton"),
            "Removing the duplicate Group menu must preserve grouping commands in Edit, Layers context menu, and toolbar surfaces.");
    }


    private static void EditorWindowChromeHitTestContract()
    {
        using var host = new EditorTitleBarHost { Size = new Size(1200, 32) };
        host.UpdateNativeMetrics(height: 32, leftInset: 44, rightInset: 138);

        Assert(host.MenuBounds == Rectangle.FromLTRB(44, 0, 1062, 32),
            "Native title-bar layout must use Windows App SDK LeftInset/RightInset instead of hand-calculated caption-button geometry.");

        using var icon = (Icon)SystemIcons.Application.Clone();
        host.SetWindowIcon(icon);
        host.ShowWindowIcon = true;
        Assert(!host.WindowIconBounds.IsEmpty && host.MenuBounds.Left > 44,
            "A fully extended title bar must reserve client-title-bar space for the app icon because Windows retains only the caption controls.");

        var centered = EditorWindowChromeContract.CenterVertically(host.MenuBounds, 24);
        Assert(centered.Left == host.MenuBounds.Left && centered.Top == 4 && centered.Height == 24,
            "Title-bar menu content must be centered inside the AppWindowTitleBar-reported height while preserving the icon reservation.");
    }


    private static void EditorTitleBarCaptionVisualExclusion()
    {
        var fields = typeof(EditorForm)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .ToDictionary(field => field.Name, StringComparer.Ordinal);

        Assert(fields.TryGetValue("_appWindow", out var appWindowField) &&
               appWindowField.FieldType.FullName == "Microsoft.UI.Windowing.AppWindow",
            "Editor chrome must be owned by Windows App SDK AppWindow rather than manual Win32 frame emulation.");
        Assert(fields.TryGetValue("_appWindowTitleBar", out var titleBarField) &&
               titleBarField.FieldType.FullName == "Microsoft.UI.Windowing.AppWindowTitleBar",
            "Editor title-bar layout must use AppWindowTitleBar system insets and caption-button ownership.");
        Assert(fields.TryGetValue("_nonClientPointerSource", out var pointerSourceField) &&
               pointerSourceField.FieldType.FullName == "Microsoft.UI.Input.InputNonClientPointerSource",
            "Interactive title-bar menu regions must use the Windows App SDK non-client pointer source.");

        var assembly = typeof(EditorForm).Assembly;
        Assert(assembly.GetType("PinkieSysMon.Editor.EditorWindowChromeNative") is null,
            "The rejected hand-written DWM/WM_NCHITTEST frame implementation must not return.");
    }


    private static void EditorTitleBarMenuIntegration()
    {
        using var form = new Form { ClientSize = new Size(1200, 800) };
        using var host = new EditorTitleBarHost { Size = new Size(1200, 32) };
        using var menu = new EditorTitleBarMenuStrip();
        var fileMenu = new ToolStripMenuItem(EditorMainMenuContract.FileMenuText);
        var editMenu = new ToolStripMenuItem(EditorMainMenuContract.EditMenuText);
        var toolsMenu = new ToolStripMenuItem(EditorMainMenuContract.ToolsMenuText);
        var helpMenu = new ToolStripMenuItem(EditorMainMenuContract.HelpMenuText);
        var outputMenu = new ToolStripMenuItem(EditorMainMenuContract.OutputMenuText) { Alignment = ToolStripItemAlignment.Right };

        EditorMainMenuContract.Populate(menu, fileMenu, editMenu, toolsMenu, helpMenu, outputMenu);
        host.Controls.Add(menu);
        form.Controls.Add(host);
        form.MainMenuStrip = menu;

        host.UpdateNativeMetrics(height: 32, leftInset: 44, rightInset: 138);
        menu.Bounds = EditorWindowChromeContract.CenterVertically(
            host.MenuBounds,
            menu.GetTitleBarPreferredHeight(verticalPadding: 3));
        menu.PerformLayout();

        Assert(ReferenceEquals(menu.Parent, host) && ReferenceEquals(form.MainMenuStrip, menu),
            "The title-bar MenuStrip must live inside the title-bar host while remaining Form.MainMenuStrip for native keyboard routing.");
        Assert(menu.Left >= 44 && menu.Right <= 1062,
            "Title-bar menu content must remain outside Windows App SDK system-reserved left/right inset regions.");

        var passthrough = EditorWindowChromeContract.GetMenuPassthroughRegions(
            menu.Bounds,
            menu.GetInteractiveItemBounds());
        Assert(passthrough.Length == 5,
            "Every visible top-level menu item must have an explicit non-client passthrough region.");

        var leftGroupRight = new[] { fileMenu, editMenu, toolsMenu, helpMenu }.Max(item => menu.Left + item.Bounds.Right);
        var rightGroupLeft = menu.Left + outputMenu.Bounds.Left;
        if (rightGroupLeft - leftGroupRight > 8)
        {
            var dragPoint = new Point(
                leftGroupRight + ((rightGroupLeft - leftGroupRight) / 2),
                menu.Top + Math.Max(0, menu.Height / 2));
            Assert(passthrough.All(region => !region.Contains(dragPoint)),
                "Blank/title space between left and right menu groups must remain outside passthrough regions so Windows owns dragging.");
        }
    }


    private static void EditorTitleBarVerticalAlignment()
    {
        var available = new Rectangle(24, 8, 800, 24);
        var centered = EditorWindowChromeContract.CenterVertically(available, 18);
        Assert(centered.Left == available.Left && centered.Width == available.Width && centered.Height == 18,
            "Title-bar menu centering must preserve its horizontal allocation and preferred height.");
        Assert(Math.Abs((centered.Top - available.Top) - (available.Bottom - centered.Bottom)) <= 1,
            "Title-bar menu content must be vertically centered inside the available caption band.");

        var clamped = EditorWindowChromeContract.CenterVertically(available, 100);
        Assert(clamped == available,
            "A title-bar menu taller than the available caption band must clamp to that band instead of shifting the shell layout.");
    }

    private static void EditorTitleBarMenuThemeContract()
    {
        using var menu = new EditorTitleBarMenuStrip();
        var fileMenu = new ToolStripMenuItem(EditorMainMenuContract.FileMenuText);
        menu.Items.Add(fileMenu);
        var theme = EditorShellTheme.CaptureCurrent();
        menu.ApplyTheme(theme);

        Assert(menu.Renderer is EditorTitleBarMenuRenderer,
            "The title-bar MenuStrip may custom-draw only its client content; the window frame itself remains Windows-owned.");
        Assert(menu.BackColor == Color.Transparent &&
               menu.ForeColor.ToArgb() == theme.Foreground.ToArgb() &&
               fileMenu.ForeColor.ToArgb() == theme.Foreground.ToArgb(),
            "Title-bar menu background must remain transparent while labels use the shared shell foreground palette.");
        Assert(theme.IsDark ? theme.Foreground.GetBrightness() > theme.Background.GetBrightness() : true,
            "Dark shell custom content must use a foreground brighter than its background.");
    }


    private static void EditorThemeSettingsContract()
    {
        Assert(EditorThemeContract.Parse(null) == EditorThemeMode.System &&
               EditorThemeContract.Parse("") == EditorThemeMode.System &&
               EditorThemeContract.Parse("System") == EditorThemeMode.System,
            "Missing/invalid legacy editor theme settings must default to System mode.");
        Assert(EditorThemeContract.Parse("Light") == EditorThemeMode.Light &&
               EditorThemeContract.Parse("dark") == EditorThemeMode.Dark,
            "Editor theme settings must accept the persisted System/Light/Dark contract case-insensitively.");
        Assert(EditorThemeContract.ToSystemColorMode(EditorThemeMode.System) == SystemColorMode.System &&
               EditorThemeContract.ToSystemColorMode(EditorThemeMode.Light) == SystemColorMode.Classic &&
               EditorThemeContract.ToSystemColorMode(EditorThemeMode.Dark) == SystemColorMode.Dark,
            "Editor theme modes must map directly to the supported WinForms .NET 10 color-mode API.");

        var propertyTabsField = typeof(EditorForm)
            .GetField("_propertyTabs", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert(propertyTabsField?.FieldType == typeof(EditorThemedTabControl),
            "Property tabs must opt into WinForms implicit theming through CreateParams rather than private control internals or ad-hoc recoloring.");

        using var propertyGrid = new PropertyGrid();
        var currentTheme = EditorShellTheme.CaptureCurrent();
        EditorPropertyGridTheme.Apply(propertyGrid, currentTheme with { IsDark = true });
        Assert(!propertyGrid.CanShowVisualStyleGlyphs,
            "Dark PropertyGrid must use the supported classic expansion-glyph path so +/- controls remain visible.");
        EditorPropertyGridTheme.Apply(propertyGrid, currentTheme with { IsDark = false });
        Assert(propertyGrid.CanShowVisualStyleGlyphs,
            "Light PropertyGrid must retain the normal OS visual-style expansion glyphs.");

        var tempRoot = Path.Combine(Path.GetTempPath(), $"PinkieSysMon-editor-theme-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var path = Path.Combine(tempRoot, "editor.json");
            var settings = new EditorSettings { Theme = EditorThemeContract.DarkSetting };
            settings.Save(path);
            var loaded = EditorSettings.Load(path);
            Assert(loaded.Theme == EditorThemeContract.DarkSetting,
                "Editor theme choice must persist in editor.json independently of dashboard.json semantics.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch { }
        }
    }

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
        var toolsMenu = new ToolStripMenuItem(EditorMainMenuContract.ToolsMenuText);
        var helpMenu = new ToolStripMenuItem(EditorMainMenuContract.HelpMenuText);
        var outputMenu = new ToolStripMenuItem(EditorMainMenuContract.OutputMenuText) { Alignment = ToolStripItemAlignment.Right };
        EditorMainMenuContract.Populate(menu, fileMenu, editMenu, toolsMenu, helpMenu, outputMenu);

        Assert(menu.Items.Cast<ToolStripItem>().All(item => !string.Equals(item.Text, "Group", StringComparison.OrdinalIgnoreCase)),
            "PASS 8 final audit must keep the redundant top-level Group menu removed.");
        Assert(fileMenu.Alignment == ToolStripItemAlignment.Left &&
               editMenu.Alignment == ToolStripItemAlignment.Left &&
               toolsMenu.Alignment == ToolStripItemAlignment.Left &&
               helpMenu.Alignment == ToolStripItemAlignment.Left &&
               outputMenu.Alignment == ToolStripItemAlignment.Right,
            "Final shell contract must keep File/Edit/Tools/Help left and Output right in the integrated top chrome.");
    }

    private static AppConfig CreateMultiOutputConfig() => new()
    {
        Outputs = new OutputConfig
        {
            Targets =
            [
                new OutputTargetConfig
                {
                    Id = "screen-a",
                    Name = "Left Trofeo",
                    DeviceId = "trofeo:" + new string('a', 64)
                },
                new OutputTargetConfig
                {
                    Id = "screen-b",
                    Name = "Right Trofeo",
                    DeviceId = "trofeo:" + new string('b', 64)
                }
            ]
        }
    };

    private static void TelemetryProviderFailureIsIsolated()
    {
        const string badMetric = "test.bad.value";
        const string goodMetric = "test.good.value";

        var tempRoot = Path.Combine(Path.GetTempPath(), $"PinkieSysMon-regression-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var log = new FileLogger(Path.Combine(tempRoot, "regression.log"));
            var store = new MetricStore();
            IMetricSource[] sources =
            [
                new ThrowingMetricSource("test.bad", "bad-source", badMetric),
                new ConstantMetricSource("test.good", "good-source", goodMetric, 42d)
            ];
            var config = new TelemetryConfig
            {
                Metrics = new Dictionary<string, MetricPollingConfig>(StringComparer.OrdinalIgnoreCase)
                {
                    [badMetric] = new() { Enabled = true, IntervalMs = 100 },
                    [goodMetric] = new() { Enabled = true, IntervalMs = 100 }
                }
            };

            using var engine = new TelemetryEngine(
                store,
                sources,
                config,
                new[] { "test.bad", "test.good" },
                new[] { badMetric, goodMetric },
                log);
            engine.Start();

            var published = SpinWait.SpinUntil(
                () => store.Snapshot().ContainsKey(goodMetric),
                TimeSpan.FromSeconds(2));
            Assert(published, "Healthy provider was never published after the preceding provider threw.");

            var snapshot = store.Snapshot();
            Assert(snapshot.TryGetValue(goodMetric, out var good) && Convert.ToDouble(good) == 42d,
                "Healthy provider value was not preserved.");
            Assert(snapshot.TryGetValue(badMetric, out var bad) && bad is null,
                "Failed provider metric must be explicitly published as unavailable.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void TelemetryReconfigurationClearsDeactivatedMetrics()
    {
        const string metric = "test.reconfigure.value";

        var tempRoot = Path.Combine(Path.GetTempPath(), $"PinkieSysMon-regression-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var log = new FileLogger(Path.Combine(tempRoot, "regression.log"));
            var store = new MetricStore();
            IMetricSource[] sources =
            [
                new ConstantMetricSource("test.reconfigure", "reconfigure-source", metric, 42d)
            ];
            var enabledConfig = new TelemetryConfig
            {
                Metrics = new Dictionary<string, MetricPollingConfig>(StringComparer.OrdinalIgnoreCase)
                {
                    [metric] = new() { Enabled = true, IntervalMs = 100 }
                }
            };
            var disabledConfig = new TelemetryConfig
            {
                Metrics = new Dictionary<string, MetricPollingConfig>(StringComparer.OrdinalIgnoreCase)
                {
                    [metric] = new() { Enabled = false, IntervalMs = 100 }
                }
            };

            using var engine = new TelemetryEngine(
                store,
                sources,
                enabledConfig,
                new[] { "test.reconfigure" },
                new[] { metric },
                log);
            engine.Start();

            var published = SpinWait.SpinUntil(
                () => store.Snapshot().TryGetValue(metric, out var value) && value is not null,
                TimeSpan.FromSeconds(2));
            Assert(published, "Telemetry metric was never published before the reconfiguration test.");

            engine.Reconfigure(
                disabledConfig,
                new[] { "test.reconfigure" },
                new[] { metric });
            Assert(store.Snapshot().TryGetValue(metric, out var disabledValue) && disabledValue is null,
                "A metric disabled by telemetry configuration must be published as unavailable immediately.");

            engine.Reconfigure(
                enabledConfig,
                new[] { "test.reconfigure" },
                new[] { metric });
            var republished = SpinWait.SpinUntil(
                () => store.Snapshot().TryGetValue(metric, out var value) && value is not null,
                TimeSpan.FromSeconds(2));
            Assert(republished, "Telemetry metric did not resume publishing after being re-enabled.");

            engine.Reconfigure(
                enabledConfig,
                new[] { "test.reconfigure" },
                Array.Empty<string>());
            Assert(store.Snapshot().TryGetValue(metric, out var noLongerRequiredValue) && noLongerRequiredValue is null,
                "A metric removed from dashboard demand must be published as unavailable immediately.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }
    private static void TelemetryReconfigurationPrepareCommitIsolation()
    {
        const string metric = "test.reconfigure.prepare";

        var tempRoot = Path.Combine(Path.GetTempPath(), $"PinkieSysMon-regression-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var log = new FileLogger(Path.Combine(tempRoot, "regression.log"));
            var store = new MetricStore();
            IMetricSource[] sources =
            [
                new ConstantMetricSource("test.reconfigure.prepare", "prepare-source", metric, 42d)
            ];
            var enabledConfig = new TelemetryConfig
            {
                Metrics = new Dictionary<string, MetricPollingConfig>(StringComparer.OrdinalIgnoreCase)
                {
                    [metric] = new() { Enabled = true, IntervalMs = 100 }
                }
            };
            var disabledConfig = new TelemetryConfig
            {
                Metrics = new Dictionary<string, MetricPollingConfig>(StringComparer.OrdinalIgnoreCase)
                {
                    [metric] = new() { Enabled = false, IntervalMs = 100 }
                }
            };

            var engine = new TelemetryEngine(
                store,
                sources,
                enabledConfig,
                new[] { "test.reconfigure.prepare" },
                new[] { metric },
                log);
            try
            {
                store.Publish(metric, 42d);
                var plan = engine.PrepareReconfiguration(
                    disabledConfig,
                    new[] { "test.reconfigure.prepare" },
                    new[] { metric });

                Assert(store.Snapshot().TryGetValue(metric, out var beforeCommit) &&
                       Convert.ToDouble(beforeCommit, CultureInfo.InvariantCulture) == 42d,
                    "Preparing telemetry reconfiguration must not mutate the live metric store or active schedule.");

                engine.CommitReconfiguration(plan);
                Assert(store.Snapshot().TryGetValue(metric, out var afterCommit) && afterCommit is null,
                    "Committing a prepared telemetry reconfiguration must apply deactivation semantics.");

                engine.Dispose();
                engine.Dispose();
            }
            finally
            {
                engine.Dispose();
            }
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
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
        Assert(RuntimeNotificationContract.GetTitle(RuntimeNotificationKind.Information) == "PinkieSysMon",
            "Informational app notifications must use the canonical PinkieSysMon identity.");
        Assert(RuntimeNotificationContract.GetTitle(RuntimeNotificationKind.Warning) == "PinkieSysMon warning" &&
               RuntimeNotificationContract.GetTitle(RuntimeNotificationKind.Error) == "PinkieSysMon error",
            "Warning and error app notifications must preserve explicit severity in the native notification title.");

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

    private static void AssertApplicationIdentity(
        System.Reflection.Assembly assembly,
        string expectedTitle,
        string expectedDescription)
    {
        static string? AttributeValue<TAttribute>(
            System.Reflection.Assembly target,
            Func<TAttribute, string?> selector)
            where TAttribute : Attribute
        {
            var attribute = target.GetCustomAttributes(typeof(TAttribute), inherit: false)
                .OfType<TAttribute>()
                .SingleOrDefault();
            return attribute is null ? null : selector(attribute);
        }

        Assert(AttributeValue<System.Reflection.AssemblyCompanyAttribute>(assembly, a => a.Company) == "PinkieSysMon Project",
            $"{assembly.GetName().Name} must expose the normalized PinkieSysMon company identity.");
        Assert(AttributeValue<System.Reflection.AssemblyProductAttribute>(assembly, a => a.Product) == "PinkieSysMon",
            $"{assembly.GetName().Name} must expose the normalized PinkieSysMon product identity.");
        Assert(AttributeValue<System.Reflection.AssemblyTitleAttribute>(assembly, a => a.Title) == expectedTitle,
            $"{assembly.GetName().Name} must expose its expected Windows title metadata.");
        Assert(AttributeValue<System.Reflection.AssemblyDescriptionAttribute>(assembly, a => a.Description) == expectedDescription,
            $"{assembly.GetName().Name} must expose its expected Windows description metadata.");
        Assert(AttributeValue<System.Reflection.AssemblyInformationalVersionAttribute>(assembly, a => a.InformationalVersion) == "1.19.0",
            $"{assembly.GetName().Name} must expose the normalized informational version.");

        var fileInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location);
        Assert(fileInfo.CompanyName == "PinkieSysMon Project",
            $"{assembly.GetName().Name} Win32 version resources must contain CompanyName.");
        Assert(fileInfo.ProductName == "PinkieSysMon",
            $"{assembly.GetName().Name} Win32 version resources must contain ProductName.");
        Assert(fileInfo.FileDescription == expectedTitle,
            $"{assembly.GetName().Name} Win32 version resources must use the assembly title as FileDescription.");
        Assert(fileInfo.FileVersion == "1.19.0.0",
            $"{assembly.GetName().Name} Win32 version resources must expose FileVersion 1.19.0.0.");
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

    private static void WindowsInternetConnectivityMetric()
    {
        var descriptor = MetricContract.GetDescriptor(NetworkMetricContract.InternetConnected);
        Assert(descriptor is not null, "Internet connectivity metric descriptor is missing.");
        Assert(descriptor!.ValueKind == MetricValueKind.Boolean,
            "Internet connectivity metric must be boolean.");

        var tempRoot = Path.Combine(Path.GetTempPath(), $"PinkieSysMon-network-regression-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var log = new FileLogger(Path.Combine(tempRoot, "regression.log"));

            var connectedSource = new WindowsNetworkTelemetrySource(log, () => true);
            Assert(connectedSource.DefaultIntervalMs == 5000,
                "Internet connectivity metric must use the 5 second polling baseline.");
            var connected = connectedSource.Capture([NetworkMetricContract.InternetConnected]);
            Assert(connected.TryGetValue(NetworkMetricContract.InternetConnected, out var connectedValue) &&
                   connectedValue is bool connectedBool && connectedBool,
                "Connected state was not published as true.");

            var disconnectedSource = new WindowsNetworkTelemetrySource(log, () => false);
            var disconnected = disconnectedSource.Capture([NetworkMetricContract.InternetConnected]);
            Assert(disconnected.TryGetValue(NetworkMetricContract.InternetConnected, out var disconnectedValue) &&
                   disconnectedValue is bool disconnectedBool && !disconnectedBool,
                "Disconnected state was not published as false.");

            var failingSource = new WindowsNetworkTelemetrySource(log, () => throw new InvalidOperationException("probe failed"));
            var unavailable = failingSource.Capture([NetworkMetricContract.InternetConnected]);
            Assert(unavailable.TryGetValue(NetworkMetricContract.InternetConnected, out var unavailableValue) &&
                   unavailableValue is null,
                "Network probe failure must publish the metric as unavailable.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }


    private static void BinaryNumericTruthSemantics()
    {
        Assert(BinarySignalContract.TryResolve(0d, out var zero) && !zero, "Numeric zero must be false.");
        Assert(BinarySignalContract.TryResolve(1d, out var one) && one, "Numeric one must be true.");
        Assert(BinarySignalContract.TryResolve(1175d, out var rpm) && rpm, "Any finite non-zero number must be true.");
        Assert(BinarySignalContract.TryResolve(-1d, out var negative) && negative, "Negative non-zero numbers must be true.");
        Assert(!BinarySignalContract.TryResolve(double.NaN, out _), "NaN must be unavailable.");
        Assert(!BinarySignalContract.TryResolve(double.PositiveInfinity, out _), "Infinity must be unavailable.");
    }

    private static void BinarySetpointSemantics()
    {
        Assert(BinarySignalContract.TryResolve(
                81d,
                BinarySignalContract.EvaluationModeSetpoint,
                "80",
                BinarySignalContract.TrueIfGreaterThan,
                out var above) && above,
            "Numeric setpoint comparison > failed.");
        Assert(BinarySignalContract.TryResolve(
                80d,
                BinarySignalContract.EvaluationModeSetpoint,
                "80",
                BinarySignalContract.TrueIfGreaterThanOrEqual,
                out var equalInclusive) && equalInclusive,
            "Numeric setpoint comparison >= failed at equality.");
        Assert(BinarySignalContract.TryResolve(
                80d,
                BinarySignalContract.EvaluationModeSetpoint,
                "80",
                BinarySignalContract.TrueIfEqual,
                out var equal) && equal,
            "Numeric setpoint comparison = failed.");
        Assert(BinarySignalContract.TryResolve(
                79d,
                BinarySignalContract.EvaluationModeSetpoint,
                "80",
                BinarySignalContract.TrueIfGreaterThan,
                out var below) && !below,
            "Numeric false setpoint result failed.");

        Assert(BinarySignalContract.TryResolve(
                "READY",
                BinarySignalContract.EvaluationModeSetpoint,
                "READY",
                BinarySignalContract.TrueIfGreaterThan,
                out var exactText) && exactText,
            "String setpoint exact comparison failed.");
        Assert(BinarySignalContract.TryResolve(
                "ready",
                BinarySignalContract.EvaluationModeSetpoint,
                "READY",
                BinarySignalContract.TrueIfEqual,
                out var caseSensitiveText) && !caseSensitiveText,
            "String setpoint comparison must be case-sensitive.");
        Assert(BinarySignalContract.TryResolve(
                string.Empty,
                BinarySignalContract.EvaluationModeSetpoint,
                string.Empty,
                BinarySignalContract.TrueIfEqual,
                out var emptyText) && emptyText,
            "Empty string must remain a valid setpoint value.");

        Assert(BinarySignalContract.TryResolve(
                true,
                BinarySignalContract.EvaluationModeSetpoint,
                "1",
                BinarySignalContract.TrueIfEqual,
                out var trueAsOne) && trueAsOne,
            "Boolean setpoint '1' must mean expected true.");
        Assert(BinarySignalContract.TryResolve(
                false,
                BinarySignalContract.EvaluationModeSetpoint,
                "True",
                BinarySignalContract.TrueIfEqual,
                out var falseVsTrue) && !falseVsTrue,
            "Boolean setpoint must compare the metric value against the expected boolean.");
        Assert(BinarySignalContract.TryResolve(
                false,
                BinarySignalContract.EvaluationModeSetpoint,
                "anything else",
                BinarySignalContract.TrueIfEqual,
                out var falseAsDefault) && falseAsDefault,
            "Boolean setpoint other than True/1 must mean expected false.");

        Assert(!BinarySignalContract.TryResolve(
                null,
                BinarySignalContract.EvaluationModeSetpoint,
                "80",
                BinarySignalContract.TrueIfGreaterThan,
                out _),
            "Null metric must be unavailable.");
        Assert(!BinarySignalContract.TryResolve(
                "undefined",
                BinarySignalContract.EvaluationModeSetpoint,
                "undefined",
                BinarySignalContract.TrueIfEqual,
                out _),
            "Undefined metric text must be unavailable, not a string match.");
        Assert(!BinarySignalContract.TryResolve(
                double.NaN,
                BinarySignalContract.EvaluationModeSetpoint,
                "80",
                BinarySignalContract.TrueIfGreaterThan,
                out _),
            "NaN metric must be unavailable in setpoint mode.");
        Assert(!BinarySignalContract.TryResolve(
                80d,
                BinarySignalContract.EvaluationModeSetpoint,
                "not-a-number",
                BinarySignalContract.TrueIfGreaterThan,
                out _),
            "Invalid numeric setpoint must produce unavailable state.");
    }

    private static void StateImageColorFidelity()
    {
        const string metric = "test.binary.state-image";
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-state-image-regression");
        try
        {
            const string source = "state-image.png";
            WriteSolidTestImage(Path.Combine(tempRoot, source), SkiaSharp.SKColors.Red, width: 40, height: 10);

            var widget = new WidgetDefinition
            {
                Id = "binary-state-image",
                Type = WidgetTypeContract.Binary,
                X = 0,
                Y = 0,
                Width = 20,
                Height = 20,
                Metric = metric,
                // Deliberately contradict the state profile. Schema 11 Binary rendering must
                // ignore the legacy widget-level Image settings.
                Fit = "stretch",
                Loop = true
            };
            widget.EnsureStateVisualProfiles();
            var trueProfile = widget.Profiles![BinarySignalContract.TrueKey];
            trueProfile.SourceType = StateVisualProfileContract.SourceFile;
            trueProfile.Source = source;
            trueProfile.Color = "#FF00FF00";
            trueProfile.Opacity = 1f;
            trueProfile.Fit = StateVisualProfileContract.FitContain;
            trueProfile.Loop = false;

            var falseProfile = widget.Profiles[BinarySignalContract.FalseKey];
            falseProfile.SourceType = StateVisualProfileContract.SourceFile;
            falseProfile.Source = source;
            falseProfile.Opacity = 1f;
            falseProfile.Fit = StateVisualProfileContract.FitStretch;
            falseProfile.Loop = true;

            using var images = new ImageAssetCache(tempRoot);
            images.Preload(new[] { CurrentWidget(widget) });
            using var icons = new IconAssetCache(tempRoot);
            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, new[] { CurrentWidget(widget) });
            using var renderer = new PinkieSysMon.Widgets.BinaryWidgetRenderer(images, icons);

            using (var trueContext = new PinkieSysMon.Widgets.WidgetRenderContext(
                       new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = true },
                       antialias: false,
                       typefaces))
            using (var trueSurface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(20, 20))
                       ?? throw new InvalidOperationException("Could not create state-image regression surface."))
            {
                trueSurface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
                PinkieSysMon.Widgets.WidgetTransform.Render(trueSurface.Canvas, CurrentWidget(widget), trueContext, renderer);
                trueSurface.Canvas.Flush();

                using var pixels = trueSurface.PeekPixels()
                    ?? throw new InvalidOperationException("Could not inspect true state-image regression pixels.");
                var center = pixels.GetPixelColor(10, 10);
                var top = pixels.GetPixelColor(10, 2);
                Assert(center.Red > 240 && center.Green < 20 && center.Blue < 20,
                    "File-backed state visuals must preserve source image colors instead of applying the icon color.");
                Assert(top.Alpha < 10,
                    "Binary True state must use its profile Fit=contain rather than the legacy widget-level Fit=stretch.");
            }

            using (var falseContext = new PinkieSysMon.Widgets.WidgetRenderContext(
                       new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = false },
                       antialias: false,
                       typefaces))
            using (var falseSurface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(20, 20))
                       ?? throw new InvalidOperationException("Could not create false state-image regression surface."))
            {
                falseSurface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
                PinkieSysMon.Widgets.WidgetTransform.Render(falseSurface.Canvas, CurrentWidget(widget), falseContext, renderer);
                falseSurface.Canvas.Flush();

                using var pixels = falseSurface.PeekPixels()
                    ?? throw new InvalidOperationException("Could not inspect false state-image regression pixels.");
                var top = pixels.GetPixelColor(10, 2);
                Assert(top.Red > 240 && top.Alpha > 240,
                    "Binary False state must independently use its own profile Fit=stretch.");
            }
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void BinaryStateAnimationPlayback()
    {
        var widget = new WidgetDefinition
        {
            Id = "binary-animation-playback",
            Type = WidgetTypeContract.Binary,
            X = 0,
            Y = 0,
            Width = 8,
            Height = 8,
            Metric = "test.binary.animation"
        };
        widget.EnsureStateVisualProfiles();

        var canonicalWidget = PropertyModelNormalizationMigration.MapLegacyWidget(widget);
        var tracker = new PinkieSysMon.Widgets.StateAnimationPlaybackTracker();
        Assert(tracker.GetElapsedMs(canonicalWidget, BinarySignalContract.TrueKey, "same.gif", 1000) == 0,
            "Binary file playback must start at elapsed zero on first activation.");
        Assert(tracker.GetElapsedMs(canonicalWidget, BinarySignalContract.TrueKey, "same.gif", 1125) == 125,
            "Binary file playback must advance relative to the current state activation.");
        Assert(tracker.GetElapsedMs(canonicalWidget, BinarySignalContract.FalseKey, "same.gif", 1200) == 0,
            "Changing Binary state must restart playback even when both states use the same asset.");
        Assert(tracker.GetElapsedMs(canonicalWidget, BinarySignalContract.FalseKey, "same.gif", 1260) == 60,
            "Binary False state playback must advance from its own activation time.");
        Assert(tracker.GetElapsedMs(canonicalWidget, BinarySignalContract.TrueKey, "same.gif", 1300) == 0,
            "Re-entering Binary True state must restart playback from zero.");
        tracker.Deactivate(canonicalWidget);
        Assert(tracker.GetElapsedMs(canonicalWidget, BinarySignalContract.TrueKey, "same.gif", 1500) == 0,
            "Deactivating a Binary visual must clear its playback activation.");
        Assert(tracker.GetElapsedMs(canonicalWidget, BinarySignalContract.TrueKey, "other.gif", 1510) == 0,
            "Changing the active Binary file asset must restart playback.");

        int[] frameEnds = [100, 200];
        Assert(ImageAsset.ResolveFrameIndex(frameEnds, 200, 0, loop: false) == 0,
            "Animation elapsed zero must select the first frame.");
        Assert(ImageAsset.ResolveFrameIndex(frameEnds, 200, 99, loop: false) == 0,
            "Animation must retain the first frame through its declared duration.");
        Assert(ImageAsset.ResolveFrameIndex(frameEnds, 200, 100, loop: false) == 1,
            "Animation must advance to the second frame at the first frame boundary.");
        Assert(ImageAsset.ResolveFrameIndex(frameEnds, 200, 500, loop: false) == 1,
            "Non-looping animation must remain on its final frame after completion.");
        Assert(ImageAsset.ResolveFrameIndex(frameEnds, 200, 200, loop: true) == 0,
            "Looping animation must wrap to the first frame at the total-duration boundary.");

        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-binary-animation-regression");
        try
        {
            const string source = "binary-animation.gif";
            File.WriteAllBytes(
                Path.Combine(tempRoot, source),
                Convert.FromBase64String(
                    "R0lGODlhAgACAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQICgAAACwAAAAAAgACAAAIBgABCAQQEAAh+QQICgAAACwAAAAAAgACAIEAAP8AAAAAAAAAAAAIBgABCAQQEAA7"));

            var trueProfile = widget.Profiles![BinarySignalContract.TrueKey];
            trueProfile.SourceType = StateVisualProfileContract.SourceFile;
            trueProfile.Source = source;
            trueProfile.Fit = StateVisualProfileContract.FitStretch;
            trueProfile.Loop = false;
            trueProfile.Opacity = 1f;

            var falseProfile = widget.Profiles[BinarySignalContract.FalseKey];
            falseProfile.SourceType = StateVisualProfileContract.SourceFile;
            falseProfile.Source = source;
            falseProfile.Fit = StateVisualProfileContract.FitStretch;
            falseProfile.Loop = true;
            falseProfile.Opacity = 1f;

            var currentWidget = (C.BinaryWidgetDefinition)CurrentWidget(widget);
            long nowMs = 10_000;
            using var images = new ImageAssetCache(tempRoot);
            images.Preload(new[] { currentWidget });
            using var icons = new IconAssetCache(tempRoot);
            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, new[] { currentWidget });
            using var renderer = new PinkieSysMon.Widgets.BinaryWidgetRenderer(images, icons, () => nowMs);
            using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    [widget.Metric!] = true
                },
                antialias: false,
                typefaces);
            using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(8, 8))
                ?? throw new InvalidOperationException("Could not create Binary animation regression surface.");

            SkiaSharp.SKColor RenderCenter(object? metricValue)
            {
                context.UpdateMetrics(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    [widget.Metric!] = metricValue
                });
                surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
                renderer.Render(surface.Canvas, currentWidget, context);
                surface.Canvas.Flush();
                using var pixels = surface.PeekPixels()
                    ?? throw new InvalidOperationException("Could not inspect Binary animation regression pixels.");
                return pixels.GetPixelColor(4, 4);
            }

            static bool IsRed(SkiaSharp.SKColor color) =>
                color.Red > 240 && color.Green < 20 && color.Blue < 20 && color.Alpha > 240;
            static bool IsBlue(SkiaSharp.SKColor color) =>
                color.Blue > 240 && color.Red < 20 && color.Green < 20 && color.Alpha > 240;

            Assert(IsRed(RenderCenter(true)),
                "Entering a file-backed Binary True state must render animation frame zero.");

            nowMs = 10_150;
            Assert(IsBlue(RenderCenter(true)),
                "Binary True playback must advance from its activation-relative clock.");

            nowMs = 10_500;
            Assert(IsBlue(RenderCenter(true)),
                "Binary True Loop=false playback must stop on the final animation frame.");

            nowMs = 10_510;
            Assert(IsRed(RenderCenter(false)),
                "Switching to Binary False must restart the shared animated asset at frame zero.");

            nowMs = 10_660;
            Assert(IsBlue(RenderCenter(false)),
                "Binary False playback must advance independently after activation.");

            nowMs = 10_710;
            Assert(IsRed(RenderCenter(false)),
                "Binary False Loop=true playback must wrap after the animation duration.");

            nowMs = 10_720;
            Assert(IsRed(RenderCenter(true)),
                "Re-entering Binary True must restart its animation at frame zero.");

            nowMs = 10_730;
            _ = RenderCenter(null);
            nowMs = 10_900;
            Assert(IsRed(RenderCenter(true)),
                "Unavailable Binary state must clear playback so the next activation restarts at frame zero.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void StateImagePropertyVisibility()
    {
        var widget = new C.BinaryWidgetDefinition
        {
            Id = "binary-state-image-properties",
            Metric = RuntimeMetricContract.Fps,
            Width = 96f,
            Height = 96f
        };
        TextOverflowStateContract.EnsureProfiles(widget);

        static string StateProperty(string key, StateVisualProfileField field) =>
            $"{StateVisualProfilePropertyDescriptor.PropertyPrefix}{key}:{field}";

        var trueColorName = StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.Color);
        var falseColorName = StateProperty(BinarySignalContract.FalseKey, StateVisualProfileField.Color);
        var trueFitName = StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.Fit);
        var trueLoopName = StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.Loop);

        var iconProperties = new EditorPropertyView(widget, section: EditorPropertySection.Image).GetProperties();
        Assert(GetNestedProperty(iconProperties, "State: True", trueColorName) is not null &&
               GetNestedProperty(iconProperties, "State: False", falseColorName) is not null &&
               GetNestedProperty(iconProperties, "State: True", trueFitName) is not null &&
               GetNestedProperty(iconProperties, "State: True", trueLoopName) is not null,
            "Icon-backed Binary state visuals must keep Color/Fit/Loop rows available for contextual capability state.");

        var trueProfile = widget.Profiles[BinarySignalContract.TrueKey];
        trueProfile.Asset.ChangeSourceType(C.ImageAssetSourceType.File);
        trueProfile.Asset.Source = "state.png";
        var fileProperties = new EditorPropertyView(widget, section: EditorPropertySection.Image).GetProperties();
        var fileColor = GetNestedProperty(fileProperties, "State: True", trueColorName);
        Assert(fileColor is { IsReadOnly: true } &&
               GetNestedProperty(fileProperties, "State: True", trueFitName) is not null &&
               GetNestedProperty(fileProperties, "State: True", trueLoopName) is not null,
            "File-backed Binary state visuals must keep Fit/Loop while Color remains visible but disabled by Source Type.");
        Assert(GetNestedProperty(fileProperties, "State: False", falseColorName) is not null,
            "Changing one state to a file source must not remove the sibling state's Color row.");

        trueProfile.ContentType = C.StateContentType.Value;
        var valueImageProperties = new EditorPropertyView(widget, section: EditorPropertySection.Image).GetProperties();
        var valueTextProperties = new EditorPropertyView(widget, section: EditorPropertySection.Text).GetProperties();
        Assert(valueImageProperties["State: True"] is null &&
               GetNestedProperty(valueTextProperties, "State: True", "Foreground", trueColorName) is not null,
            "Value-backed Binary states must move Color to Text presentation and omit image-only presentation rows.");
    }

    private static void BinaryValueStateVisual()
    {
        const string metric = RuntimeMetricContract.Fps;
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-binary-value-regression");
        try
        {
            var widget = new WidgetDefinition
            {
                Id = "binary-value",
                Type = WidgetTypeContract.Binary,
                X = 2,
                Y = 30,
                Width = 116,
                Height = 40,
                Metric = metric,
                FontSize = 24,
                Align = "left",
                VerticalAlign = "baseline",
                OverflowMode = ValueOverflowContract.Clip
            };
            widget.EnsureStateVisualProfiles();

            var trueProfile = widget.Profiles![BinarySignalContract.TrueKey];
            trueProfile.SourceType = StateVisualProfileContract.SourceValue;
            trueProfile.Source = "must-not-be-loaded.png";
            trueProfile.Color = "#FFFF0000";
            trueProfile.Opacity = 1f;

            var falseProfile = widget.Profiles![BinarySignalContract.FalseKey];
            falseProfile.SourceType = StateVisualProfileContract.SourceValue;
            falseProfile.Source = "must-not-be-loaded-either.png";
            falseProfile.Color = "#FF0000FF";
            falseProfile.Opacity = 0.5f;

            foreach (var text in new[] { trueProfile.Text!, falseProfile.Text! })
            {
                text.FontSize = 24f;
                text.Align = "left";
                text.VerticalAlign = "baseline";
                text.OverflowMode = ValueOverflowContract.Clip;
            }

            using var images = new ImageAssetCache(tempRoot);
            images.Preload(new[] { CurrentWidget(widget) });
            using var icons = new IconAssetCache(tempRoot);
            icons.Preload(new[] { CurrentWidget(widget) });
            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, new[] { CurrentWidget(widget) });
            using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = 42d },
                antialias: false,
                typefaces);
            using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(120, 80))
                ?? throw new InvalidOperationException("Could not create Binary value regression surface.");
            using var renderer = new PinkieSysMon.Widgets.BinaryWidgetRenderer(images, icons);

            surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
            PinkieSysMon.Widgets.WidgetTransform.Render(surface.Canvas, CurrentWidget(widget), context, renderer);
            surface.Canvas.Flush();
            using (var pixels = surface.PeekPixels()
                   ?? throw new InvalidOperationException("Could not inspect Binary true-value pixels."))
            {
                var sawRed = false;
                for (var y = 0; y < pixels.Height && !sawRed; y++)
                {
                    for (var x = 0; x < pixels.Width; x++)
                    {
                        var color = pixels.GetPixelColor(x, y);
                        if (color.Alpha > 0 && color.Red > 150 && color.Green < 100 && color.Blue < 100)
                        {
                            sawRed = true;
                            break;
                        }
                    }
                }

                Assert(sawRed, "Binary value visual did not render the metric text with the True-state color.");
            }

            context.UpdateMetrics(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = 0d });
            surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
            PinkieSysMon.Widgets.WidgetTransform.Render(surface.Canvas, CurrentWidget(widget), context, renderer);
            surface.Canvas.Flush();
            using (var pixels = surface.PeekPixels()
                   ?? throw new InvalidOperationException("Could not inspect Binary false-value pixels."))
            {
                var sawHalfBlue = false;
                for (var y = 0; y < pixels.Height && !sawHalfBlue; y++)
                {
                    for (var x = 0; x < pixels.Width; x++)
                    {
                        var color = pixels.GetPixelColor(x, y);
                        if (color.Blue > 150 && color.Red < 100 && color.Green < 100 &&
                            color.Alpha is >= 100 and <= 160)
                        {
                            sawHalfBlue = true;
                            break;
                        }
                    }
                }

                Assert(sawHalfBlue,
                    "Binary value visual did not apply the False-state color and opacity after a state transition.");
            }
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void BinaryValueContainerLayout()
    {
        const string metric = RuntimeMetricContract.Fps;
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-binary-value-layout-regression");
        try
        {
            var widget = new WidgetDefinition
            {
                Id = "binary-value-layout",
                Type = WidgetTypeContract.Binary,
                X = 10,
                Y = 20,
                Width = 80,
                Height = 40,
                Metric = metric,
                FontSize = 24,
                Align = "center",
                VerticalAlign = "middle",
                OverflowMode = ValueOverflowContract.Clip
            };
            widget.EnsureStateVisualProfiles();
            widget.Profiles![BinarySignalContract.TrueKey].SourceType = StateVisualProfileContract.SourceValue;
            widget.Profiles![BinarySignalContract.FalseKey].SourceType = StateVisualProfileContract.SourceIcon;
            var trueText = GetBinaryText(widget, BinarySignalContract.TrueKey);
            trueText.FontSize = 24f;
            trueText.Align = "center";
            trueText.VerticalAlign = "middle";
            trueText.OverflowMode = ValueOverflowContract.Clip;
            widget.Validate(tempRoot);

            using var images = new ImageAssetCache(tempRoot);
            using var icons = new IconAssetCache(tempRoot);
            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, new[] { CurrentWidget(widget) });
            using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = 42d },
                antialias: false,
                typefaces);
            using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(120, 80))
                ?? throw new InvalidOperationException("Could not create Binary container-layout surface.");
            using var renderer = new PinkieSysMon.Widgets.BinaryWidgetRenderer(images, icons);

            surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
            PinkieSysMon.Widgets.WidgetTransform.Render(surface.Canvas, CurrentWidget(widget), context, renderer);
            surface.Canvas.Flush();

            using var pixels = surface.PeekPixels()
                ?? throw new InvalidOperationException("Could not inspect Binary container-layout pixels.");
            var minY = int.MaxValue;
            var maxY = int.MinValue;
            for (var y = 0; y < pixels.Height; y++)
            {
                for (var x = 0; x < pixels.Width; x++)
                {
                    if (pixels.GetPixelColor(x, y).Alpha == 0)
                        continue;
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                }
            }

            Assert(minY != int.MaxValue, "Binary value visual rendered no text pixels.");
            Assert(minY >= 20 && maxY < 60,
                $"Binary middle-aligned value escaped its 40px container: y={minY}..{maxY}.");
            var pixelCenter = (minY + maxY) / 2.0;
            Assert(Math.Abs(pixelCenter - 40d) <= 4d,
                $"Binary middle-aligned value is not centered in the widget: pixel center={pixelCenter:0.0}, expected ~40.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void BinaryValueContainerHeightClip()
    {
        const string metric = RuntimeMetricContract.Fps;
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-binary-value-height-clip-regression");
        try
        {
            foreach (var width in new[] { 80f })
            {
                var widget = new WidgetDefinition
                {
                    Id = $"binary-value-height-clip-{width}",
                    Type = WidgetTypeContract.Binary,
                    X = 10,
                    Y = 20,
                    Width = width,
                    Height = 10,
                    Metric = metric,
                    FontSize = 28,
                    Align = "left",
                    VerticalAlign = "middle",
                    OverflowMode = ValueOverflowContract.Clip
                };
                widget.EnsureStateVisualProfiles();
                widget.Profiles![BinarySignalContract.TrueKey].SourceType = StateVisualProfileContract.SourceValue;
                widget.Profiles![BinarySignalContract.FalseKey].SourceType = StateVisualProfileContract.SourceIcon;
                var trueText = GetBinaryText(widget, BinarySignalContract.TrueKey);
                trueText.FontSize = 28f;
                trueText.Align = "left";
                trueText.VerticalAlign = "middle";
                trueText.OverflowMode = ValueOverflowContract.Clip;
                widget.Validate(tempRoot);

                using var images = new ImageAssetCache(tempRoot);
                using var icons = new IconAssetCache(tempRoot);
                using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, new[] { CurrentWidget(widget) });
                using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = 42d },
                    antialias: false,
                    typefaces);
                using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(120, 60))
                    ?? throw new InvalidOperationException("Could not create Binary height-clip surface.");
                using var renderer = new PinkieSysMon.Widgets.BinaryWidgetRenderer(images, icons);

                surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
                PinkieSysMon.Widgets.WidgetTransform.Render(surface.Canvas, CurrentWidget(widget), context, renderer);
                surface.Canvas.Flush();

                using var pixels = surface.PeekPixels()
                    ?? throw new InvalidOperationException("Could not inspect Binary height-clip pixels.");
                for (var y = 0; y < pixels.Height; y++)
                {
                    for (var x = 0; x < pixels.Width; x++)
                    {
                        if (pixels.GetPixelColor(x, y).Alpha == 0)
                            continue;

                        Assert(y >= 20 && y < 30,
                            $"Binary value text escaped explicit Height=10 at y={y} (Width={width}).");
                    }
                }
            }
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void BinaryValueAutoWidthGeometry()
    {
        const string metric = RuntimeMetricContract.Fps;
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-binary-value-autowidth-regression");
        try
        {
            var widget = new WidgetDefinition
            {
                Id = "binary-value-autowidth",
                Type = WidgetTypeContract.Binary,
                X = 12,
                Y = 18,
                Width = 0,
                Height = 40,
                Metric = metric,
                FontSize = 24,
                Align = "left",
                VerticalAlign = "middle"
            };
            widget.EnsureStateVisualProfiles();
            widget.Profiles![BinarySignalContract.TrueKey].SourceType = StateVisualProfileContract.SourceValue;
            widget.Profiles![BinarySignalContract.FalseKey].SourceType = StateVisualProfileContract.SourceValue;
            foreach (var stateKey in new[] { BinarySignalContract.TrueKey, BinarySignalContract.FalseKey })
            {
                var text = GetBinaryText(widget, stateKey);
                text.FontSize = 24f;
                text.Align = "left";
                text.VerticalAlign = "middle";
                text.OverflowMode = ValueOverflowContract.None;
            }

            widget.Validate(tempRoot);

            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, new[] { CurrentWidget(widget) });
            using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = 42d },
                antialias: false,
                typefaces);
            var bounds = PinkieSysMon.Widgets.WidgetGeometry.GetLayoutBounds(CurrentWidget(widget), context);

            Assert(bounds.Width > 1f,
                $"Binary value Width=0 did not resolve intrinsic width; got {bounds.Width}.");
            Assert(bounds.Height > 1f && Math.Abs(bounds.Height - 40f) > 1f,
                $"Value-only Binary must use content-driven height instead of dormant Height=40; got {bounds.Height}.");
            Assert(Math.Abs(bounds.Left - 12f) < 0.001f && Math.Abs(bounds.Top - 18f) < 0.001f,
                "Binary value auto geometry changed the top-left container origin.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void BinaryValueOnlyGeometryValidation()
    {
        const string metric = RuntimeMetricContract.Fps;
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-binary-value-validation-regression");
        try
        {
            var widget = new WidgetDefinition
            {
                Id = "binary-value-validation",
                Type = WidgetTypeContract.Binary,
                X = 10,
                Y = 20,
                Width = 80,
                Height = 0,
                Metric = metric,
                FontSize = 24,
                OverflowMode = ValueOverflowContract.Clip
            };
            widget.EnsureStateVisualProfiles();
            widget.Profiles![BinarySignalContract.TrueKey].SourceType = StateVisualProfileContract.SourceValue;
            widget.Profiles![BinarySignalContract.FalseKey].SourceType = StateVisualProfileContract.SourceValue;
            foreach (var stateKey in new[] { BinarySignalContract.TrueKey, BinarySignalContract.FalseKey })
                GetBinaryText(widget, stateKey).OverflowMode = ValueOverflowContract.Clip;

            // This is the geometry that previously made Ctrl+D fail in clone.Validate().
            widget.Validate(tempRoot);

            // Value-only Binary also supports intrinsic Width=0, matching the established
            // Binary value auto-width contract while keeping Height content-driven.
            widget.Width = 0;
            foreach (var stateKey in new[] { BinarySignalContract.TrueKey, BinarySignalContract.FalseKey })
                GetBinaryText(widget, stateKey).OverflowMode = ValueOverflowContract.None;
            widget.Validate(tempRoot);

            widget.Width = 80;
            foreach (var stateKey in new[] { BinarySignalContract.TrueKey, BinarySignalContract.FalseKey })
                GetBinaryText(widget, stateKey).OverflowMode = ValueOverflowContract.Clip;
            widget.Profiles![BinarySignalContract.FalseKey].SourceType = StateVisualProfileContract.SourceIcon;
            var rejectedMixedAutoGeometry = false;
            try
            {
                widget.Validate(tempRoot);
            }
            catch (InvalidDataException)
            {
                rejectedMixedAutoGeometry = true;
            }

            Assert(rejectedMixedAutoGeometry,
                "Mixed Binary icon/value states must require explicit Width and Height.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void BinaryValuePropertyVisibility()
    {
        var widget = new C.BinaryWidgetDefinition
        {
            Id = "binary-value-properties",
            Metric = SystemMetricContract.Uptime,
            Width = 100f,
            Height = 40f
        };
        TextOverflowStateContract.EnsureProfiles(widget);

        static string StateProperty(string key, StateVisualProfileField field) =>
            $"{StateVisualProfilePropertyDescriptor.PropertyPrefix}{key}:{field}";

        var trueSourceTypeName = StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.SourceType);
        var trueSourceName = StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.Source);
        var trueColorName = StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.Color);
        var trueFontName = StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.FontFamily);
        var trueOverflowName = StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.OverflowMode);

        var iconAppearance = new EditorPropertyView(widget, section: EditorPropertySection.Appearance).GetProperties();
        var iconText = new EditorPropertyView(widget, section: EditorPropertySection.Text).GetProperties();
        var iconStates = new EditorPropertyView(widget, section: EditorPropertySection.States).GetProperties();
        Assert(GetNestedProperty(iconAppearance, "Foreground", nameof(C.WidgetDefinition.Color)) is { DisplayName: "Color" } &&
               iconText.Count == 0,
            "Icon-only Binary must expose universal Foreground fallback while hiding Text presentation.");
        Assert(GetNestedProperty(iconStates, "State: True", trueSourceName) is { IsReadOnly: false },
            "Icon-backed Binary state must expose editable Source.");

        var sourceType = GetNestedProperty(iconStates, "State: True", trueSourceTypeName)
            ?? throw new InvalidOperationException("Binary state Source Type property is missing.");
        var values = sourceType.Converter.GetStandardValues()
            ?.Cast<object>()
            .Select(Convert.ToString)
            .Where(value => value is not null)
            .ToArray() ?? [];
        Assert(values.Any(value => string.Equals(value, "Value", StringComparison.OrdinalIgnoreCase)),
            "Binary state Source Type must offer Value.");

        var trueProfile = widget.Profiles[BinarySignalContract.TrueKey];
        trueProfile.ContentType = C.StateContentType.Value;
        TextOverflowStateContract.ApplyContextChange(widget, 100f, 32f);

        var mixedStates = new EditorPropertyView(widget, section: EditorPropertySection.States).GetProperties();
        var mixedTrueSource = GetNestedProperty(mixedStates, "State: True", trueSourceName);
        Assert(mixedTrueSource is { IsReadOnly: true } &&
               mixedTrueSource.Description == "Not applicable for selected Source Type.",
            "Value-backed Binary state must retain Source as a stable disabled row.");

        var falseFitName = StateProperty(BinarySignalContract.FalseKey, StateVisualProfileField.Fit);
        var mixedImage = new EditorPropertyView(widget, section: EditorPropertySection.Image).GetProperties();
        Assert(GetNestedProperty(mixedImage, "State: False", falseFitName) is { DisplayName: "Fit" } &&
               mixedImage["State: True"] is null,
            "Mixed Binary value/icon states must retain image presentation only for the image-backed state.");

        var mixedGeneral = new EditorPropertyView(widget, section: EditorPropertySection.General).GetProperties();
        var mixedHeight = GetNestedProperty(mixedGeneral, "Geometry", nameof(C.WidgetDefinition.Height));
        Assert(mixedHeight is { IsReadOnly: false },
            "Mixed Binary value/icon states must expose editable Height.");

        var mixedText = new EditorPropertyView(widget, section: EditorPropertySection.Text).GetProperties();
        Assert(mixedText["State: True"] is PropertyGroupPropertyDescriptor && mixedText["State: False"] is null,
            "Mixed Binary must expose Text presentation only for its value-backed state.");
        Assert(GetNestedProperty(mixedText, "State: True", "Foreground", trueColorName) is { DisplayName: "Color" } &&
               GetNestedProperty(mixedText, "State: True", "Font", trueFontName) is { DisplayName: "Family" } &&
               GetNestedProperty(mixedText, "State: True", "Alignment",
                   StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.Align)) is { DisplayName: "Horizontal" } &&
               GetNestedProperty(mixedText, "State: True", "Overflow", trueOverflowName) is { DisplayName: "Mode" },
            "Binary value state must expose canonical state TextPresentation groups.");

        var mixedData = new EditorPropertyView(widget, section: EditorPropertySection.Data).GetProperties();
        Assert(GetNestedProperty(mixedData, "Value", "SourceUnit") is { IsReadOnly: true } &&
               GetNestedProperty(mixedData, "Value", nameof(C.BinaryWidgetDefinition.Unit)) is not null &&
               GetNestedProperty(mixedData, "Value", nameof(C.BinaryWidgetDefinition.Format)) is not null &&
               GetNestedProperty(mixedData, "Display", nameof(C.BinaryWidgetDefinition.Prefix)) is not null &&
               GetNestedProperty(mixedData, "Display", nameof(C.BinaryWidgetDefinition.Suffix)) is not null &&
               GetNestedProperty(mixedData, "Display", nameof(C.BinaryWidgetDefinition.Fallback)) is not null &&
               GetNestedProperty(mixedData, "Evaluation", nameof(C.BinaryWidgetDefinition.EvaluationMode)) is { DisplayName: "Mode" },
            "Binary value mode must expose Value, Display, and Evaluation groups on Data.");

        widget.Profiles[BinarySignalContract.FalseKey].ContentType = C.StateContentType.Value;
        TextOverflowStateContract.ApplyContextChange(widget, 100f, 32f);
        var valueOnlyImage = new EditorPropertyView(widget, section: EditorPropertySection.Image).GetProperties();
        var valueOnlyText = new EditorPropertyView(widget, section: EditorPropertySection.Text).GetProperties();
        var valueOnlyGeneral = new EditorPropertyView(widget, section: EditorPropertySection.General).GetProperties();
        var valueOnlyHeight = GetNestedProperty(valueOnlyGeneral, "Geometry", nameof(C.WidgetDefinition.Height));
        Assert(valueOnlyHeight is { IsReadOnly: true },
            "Value-only Binary must expose disabled derived Height.");
        Assert(valueOnlyImage.Count == 0 &&
               valueOnlyText["State: True"] is not null &&
               valueOnlyText["State: False"] is not null,
            "Value-only Binary must move both state presentations entirely to Text.");
    }

    private static void BinaryValueUnitOptions()
    {
        const string provider = "binary-unit-test";
        const string metric = provider + ".download";
        MetricContract.ReplaceProviderDescriptors(
            provider,
            [new MetricDescriptor(metric, MetricValueKind.Number, MetricUnit.BytesPerSecond)]);

        var widget = new C.BinaryWidgetDefinition
        {
            Id = "binary-value-units",
            Metric = metric,
            Width = 100f,
            Height = 40f,
            EvaluationMode = BinarySignalContract.EvaluationModeAuto
        };
        TextOverflowStateContract.EnsureProfiles(widget);
        widget.Profiles[BinarySignalContract.TrueKey].ContentType = C.StateContentType.Value;
        widget.Profiles[BinarySignalContract.FalseKey].ContentType = C.StateContentType.Value;
        TextOverflowStateContract.ApplyContextChange(widget, 100f, 32f);

        var data = new EditorPropertyView(widget, section: EditorPropertySection.Data).GetProperties();
        var unit = GetNestedProperty(data, "Value", nameof(C.BinaryWidgetDefinition.Unit))
            ?? throw new InvalidOperationException("Binary value Unit property is missing for a data-rate metric.");
        var unitValues = unit.Converter.GetStandardValues()
            ?.Cast<object>()
            .Select(Convert.ToString)
            .Where(value => value is not null)
            .ToArray() ?? [];
        Assert(unitValues.Any(value => value!.StartsWith("Mbit/s", StringComparison.Ordinal)),
            "Binary value Unit selector must expose Mbit/s for a byte-rate metric.");
        Assert(unitValues.Any(value => value!.StartsWith("MiB/s", StringComparison.Ordinal)),
            "Binary value Unit selector must expose MiB/s for a byte-rate metric.");

        var fixedText = new EditorPropertyView(widget, section: EditorPropertySection.Text).GetProperties();
        Assert(GetNestedProperty(
                   fixedText, "State: True", "Overflow",
                   $"{StateVisualProfilePropertyDescriptor.PropertyPrefix}{BinarySignalContract.TrueKey}:{StateVisualProfileField.OverflowMode}") is not null &&
               GetNestedProperty(
                   fixedText, "State: False", "Overflow",
                   $"{StateVisualProfilePropertyDescriptor.PropertyPrefix}{BinarySignalContract.FalseKey}:{StateVisualProfileField.OverflowMode}") is not null,
            "Fixed-width Binary value must expose per-state Overflow Mode.");

        widget.Width = 0;
        TextOverflowStateContract.NormalizeStoredState(widget);
        Assert(string.Equals(widget.Profiles[BinarySignalContract.TrueKey].TextPresentation.OverflowMode, ValueOverflowContract.None, StringComparison.Ordinal) &&
               string.Equals(widget.Profiles[BinarySignalContract.FalseKey].TextPresentation.OverflowMode, ValueOverflowContract.None, StringComparison.Ordinal),
            "Auto-width Binary value must normalize both state Overflow modes to None.");
    }

    private static void MetricUnitOptionsFollowMetricChanges()
    {
        const string provider = "unit-refresh-test";
        const string sizeMetric = provider + ".size";
        const string rateMetric = provider + ".rate";
        MetricContract.ReplaceProviderDescriptors(
            provider,
            [
                new MetricDescriptor(sizeMetric, MetricValueKind.DataSize, MetricUnit.Bytes),
                new MetricDescriptor(rateMetric, MetricValueKind.Number, MetricUnit.BytesPerSecond)
            ]);

        var widget = new C.BinaryWidgetDefinition
        {
            Id = "binary-dynamic-unit-options",
            Metric = sizeMetric,
            Width = 100f,
            Height = 40f,
            EvaluationMode = BinarySignalContract.EvaluationModeAuto
        };
        TextOverflowStateContract.EnsureProfiles(widget);
        widget.Profiles[BinarySignalContract.TrueKey].ContentType = C.StateContentType.Value;
        widget.Profiles[BinarySignalContract.FalseKey].ContentType = C.StateContentType.Value;

        var propertyView = new EditorPropertyView(widget, section: EditorPropertySection.Data);
        var unit = GetNestedProperty(propertyView.GetProperties(), "Value", nameof(C.BinaryWidgetDefinition.Unit))
            ?? throw new InvalidOperationException("Binary value Unit property is missing.");

        var initialValues = unit.Converter.GetStandardValues()
            ?.Cast<object>()
            .Select(Convert.ToString)
            .Where(value => value is not null)
            .ToArray() ?? [];
        Assert(initialValues.Contains("MB", StringComparer.Ordinal),
            "Data-size metric must initially expose data-size units.");
        Assert(!initialValues.Any(value => value!.StartsWith("Mbit/s", StringComparison.Ordinal)),
            "Data-size metric must not expose data-rate units.");

        // Reuse the exact same canonical descriptor instance: Unit choices must follow the
        // live Metric instead of retaining the converter options from descriptor creation.
        widget.Metric = rateMetric;

        var updatedValues = unit.Converter.GetStandardValues()
            ?.Cast<object>()
            .Select(Convert.ToString)
            .Where(value => value is not null)
            .ToArray() ?? [];
        Assert(updatedValues.Any(value => value!.StartsWith("Mbit/s", StringComparison.Ordinal)),
            "Existing Binary Unit descriptor must follow a metric change to data-rate units.");
        Assert(updatedValues.Any(value => value!.StartsWith("MiB/s", StringComparison.Ordinal)),
            "Existing Binary Unit descriptor must expose binary data-rate units after the metric changes.");
        Assert(!updatedValues.Contains("MB", StringComparer.Ordinal),
            "Existing Binary Unit descriptor must not retain stale data-size unit choices.");
    }

    private static void TextValueSourceSemantics()
    {
        const string metric = RuntimeMetricContract.Version;
        var metricWidget = new WidgetDefinition
        {
            Type = WidgetTypeContract.Value,
            Id = "metric-value",
            ValueSource = TextValueContract.SourceMetric,
            Metric = metric
        };
        var textWidget = new WidgetDefinition
        {
            Type = WidgetTypeContract.Value,
            Id = "static-text",
            ValueSource = TextValueContract.SourceText,
            Text = "STATIC",
            Metric = metric,
            Prefix = "<",
            Suffix = ">"
        };
        var numericTextWidget = new WidgetDefinition
        {
            Type = WidgetTypeContract.Value,
            Id = "numeric-text",
            ValueSource = TextValueContract.SourceText,
            Text = "125",
            SourceUnit = "MB/s",
            Unit = "Mbit/s",
            Format = "0.0",
            Prefix = "[",
            Suffix = "]"
        };

        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-text-value-regression");
        try
        {
            var currentMetricWidget = (C.ValueWidgetDefinition)CurrentWidget(metricWidget);
            var currentTextWidget = (C.ValueWidgetDefinition)CurrentWidget(textWidget);
            var currentNumericTextWidget = (C.ValueWidgetDefinition)CurrentWidget(numericTextWidget);
            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, new C.WidgetDefinition[] { currentMetricWidget, currentTextWidget, currentNumericTextWidget });
            using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = "1.2.3" },
                antialias: false,
                typefaces);

            Assert(context.ResolveValueText(currentMetricWidget) == "1.2.3",
                "Metric-source value did not resolve formatted metric content.");
            Assert(context.ResolveValueText(currentTextWidget) == "<STATIC>",
                "Text-source value must apply Prefix/Suffix while preserving non-numeric literal text.");
            var expectedNumericText = $"[{1000d.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture)}]";
            Assert(context.ResolveValueText(currentNumericTextWidget) == expectedNumericText,
                "Numeric literal Text must apply Source Unit conversion and Format before text presentation.");

            var dashboard = new DashboardDefinition();
            dashboard.Widgets.Add(textWidget);
            Assert(!DashboardMetricUsage.Collect(CurrentDashboard(dashboard)).Contains(metric),
                "Text-source value must not retain a telemetry dependency from a stale Metric field.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void TextValuePropertyVisibility()
    {
        var metricWidget = new WidgetDefinition
        {
            Type = WidgetTypeContract.Value,
            Id = "metric-value-properties",
            ValueSource = TextValueContract.SourceMetric,
            Metric = RuntimeMetricContract.Version,
            Width = 100
        };
        var metricProperties = CurrentPropertyView(metricWidget, section: EditorPropertySection.General).GetProperties();
        var metricSourceType = GetNestedProperty(metricProperties, "Source", nameof(C.ValueWidgetDefinition.SourceKind));
        var metricSourceMetric = GetNestedProperty(metricProperties, "Source", nameof(WidgetDefinition.Metric));
        var metricSourceText = GetNestedProperty(metricProperties, "Source", nameof(WidgetDefinition.Text));
        Assert(metricSourceType is not null && metricSourceType.DisplayName == "Type" &&
               string.Equals(Convert.ToString(metricSourceType.GetValue(null)), TextValueContract.SourceMetric, StringComparison.Ordinal),
            "Metric-source value must expose Source/Type=Metric.");
        Assert(metricSourceMetric is { IsReadOnly: false },
            "Metric-source value must expose editable Source/Metric.");
        Assert(metricSourceText is { IsReadOnly: true } &&
               metricSourceText.Description == "Not applicable for selected Type.",
            "Metric-source value must keep Source/Text visible but disabled.");

        var textWidget = new WidgetDefinition
        {
            Type = WidgetTypeContract.Value,
            Id = "text-value-properties",
            ValueSource = TextValueContract.SourceText,
            Text = "STATIC",
            Width = 100
        };
        var textProperties = CurrentPropertyView(textWidget, section: EditorPropertySection.General).GetProperties();
        var textSourceType = GetNestedProperty(textProperties, "Source", nameof(C.ValueWidgetDefinition.SourceKind));
        var textSourceText = GetNestedProperty(textProperties, "Source", nameof(WidgetDefinition.Text));
        var textSourceMetric = GetNestedProperty(textProperties, "Source", nameof(WidgetDefinition.Metric));
        Assert(textSourceType is not null && textSourceType.DisplayName == "Type" &&
               string.Equals(Convert.ToString(textSourceType.GetValue(null)), TextValueContract.SourceText, StringComparison.Ordinal),
            "Text-source value must expose Source/Type=Text.");
        Assert(textSourceText is { IsReadOnly: false },
            "Text-source value must expose editable Source/Text.");
        Assert(textSourceMetric is { IsReadOnly: true } &&
               textSourceMetric.Description == "Not applicable for selected Type.",
            "Text-source value must keep Source/Metric visible but disabled.");
        Assert(textProperties[nameof(WidgetDefinition.SourceUnit)] is null &&
               textProperties[nameof(WidgetDefinition.Prefix)] is null &&
               textProperties[nameof(WidgetDefinition.Suffix)] is null &&
               textProperties[nameof(WidgetDefinition.Unit)] is null &&
               textProperties[nameof(WidgetDefinition.Format)] is null &&
               textProperties[nameof(WidgetDefinition.Fallback)] is null,
            "Literal Text data properties must live on Data rather than Widget.");

        var metricAppearance = CurrentPropertyView(
            metricWidget,
            section: EditorPropertySection.Appearance).GetProperties();
        var textAppearance = CurrentPropertyView(
            textWidget,
            section: EditorPropertySection.Appearance).GetProperties();
        Assert(metricAppearance["Alignment"] is null &&
               textAppearance["Alignment"] is null &&
               metricAppearance["Font"] is null && metricAppearance["Outline"] is null && metricAppearance["Overflow"] is null &&
               textAppearance["Font"] is null && textAppearance["Outline"] is null && textAppearance["Overflow"] is null,
            "Text presentation groups must move off Appearance.");

        var metricText = CurrentPropertyView(
            metricWidget,
            section: EditorPropertySection.Text).GetProperties();
        var literalText = CurrentPropertyView(
            textWidget,
            section: EditorPropertySection.Text).GetProperties();
        Assert(metricText["Font"] is not null &&
               metricText["Alignment"] is not null &&
               metricText["Outline"] is not null &&
               metricText["Overflow"] is not null,
            "Metric-source Text/Value must expose Font, Alignment, Outline, and Overflow together on Text.");
        Assert(literalText["Font"] is not null &&
               literalText["Alignment"] is not null &&
               literalText["Outline"] is not null &&
               literalText["Overflow"] is not null,
            "Text-source Text/Value must expose Font, Alignment, Outline, and Overflow together on Text.");
        Assert(GetNestedProperty(literalText, "Overflow", nameof(WidgetDefinition.OverflowMode)) is { DisplayName: "Mode" },
            "Text-source Text/Value Overflow group must expose Mode.");

        var metricData = CurrentPropertyView(
            metricWidget,
            section: EditorPropertySection.Data).GetProperties();
        Assert(GetNestedProperty(metricData, "Display", nameof(WidgetDefinition.Prefix)) is not null &&
               GetNestedProperty(metricData, "Display", nameof(WidgetDefinition.Suffix)) is not null &&
               GetNestedProperty(metricData, "Display", nameof(WidgetDefinition.Fallback)) is not null,
            "Metric-source Text/Value presentation fields must live under Data/Display.");
        var literalData = CurrentPropertyView(
            textWidget,
            section: EditorPropertySection.Data).GetProperties();
        Assert(GetNestedProperty(literalData, "Value", nameof(WidgetDefinition.SourceUnit)) is { IsReadOnly: true } &&
               GetNestedProperty(literalData, "Value", nameof(WidgetDefinition.Unit)) is { IsReadOnly: true } &&
               GetNestedProperty(literalData, "Value", nameof(WidgetDefinition.Format)) is { IsReadOnly: true } &&
               GetNestedProperty(literalData, "Display", nameof(WidgetDefinition.Prefix)) is { IsReadOnly: false } &&
               GetNestedProperty(literalData, "Display", nameof(WidgetDefinition.Suffix)) is { IsReadOnly: false } &&
               GetNestedProperty(literalData, "Display", nameof(WidgetDefinition.Fallback)) is { IsReadOnly: true },
            "Non-numeric literal Text must keep Data discoverable while disabling numeric-only and unavailable-source fields.");

    }

    private static void LiteralNumericDataSemantics()
    {
        Assert(LiteralNumericContract.TryParse("125", out var integerValue) && Math.Abs(integerValue - 125d) < 0.000001,
            "Plain invariant numeric Text must be recognized as numeric.");
        Assert(LiteralNumericContract.TryParse("1.25e3", out var exponentValue) && Math.Abs(exponentValue - 1250d) < 0.000001,
            "Invariant exponent notation must be recognized as numeric Text.");
        Assert(!LiteralNumericContract.TryParse("1,234", out _),
            "Ambiguous thousands/decimal punctuation must not be guessed as numeric Text.");
        Assert(!LiteralNumericContract.TryParse("125 MB", out _),
            "Text carrying embedded unit characters must remain ordinary text.");

        var sourceDescriptor = LiteralNumericContract.GetDescriptor("MB/s");
        Assert(MetricValueConverter.TryConvertNumeric(125d, sourceDescriptor, "Mbit/s", out var converted) &&
               Math.Abs(converted - 1000d) < 0.000001,
            "Manual Source Unit=MB/s must convert through the same numeric pipeline to Mbit/s.");

        var numeric = new WidgetDefinition
        {
            Type = WidgetTypeContract.Value,
            Id = "literal-numeric-data",
            ValueSource = TextValueContract.SourceText,
            Text = "125",
            SourceUnit = "MB/s",
            Unit = "Mbit/s",
            Format = "0.0",
            Width = 100f
        };
        var data = CurrentPropertyView(numeric, section: EditorPropertySection.Data).GetProperties();
        Assert(GetNestedProperty(data, "Value", nameof(WidgetDefinition.SourceUnit)) is { IsReadOnly: false } &&
               GetNestedProperty(data, "Value", nameof(WidgetDefinition.Unit)) is { IsReadOnly: false } &&
               GetNestedProperty(data, "Value", nameof(WidgetDefinition.Format)) is { IsReadOnly: false },
            "Numeric literal Text must enable Source Unit, Unit, and Format under Data/Value.");
    }

    private static void LiteralNumericUnitOptionsFollowSourceUnit()
    {
        var widget = new C.ValueWidgetDefinition
        {
            Id = "literal-unit-refresh",
            SourceKind = C.ValueSourceKind.Text,
            Text = "125",
            SourceUnit = "B",
            Width = 100f
        };

        var properties = new EditorPropertyView(widget, section: EditorPropertySection.Data).GetProperties();
        var unit = GetNestedProperty(properties, "Value", nameof(C.ValueWidgetDefinition.Unit))
            ?? throw new InvalidOperationException("Literal numeric Data/Value/Unit property is missing.");

        var initialValues = unit.Converter.GetStandardValues()
            ?.Cast<object>()
            .Select(Convert.ToString)
            .Where(value => value is not null)
            .ToArray() ?? [];
        Assert(initialValues.Contains("MB", StringComparer.Ordinal) &&
               !initialValues.Any(value => value!.StartsWith("Mbit/s", StringComparison.Ordinal)),
            "Source Unit=B must expose data-size target units, not data-rate units.");

        widget.SourceUnit = "B/s";
        var updatedValues = unit.Converter.GetStandardValues()
            ?.Cast<object>()
            .Select(Convert.ToString)
            .Where(value => value is not null)
            .ToArray() ?? [];
        Assert(updatedValues.Any(value => value!.StartsWith("Mbit/s", StringComparison.Ordinal)) &&
               updatedValues.Any(value => value!.StartsWith("MiB/s", StringComparison.Ordinal)) &&
               !updatedValues.Contains("MB", StringComparer.Ordinal),
            "The live literal Unit selector must follow Source Unit changes without a stale converter snapshot.");
    }

    private static void LiteralNumericDashboardNormalization()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-literal-normalization-regression");
        try
        {
            var dashboardPath = Path.Combine(tempRoot, "dashboards", "literal-normalization.json");
            var definition = DashboardDefinition.Parse(
                $$"""
                {
                  "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                  "canvas": { "width": 1920, "height": 480, "orientation": 0, "backgroundColor": "#000000" },
                  "widgets": [
                    {
                      "type": "value",
                      "id": "literal",
                      "width": 100,
                      "height": 0,
                      "valueSource": "Text",
                      "text": "125",
                      "sourceUnit": "MB/s",
                      "unit": "GB",
                      "format": "0.0",
                      "overflowMode": "Clip"
                    }
                  ]
                }
                """,
                dashboardPath);

            var widget = definition.Widgets.Single();
            Assert(string.Equals(widget.SourceUnit, "MB/s", StringComparison.Ordinal),
                "Valid manual Source Unit must survive dashboard normalization.");
            Assert(widget.Unit is null,
                "A stale target Unit incompatible with the manual Source Unit must normalize to raw.");
            Assert(string.Equals(widget.Format, "0.0", StringComparison.Ordinal),
                "A valid numeric Format must survive literal numeric normalization.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void TextOverflowStateTransitions()
    {
        var widget = new WidgetDefinition
        {
            Type = WidgetTypeContract.Value,
            Id = "overflow-transitions",
            Width = 0f,
            FontSize = 32f,
            OverflowMode = ValueOverflowContract.Clip
        };

        Assert(TextOverflowStateContract.NormalizeStoredState(widget) &&
               widget.Width == 0f &&
               widget.OverflowMode == ValueOverflowContract.None,
            "Width=0 text must normalize to Overflow=None.");

        widget.OverflowMode = ValueOverflowContract.Scroll;
        Assert(TextOverflowStateContract.ApplyOverflowModeChange(widget, 137f, 32f) &&
               Math.Abs(widget.Width - 137f) < 0.001f &&
               widget.OverflowMode == ValueOverflowContract.Scroll,
            "Selecting a concrete Overflow mode from auto width must materialize the current intrinsic width.");

        widget.OverflowMode = ValueOverflowContract.None;
        Assert(TextOverflowStateContract.ApplyOverflowModeChange(widget, 137f, 32f) &&
               widget.Width == 0f &&
               widget.OverflowMode == ValueOverflowContract.None,
            "Selecting Overflow=None must restore auto width.");

        widget.Width = 90f;
        Assert(TextOverflowStateContract.ApplyWidthChange(widget, 90f, 32f) &&
               widget.Width == 90f &&
               widget.OverflowMode == ValueOverflowContract.Clip,
            "Changing Width from auto to fixed while Overflow=None must select neutral Clip mode.");

        widget.Width = 0f;
        Assert(TextOverflowStateContract.ApplyWidthChange(widget, 1f, 32f) &&
               widget.OverflowMode == ValueOverflowContract.None,
            "Changing Width back to zero must restore Overflow=None.");

        widget.OverflowMode = ValueOverflowContract.Wrap;
        Assert(TextOverflowStateContract.ApplyOverflowModeChange(widget, 1f, 32f) &&
               Math.Abs(widget.Width - 32f) < 0.001f,
            "Empty auto-width text must use the minimum default width when Overflow is enabled.");

        var binary = new WidgetDefinition
        {
            Type = WidgetTypeContract.Binary,
            Id = "overflow-binary",
            Width = 120f,
            Height = 40f
        };
        binary.EnsureStateVisualProfiles();
        binary.Profiles![BinarySignalContract.TrueKey].SourceType = StateVisualProfileContract.SourceValue;
        binary.Profiles![BinarySignalContract.FalseKey].SourceType = StateVisualProfileContract.SourceValue;
        var trueText = GetBinaryText(binary, BinarySignalContract.TrueKey);
        var falseText = GetBinaryText(binary, BinarySignalContract.FalseKey);
        trueText.OverflowMode = ValueOverflowContract.Clip;
        falseText.OverflowMode = ValueOverflowContract.Ellipsis;

        Assert(!TextOverflowStateContract.NormalizeStoredState(binary) &&
               trueText.OverflowMode == ValueOverflowContract.Clip &&
               falseText.OverflowMode == ValueOverflowContract.Ellipsis,
            "Fixed-width value-only Binary must preserve independent concrete Overflow modes.");

        trueText.OverflowMode = ValueOverflowContract.None;
        Assert(TextOverflowStateContract.ApplyOverflowModeChange(
                   binary, BinarySignalContract.TrueKey, 137f, 32f) &&
               binary.Width == 0f &&
               trueText.OverflowMode == ValueOverflowContract.None &&
               falseText.OverflowMode == ValueOverflowContract.None,
            "Selecting None in one value-only Binary state must switch shared Width to auto and normalize both states to None.");

        trueText.OverflowMode = ValueOverflowContract.Scroll;
        Assert(TextOverflowStateContract.ApplyOverflowModeChange(
                   binary, BinarySignalContract.TrueKey, 137f, 32f) &&
               Math.Abs(binary.Width - 137f) < 0.001f &&
               trueText.OverflowMode == ValueOverflowContract.Scroll &&
               falseText.OverflowMode == ValueOverflowContract.Clip,
            "Selecting a concrete Binary Overflow mode from auto width must materialize shared Width and normalize sibling None to Clip.");

        binary.Width = 0f;
        Assert(TextOverflowStateContract.ApplyWidthChange(binary, 1f, 32f) &&
               trueText.OverflowMode == ValueOverflowContract.None &&
               falseText.OverflowMode == ValueOverflowContract.None,
            "Changing shared Binary Width to zero must normalize every value state to None.");

        binary.Profiles[BinarySignalContract.FalseKey].SourceType = StateVisualProfileContract.SourceIcon;
        Assert(TextOverflowStateContract.ApplyContextChange(binary, 80f, 32f) &&
               Math.Abs(binary.Width - 80f) < 0.001f &&
               trueText.OverflowMode == ValueOverflowContract.Clip,
            "Switching auto-width Binary to mixed value/image must materialize Width and normalize the active value state to Clip.");
        Assert(falseText.OverflowMode == ValueOverflowContract.None,
            "Hidden text presentation of a non-value Binary state must remain latent during mixed-state reconciliation.");
        Assert(!TextOverflowStateContract.GetSupportedModes(binary).Contains(ValueOverflowContract.None),
            "Mixed Binary must not offer Overflow=None because its image state requires a fixed container.");
    }

    private static void TextOverflowDashboardNormalization()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-overflow-normalization-regression");
        try
        {
            var dashboardPath = Path.Combine(tempRoot, "dashboards", "overflow-normalization.json");
            var definition = DashboardDefinition.Parse(
                $$"""
                {
                  "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                  "canvas": { "width": 1920, "height": 480, "orientation": 0, "backgroundColor": "#000000" },
                  "widgets": [
                    {
                      "type": "value",
                      "id": "auto",
                      "width": 0,
                      "height": 0,
                      "valueSource": "Text",
                      "text": "AUTO",
                      "overflowMode": "Clip"
                    },
                    {
                      "type": "value",
                      "id": "fixed",
                      "width": 120,
                      "height": 0,
                      "valueSource": "Text",
                      "text": "FIXED",
                      "overflowMode": "None"
                    }
                  ]
                }
                """,
                dashboardPath);

            var auto = definition.Widgets.Single(widget => widget.Id == "auto");
            var fixedWidth = definition.Widgets.Single(widget => widget.Id == "fixed");
            Assert(auto.OverflowMode == ValueOverflowContract.None,
                "Legacy Width=0 text must migrate to Overflow=None during load.");
            Assert(fixedWidth.OverflowMode == ValueOverflowContract.Clip,
                "Legacy fixed-width text with Overflow=None must migrate to Clip during load.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void WidgetSourcePropertyHierarchy()
    {
        static PropertyDescriptor? SourceProperty(PropertyDescriptorCollection properties, string propertyName) =>
            GetNestedProperty(properties, "Source", propertyName);

        static PropertyDescriptor RequireSourceType(PropertyDescriptorCollection properties, string expectedValue)
        {
            var group = properties["Source"]
                ?? throw new InvalidOperationException("Widget Source group is missing.");
            var groupValue = group.GetValue(null)
                ?? throw new InvalidOperationException("Widget Source group value is missing.");
            var type = TypeDescriptor.GetProperties(groupValue)
                .Cast<PropertyDescriptor>()
                .FirstOrDefault(property => property.DisplayName == "Type")
                ?? throw new InvalidOperationException("Widget Source/Type property is missing.");
            Assert(string.Equals(Convert.ToString(type.GetValue(groupValue)), expectedValue, StringComparison.Ordinal),
                $"Widget Source/Type expected '{expectedValue}'.");
            return type;
        }

        static void AssertNoSourceType(PropertyDescriptorCollection properties, string widgetKind)
        {
            var group = properties["Source"]
                ?? throw new InvalidOperationException($"{widgetKind} Source group is missing.");
            var groupValue = group.GetValue(null)
                ?? throw new InvalidOperationException($"{widgetKind} Source group value is missing.");
            Assert(TypeDescriptor.GetProperties(groupValue)
                       .Cast<PropertyDescriptor>()
                       .All(property => property.DisplayName != "Type"),
                $"{widgetKind} must not expose a fake fixed Source/Type row in schema 19.");
        }

        var value = new C.ValueWidgetDefinition
        {
            Id = "source-value-metric",
            SourceKind = C.ValueSourceKind.Metric,
            Metric = RuntimeMetricContract.Version,
            Width = 100f
        };
        var valueProperties = new EditorPropertyView(value, section: EditorPropertySection.General).GetProperties();
        var valueType = RequireSourceType(valueProperties, "Metric");
        Assert(!valueType.IsReadOnly,
            "Text / Value Source/Type must remain user-selectable.");
        var valueTypeOptions = valueType.Converter.GetStandardValues()
            ?.Cast<object>()
            .Select(Convert.ToString)
            .Where(option => option is not null)
            .ToArray() ?? [];
        Assert(valueTypeOptions.SequenceEqual(new[] { "Metric", "Text" }),
            "Text / Value Source/Type must offer Metric and Text.");
        Assert(SourceProperty(valueProperties, nameof(C.ValueWidgetDefinition.Metric)) is { IsReadOnly: false } &&
               SourceProperty(valueProperties, nameof(C.ValueWidgetDefinition.Text)) is { IsReadOnly: true },
            "Metric Text / Value source must keep Text visible but disabled while Metric is selected.");

        valueType.SetValue(null, "Text");
        Assert(C.ValueSourceKind.IsText(value.SourceKind),
            "Text / Value Source/Type UI mapping must update the canonical SourceKind property.");
        valueProperties = new EditorPropertyView(value, section: EditorPropertySection.General).GetProperties();
        Assert(SourceProperty(valueProperties, nameof(C.ValueWidgetDefinition.Metric)) is { IsReadOnly: true } &&
               SourceProperty(valueProperties, nameof(C.ValueWidgetDefinition.Text)) is { IsReadOnly: false },
            "Text Text / Value source must disable Metric and enable Text.");

        C.WidgetDefinition[] metricOnly =
        [
            new C.BinaryWidgetDefinition { Id = "source-binary", Metric = RuntimeMetricContract.Fps, Width = 100f, Height = 40f },
            new C.GaugeWidgetDefinition { Id = "source-gauge", Metric = RuntimeMetricContract.Fps, Width = 100f, Height = 100f },
            new C.BarWidgetDefinition { Id = "source-bar", Metric = RuntimeMetricContract.Fps, Width = 100f, Height = 20f }
        ];
        foreach (var widget in metricOnly)
        {
            var properties = new EditorPropertyView(widget, section: EditorPropertySection.General).GetProperties();
            AssertNoSourceType(properties, widget.Type);
            Assert(SourceProperty(properties, "Metric") is { IsReadOnly: false },
                $"{widget.Type} must expose its real Metric property in Source.");
        }

        var image = new C.ImageWidgetDefinition
        {
            Id = "source-image",
            Width = 100f,
            Height = 100f,
            Asset = new C.ImageAssetPresentationDefinition
            {
                SourceType = C.ImageAssetSourceType.File,
                Source = "example.png"
            }
        };
        var imageProperties = new EditorPropertyView(image, section: EditorPropertySection.General).GetProperties();
        var imageType = RequireSourceType(imageProperties, "Image");
        var imageTypeOptions = imageType.Converter.GetStandardValues()
            ?.Cast<object>()
            .Select(Convert.ToString)
            .Where(option => option is not null)
            .ToArray() ?? [];
        Assert(imageTypeOptions.SequenceEqual(new[] { "Image", "Icon" }),
            "Image Source/Type must offer Image and Icon.");
        var imageSource = SourceProperty(imageProperties, "Source");
        Assert(imageSource is not null && imageSource.DisplayName == "Image",
            "File-backed Image widget must label its source row as Image.");
        imageType.SetValue(null, "Icon");
        Assert(C.ImageAssetSourceType.IsIcon(image.Asset.SourceType) && image.Asset.Source is null,
            "Image Source/Type transition to Icon must update canonical Asset.SourceType and clear Source atomically.");
        imageProperties = new EditorPropertyView(image, section: EditorPropertySection.General).GetProperties();
        imageSource = SourceProperty(imageProperties, "Source");
        Assert(imageSource is not null && imageSource.DisplayName == "Icon",
            "Icon-backed Image widget must label its source row as Icon.");

        var power = new C.PowerWidgetDefinition
        {
            Id = "source-power",
            PowerSource = PowerMetricContract.UpsSource,
            Width = 100f,
            Height = 100f
        };
        var powerProperties = new EditorPropertyView(power, section: EditorPropertySection.General).GetProperties();
        AssertNoSourceType(powerProperties, "Power");
        Assert(SourceProperty(powerProperties, nameof(C.PowerWidgetDefinition.PowerSource)) is { IsReadOnly: false, DisplayName: "Source" },
            "Power must expose canonical PowerSource as Source without a fake Type row.");

        var mediaSystem = new C.MediaSystemWidgetDefinition
        {
            Id = "source-media-system",
            MediaSource = MediaMetricContract.OutputSource,
            Width = 100f,
            Height = 100f
        };
        var mediaSystemProperties = new EditorPropertyView(mediaSystem, section: EditorPropertySection.General).GetProperties();
        AssertNoSourceType(mediaSystemProperties, "Media System");
        Assert(SourceProperty(mediaSystemProperties, nameof(C.MediaSystemWidgetDefinition.MediaSource)) is { IsReadOnly: false, DisplayName: "Source" },
            "Media System must expose canonical MediaSource as Source without a fake Type row.");

        var mediaPlayer = new C.MediaPlayerWidgetDefinition
        {
            Id = "source-media-player",
            Width = 100f,
            Height = 100f
        };
        var mediaPlayerProperties = new EditorPropertyView(mediaPlayer, section: EditorPropertySection.General).GetProperties();
        AssertNoSourceType(mediaPlayerProperties, "Media Player");
        var playbackMetric = SourceProperty(mediaPlayerProperties, "PlaybackMetric");
        Assert(playbackMetric is not null && playbackMetric.DisplayName == "Metric" && playbackMetric.IsReadOnly &&
               string.Equals(Convert.ToString(playbackMetric.GetValue(null)), MediaMetricContract.PlaybackStatus, StringComparison.Ordinal),
            "Media Player must expose its fixed playback-status metric only as derived read-only information.");

        var canonicalProperties = new[] { valueProperties, imageProperties, powerProperties, mediaSystemProperties, mediaPlayerProperties }
            .SelectMany(collection => collection.Cast<PropertyDescriptor>())
            .SelectMany(FlattenProperties)
            .ToArray();
        Assert(canonicalProperties.All(property => property.Name != "ContentSourceType" && property.Name != "ContentMetric"),
            "Canonical Property Editor must not expose removed synthetic ContentSourceType/ContentMetric identities.");
    }

    private static void UniversalWidgetPropertyHierarchy()
    {
        var types = new[]
        {
            WidgetTypeContract.Value,
            WidgetTypeContract.Bar,
            WidgetTypeContract.Gauge,
            WidgetTypeContract.Image,
            WidgetTypeContract.Binary,
            WidgetTypeContract.Power,
            WidgetTypeContract.MediaSystem,
            WidgetTypeContract.MediaPlayer
        };

        foreach (var type in types)
        {
            var widget = CreateCanonicalPropertyTestWidget(
                type,
                $"hierarchy-{type}",
                type == WidgetTypeContract.Value ? 0f : 100f,
                type == WidgetTypeContract.Value ? 0f : 100f);

            var properties = CurrentPropertyView(
                widget,
                section: EditorPropertySection.General).GetProperties();
            foreach (var group in new[] { "Identity", "Source", "Geometry" })
            {
                var descriptor = properties[group]
                    ?? throw new InvalidOperationException(
                        $"{type} must expose the Widget-tab {group} property group.");
                Assert(descriptor is PropertyGroupPropertyDescriptor,
                    $"{type} Widget-tab {group} must be a first-level editor group.");
            }

            Assert(properties["Foreground"] is null &&
                   properties["Alignment"] is null &&
                   properties["Background"] is null &&
                   properties["Border"] is null &&
                   properties["Shadow"] is null &&
                   properties["Threshold 1"] is null &&
                   properties["Threshold 2"] is null &&
                   properties["Threshold 3"] is null &&
                   properties["Font"] is null &&
                   properties["Outline"] is null,
                $"{type} General tab must not contain Appearance groups.");

            Assert(properties[nameof(WidgetDefinition.Type)] is null &&
                   properties[nameof(WidgetDefinition.Id)] is null &&
                   properties[nameof(WidgetDefinition.Z)] is null &&
                   properties[nameof(WidgetDefinition.ValueSource)] is null &&
                   properties[nameof(WidgetDefinition.Metric)] is null &&
                   properties[nameof(WidgetDefinition.Text)] is null &&
                   properties[nameof(WidgetDefinition.SourceType)] is null &&
                   properties[nameof(WidgetDefinition.X)] is null &&
                   properties[nameof(WidgetDefinition.Width)] is null &&
                   properties[nameof(WidgetDefinition.BackgroundColor)] is null &&
                   properties[nameof(WidgetDefinition.BorderColor)] is null &&
                   properties[nameof(WidgetDefinition.ShadowEnabled)] is null,
                $"{type} must not duplicate hidden Widget/Appearance properties at the top level.");

            Assert(GetNestedProperty(properties, "Identity", "Name") is not null &&
                   GetNestedProperty(properties, "Identity", "Type") is null &&
                   GetNestedProperty(properties, "Identity", "Id") is null,
                $"{type} Identity group must contain only Name.");
            Assert(GetNestedProperty(properties, "Geometry", "X") is not null &&
                   GetNestedProperty(properties, "Geometry", "Y") is not null &&
                   GetNestedProperty(properties, "Geometry", "Width") is not null &&
                   GetNestedProperty(properties, "Geometry", "Height") is not null &&
                   GetNestedProperty(properties, "Geometry", "Rotation") is not null &&
                   GetNestedProperty(properties, "Geometry", "Z") is null &&
                   GetNestedProperty(properties, "Geometry", "Position") is null &&
                   GetNestedProperty(properties, "Geometry", "Size") is null,
                $"{type} Geometry group must be a flat X/Y/Width/Height/Rotation list.");

        }

        var value = new WidgetDefinition
        {
            Type = WidgetTypeContract.Value,
            Id = "hierarchy-value-height",
            Width = 120f,
            Height = 999f
        };
        var valueProperties = CurrentPropertyView(
            value,
            effectiveSizeAccessor: _ => (120f, 37f),
            section: EditorPropertySection.General).GetProperties();
        var valueHeight = GetNestedProperty(valueProperties, "Geometry", "Height")
            ?? throw new InvalidOperationException("Value Height property is missing.");
        Assert(valueHeight.IsReadOnly,
            "Text/Value Height must be disabled because vertical size is derived.");
        Assert(Math.Abs(Convert.ToSingle(valueHeight.GetValue(null)) - 37f) < 0.001f,
            "Text/Value disabled Height must report effective layout height, not dormant storage.");

        var gauge = new WidgetDefinition
        {
            Type = WidgetTypeContract.Gauge,
            Id = "hierarchy-gauge-size",
            Width = 180f,
            Height = 180f
        };
        var gaugeProperties = CurrentPropertyView(
            gauge,
            section: EditorPropertySection.General).GetProperties();
        var gaugeWidth = GetNestedProperty(gaugeProperties, "Geometry", "Width")
            ?? throw new InvalidOperationException("Gauge Width property is missing.");
        var gaugeHeight = GetNestedProperty(gaugeProperties, "Geometry", "Height")
            ?? throw new InvalidOperationException("Gauge Height property is missing.");
        Assert(!gaugeWidth.IsReadOnly && gaugeHeight.IsReadOnly,
            "Gauge Width must be editable and Height must be disabled/derived.");
        Assert(Math.Abs(Convert.ToSingle(gaugeHeight.GetValue(null)) - 180f) < 0.001f,
            "Gauge derived Height must equal Width.");
    }

    private static void AppearancePropertyHierarchy()
    {
        var types = new[]
        {
            WidgetTypeContract.Value,
            WidgetTypeContract.Bar,
            WidgetTypeContract.Gauge,
            WidgetTypeContract.Image,
            WidgetTypeContract.Binary,
            WidgetTypeContract.Power,
            WidgetTypeContract.MediaSystem,
            WidgetTypeContract.MediaPlayer
        };

        foreach (var type in types)
        {
            var widget = CreateCanonicalPropertyTestWidget(
                type,
                $"appearance-{type}",
                type == WidgetTypeContract.Value ? 0f : 100f,
                type == WidgetTypeContract.Value ? 0f : 100f);

            var properties = CurrentPropertyView(
                widget,
                section: EditorPropertySection.Appearance).GetProperties();

            foreach (var group in new[] { "Background", "Border", "Shadow" })
            {
                var descriptor = properties[group]
                    ?? throw new InvalidOperationException($"{type} must expose Appearance/{group}.");
                Assert(descriptor is PropertyGroupPropertyDescriptor,
                    $"{type} Appearance/{group} must be a first-level editor group.");
            }

            Assert(GetNestedProperty(properties, "Background", nameof(WidgetDefinition.BackgroundColor)) is { DisplayName: "Color" },
                $"{type} Background must expose Color.");
            Assert(GetNestedProperty(properties, "Border", nameof(WidgetDefinition.BorderColor)) is { DisplayName: "Color" } &&
                   GetNestedProperty(properties, "Border", nameof(WidgetDefinition.BorderWidth)) is { DisplayName: "Width" } &&
                   GetNestedProperty(properties, "Border", nameof(WidgetDefinition.CornerRadius)) is { DisplayName: "Corner Radius" },
                $"{type} Border group is incomplete.");
            Assert(GetNestedProperty(properties, "Shadow", nameof(WidgetDefinition.ShadowEnabled)) is { DisplayName: "Enabled" } &&
                   GetNestedProperty(properties, "Shadow", nameof(WidgetDefinition.ShadowOffsetX)) is { DisplayName: "Offset X" } &&
                   GetNestedProperty(properties, "Shadow", nameof(WidgetDefinition.ShadowOffsetY)) is { DisplayName: "Offset Y" } &&
                   GetNestedProperty(properties, "Shadow", nameof(WidgetDefinition.ShadowBlur)) is { DisplayName: "Blur" } &&
                   GetNestedProperty(properties, "Shadow", nameof(WidgetDefinition.ShadowOpacity)) is { DisplayName: "Opacity" } &&
                   GetNestedProperty(properties, "Shadow", nameof(WidgetDefinition.ShadowColor)) is { DisplayName: "Color" },
                $"{type} Shadow group is incomplete.");

            var expectsForeground =
                WidgetTypeContract.Is(type, WidgetTypeContract.Value) ||
                WidgetTypeContract.Is(type, WidgetTypeContract.Bar) ||
                WidgetTypeContract.Is(type, WidgetTypeContract.Gauge) ||
                WidgetTypeContract.Is(type, WidgetTypeContract.Binary) ||
                WidgetTypeContract.Is(type, WidgetTypeContract.Power) ||
                WidgetTypeContract.Is(type, WidgetTypeContract.MediaSystem) ||
                WidgetTypeContract.Is(type, WidgetTypeContract.MediaPlayer);
            Assert((properties["Foreground"] is not null) == expectsForeground,
                $"{type} Foreground visibility must follow actual universal Color usage.");
            if (expectsForeground)
            {
                Assert(GetNestedProperty(properties, "Foreground", nameof(WidgetDefinition.Color)) is { DisplayName: "Color" },
                    $"{type} Foreground must expose Color.");
            }

            var expectsThresholds =
                WidgetTypeContract.Is(type, WidgetTypeContract.Bar) ||
                WidgetTypeContract.Is(type, WidgetTypeContract.Gauge);
            Assert((properties[nameof(WidgetDefinition.ThresholdMode)] is not null) == expectsThresholds,
                $"{type} Threshold Mode visibility is incorrect.");
            if (expectsThresholds)
            {
                Assert(properties[nameof(WidgetDefinition.ThresholdMode)] is { DisplayName: "Threshold Mode" },
                    $"{type} must expose the global Threshold Mode immediately before the numbered threshold groups.");
            }
            foreach (var group in new[] { "Threshold 1", "Threshold 2", "Threshold 3" })
            {
                Assert((properties[group] is not null) == expectsThresholds,
                    $"{type} {group} visibility is incorrect.");
            }

            if (expectsThresholds)
            {
                var threshold1Value = GetNestedProperty(properties, "Threshold 1", nameof(WidgetDefinition.Threshold1Value));
                var threshold1Color = GetNestedProperty(properties, "Threshold 1", nameof(WidgetDefinition.Threshold1Color));
                Assert(GetNestedProperty(properties, "Threshold 1", nameof(WidgetDefinition.Threshold1Enabled)) is { DisplayName: "Enabled" } &&
                       threshold1Value is { DisplayName: "Value", IsReadOnly: true } &&
                       threshold1Color is { DisplayName: "Color", IsReadOnly: true } &&
                       threshold1Value.Description == "Not applicable for selected Enabled." &&
                       threshold1Color.Description == "Not applicable for selected Enabled.",
                    $"{type} disabled Threshold 1 must keep Value and Color visible but disabled.");
            }

            Assert(properties["Alignment"] is null &&
                   properties["Font"] is null &&
                   properties["Outline"] is null &&
                   properties["Overflow"] is null,
                $"{type} text presentation groups must not remain on Appearance.");
        }

        var imageIcon = new WidgetDefinition
        {
            Type = WidgetTypeContract.Image,
            Id = "appearance-image-icon",
            SourceType = "icon",
            Width = 100f,
            Height = 100f
        };
        var imageIconAppearance = CurrentPropertyView(
            imageIcon,
            section: EditorPropertySection.Appearance).GetProperties();
        Assert(GetNestedProperty(imageIconAppearance, "Foreground", nameof(WidgetDefinition.Color)) is { DisplayName: "Color" },
            "Icon Image must expose universal Color as Appearance/Foreground/Color.");

        var gauge = new WidgetDefinition
        {
            Type = WidgetTypeContract.Gauge,
            Id = "appearance-gauge-threshold-enabled",
            Width = 100f,
            Height = 100f,
            Threshold1Enabled = true
        };
        var gaugeAppearance = CurrentPropertyView(
            gauge,
            section: EditorPropertySection.Appearance).GetProperties();
        Assert(GetNestedProperty(gaugeAppearance, "Threshold 1", nameof(WidgetDefinition.Threshold1Enabled)) is { DisplayName: "Enabled" } &&
               GetNestedProperty(gaugeAppearance, "Threshold 1", nameof(WidgetDefinition.Threshold1Value)) is { DisplayName: "Value" } &&
               GetNestedProperty(gaugeAppearance, "Threshold 1", nameof(WidgetDefinition.Threshold1Color)) is { DisplayName: "Color" },
            "Enabled Threshold 1 must expose Enabled, Value, and Color.");

        Assert(GetNestedProperty(gaugeAppearance, "Foreground", nameof(WidgetDefinition.Color)) is { DisplayName: "Color" },
            "Gauge base indicator color must be universal Appearance/Foreground/Color.");

        var barImage = new WidgetDefinition
        {
            Type = WidgetTypeContract.Bar,
            Id = "appearance-bar-image",
            Width = 100f,
            Height = 20f,
            BarContentMode = BarImageContract.ContentModeImage
        };
        var barImageAppearance = CurrentPropertyView(
            barImage,
            section: EditorPropertySection.Appearance).GetProperties();
        Assert(barImageAppearance["Foreground"] is null &&
               barImageAppearance[nameof(WidgetDefinition.ThresholdMode)] is null &&
               barImageAppearance["Threshold 1"] is null &&
               barImageAppearance["Threshold 2"] is null &&
               barImageAppearance["Threshold 3"] is null,
            "Image-mode Bar must hide Foreground and the threshold subsystem on Appearance.");

        var binary = new WidgetDefinition
        {
            Type = WidgetTypeContract.Binary,
            Id = "appearance-binary-value",
            Metric = RuntimeMetricContract.Fps,
            Width = 100f,
            Height = 40f
        };
        binary.EnsureStateVisualProfiles();
        binary.Profiles![BinarySignalContract.TrueKey].SourceType = StateVisualProfileContract.SourceValue;
        var binaryAppearance = CurrentPropertyView(
            binary,
            section: EditorPropertySection.Appearance).GetProperties();
        Assert(binaryAppearance["Foreground"] is not null &&
               binaryAppearance["Alignment"] is null &&
               binaryAppearance["Font"] is null &&
               binaryAppearance["Outline"] is null &&
               binaryAppearance["Overflow"] is null,
            "Binary value visual must keep universal Foreground fallback while text presentation groups stay off Appearance.");
    }

    private static void DataPropertyHierarchy()
    {
        var value = new WidgetDefinition
        {
            Type = WidgetTypeContract.Value,
            Id = "data-value",
            ValueSource = TextValueContract.SourceMetric,
            Metric = SystemMetricContract.Uptime,
            Width = 100f
        };
        var valueData = CurrentPropertyView(value, section: EditorPropertySection.Data).GetProperties();
        Assert(valueData.Count == 2 &&
               valueData["Value"] is PropertyGroupPropertyDescriptor &&
               valueData["Display"] is PropertyGroupPropertyDescriptor,
            "Metric Text/Value Data must be grouped as Value and Display.");
        Assert(GetNestedProperty(valueData, "Value", nameof(WidgetDefinition.SourceUnit)) is { IsReadOnly: true } sourceUnit &&
               string.Equals(Convert.ToString(sourceUnit.GetValue(null)), "seconds", StringComparison.Ordinal) &&
               GetNestedProperty(valueData, "Value", nameof(WidgetDefinition.Unit)) is not null &&
               GetNestedProperty(valueData, "Value", nameof(WidgetDefinition.Format)) is not null,
            "Data/Value must expose Source Unit, Unit, and Format for a numeric metric.");
        Assert(GetNestedProperty(valueData, "Display", nameof(WidgetDefinition.Prefix)) is not null &&
               GetNestedProperty(valueData, "Display", nameof(WidgetDefinition.Suffix)) is not null &&
               GetNestedProperty(valueData, "Display", nameof(WidgetDefinition.Fallback)) is not null,
            "Data/Display must expose Prefix, Suffix, and Fallback for Text/Value.");


        foreach (var type in new[] { WidgetTypeContract.Bar, WidgetTypeContract.Gauge })
        {
            var widget = new WidgetDefinition
            {
                Type = type,
                Id = $"data-{type}",
                Metric = SystemMetricContract.Uptime,
                Width = 100f,
                Height = 100f,
                Min = 0,
                Max = 100
            };
            var data = CurrentPropertyView(widget, section: EditorPropertySection.Data).GetProperties();
            Assert(data.Count == 2 &&
                   data["Value"] is PropertyGroupPropertyDescriptor &&
                   data["Range"] is PropertyGroupPropertyDescriptor,
                $"{type} Data must be grouped as Value and Range.");
            Assert(GetNestedProperty(data, "Value", nameof(WidgetDefinition.SourceUnit)) is { IsReadOnly: true } &&
                   GetNestedProperty(data, "Value", nameof(WidgetDefinition.Unit)) is not null &&
                   GetNestedProperty(data, "Value", nameof(WidgetDefinition.Format)) is null,
                $"{type} Data/Value must expose provider Source Unit and converted Unit without text Format.");
            Assert(GetNestedProperty(data, "Range", nameof(WidgetDefinition.Min)) is { DisplayName: "Min" } &&
                   GetNestedProperty(data, "Range", nameof(WidgetDefinition.Max)) is { DisplayName: "Max" },
                $"{type} Data/Range must expose Min and Max.");
            Assert(data["Display"] is null && data["Evaluation"] is null,
                $"{type} must not expose text Display or Binary Evaluation groups.");

        }

        var binary = new WidgetDefinition
        {
            Type = WidgetTypeContract.Binary,
            Id = "data-binary-evaluation",
            Metric = SystemMetricContract.Uptime,
            EvaluationMode = BinarySignalContract.EvaluationModeSetpoint,
            Width = 100f,
            Height = 40f
        };
        binary.EnsureStateVisualProfiles();
        var binaryData = CurrentPropertyView(binary, section: EditorPropertySection.Data).GetProperties();
        Assert(binaryData["Evaluation"] is PropertyGroupPropertyDescriptor,
            "Binary Data must expose Evaluation as a first-level group.");
        var evaluationOwner = binaryData["Evaluation"]!.GetValue(null)
            ?? throw new InvalidOperationException("Binary Evaluation group has no value.");
        var evaluationProperties = TypeDescriptor.GetProperties(evaluationOwner)
            .Cast<PropertyDescriptor>()
            .ToArray();
        Assert(evaluationProperties.Select(property => property.DisplayName)
                   .SequenceEqual(new[] { "Mode", "True If", "Setpoint" }, StringComparer.Ordinal),
            "Binary Evaluation properties must be ordered Mode, True If, Setpoint.");
        Assert(evaluationProperties[0].Name == nameof(WidgetDefinition.EvaluationMode) &&
               evaluationProperties[1].Name == nameof(WidgetDefinition.TrueIf) &&
               evaluationProperties[2].Name == nameof(WidgetDefinition.Setpoint),
            "Binary Evaluation property identity/order is incorrect.");

        var literalText = new WidgetDefinition
        {
            Type = WidgetTypeContract.Value,
            Id = "data-literal-numeric",
            ValueSource = TextValueContract.SourceText,
            Text = "123",
            Width = 100f
        };
        var literalData = CurrentPropertyView(literalText, section: EditorPropertySection.Data).GetProperties();
        Assert(literalData.Count == 2 &&
               GetNestedProperty(literalData, "Value", nameof(WidgetDefinition.SourceUnit)) is { IsReadOnly: false } &&
               GetNestedProperty(literalData, "Value", nameof(WidgetDefinition.Unit)) is { IsReadOnly: false } &&
               GetNestedProperty(literalData, "Value", nameof(WidgetDefinition.Format)) is { IsReadOnly: false } &&
               GetNestedProperty(literalData, "Display", nameof(WidgetDefinition.Prefix)) is { IsReadOnly: false } &&
               GetNestedProperty(literalData, "Display", nameof(WidgetDefinition.Suffix)) is { IsReadOnly: false } &&
               GetNestedProperty(literalData, "Display", nameof(WidgetDefinition.Fallback)) is { IsReadOnly: true },
            "Numeric literal Text must expose grouped Value/Display data while keeping Fallback unavailable.");

        var image = new WidgetDefinition
        {
            Type = WidgetTypeContract.Image,
            Id = "data-image",
            Width = 100f,
            Height = 100f
        };
        var imageData = CurrentPropertyView(image, section: EditorPropertySection.Data).GetProperties();
        Assert(imageData.Count == 0,
            "Image must not expose an empty Data surface.");
    }

    private static void GaugePropertyHierarchy()
    {
        var gauge = new WidgetDefinition
        {
            Type = WidgetTypeContract.Gauge,
            Id = "gauge-tab-hierarchy",
            Metric = RuntimeMetricContract.Fps,
            Width = 100f,
            Height = 100f
        };

        var gaugeProperties = CurrentPropertyView(
            gauge,
            section: EditorPropertySection.Gauge).GetProperties();

        Assert(gaugeProperties.Count == 2 &&
               gaugeProperties["Horseshoe"] is PropertyGroupPropertyDescriptor &&
               gaugeProperties["Needle"] is PropertyGroupPropertyDescriptor,
            "Gauge tab must expose exactly Horseshoe and Needle as first-level groups.");

        var horseshoeOwner = gaugeProperties["Horseshoe"]!.GetValue(null)
            ?? throw new InvalidOperationException("Gauge Horseshoe group has no value.");
        var horseshoe = TypeDescriptor.GetProperties(horseshoeOwner)
            .Cast<PropertyDescriptor>()
            .ToArray();
        Assert(horseshoe.Select(property => property.DisplayName).SequenceEqual(
            new[]
            {
                "Enabled", "Reverse", "Start Angle", "End Angle", "Thickness",
                "Background Color", "Border Width", "Border Color", "Corner Radius", "Gap"
            },
            StringComparer.Ordinal),
            "Gauge Horseshoe properties are incomplete or out of order.");
        Assert(horseshoe.Select(property => property.Name).SequenceEqual(
            new[]
            {
                nameof(WidgetDefinition.TrackEnabled),
                nameof(WidgetDefinition.Reverse),
                nameof(WidgetDefinition.StartAngle),
                nameof(WidgetDefinition.EndAngle),
                nameof(WidgetDefinition.Thickness),
                nameof(WidgetDefinition.TrackBackgroundColor),
                nameof(WidgetDefinition.TrackBorderWidth),
                nameof(WidgetDefinition.TrackBorderColor),
                nameof(WidgetDefinition.TrackCornerRadius),
                nameof(WidgetDefinition.Gap)
            },
            StringComparer.Ordinal),
            "Gauge Horseshoe property identity/order is incorrect.");

        var needleOwner = gaugeProperties["Needle"]!.GetValue(null)
            ?? throw new InvalidOperationException("Gauge Needle group has no value.");
        var needle = TypeDescriptor.GetProperties(needleOwner)
            .Cast<PropertyDescriptor>()
            .ToArray();
        Assert(needle.Select(property => property.DisplayName).SequenceEqual(
            new[]
            {
                "Enabled", "Thickness", "Color", "Start Offset", "End Offset",
                "Pointer Length", "Pointer Thickness", "Pointer Color"
            },
            StringComparer.Ordinal),
            "Gauge Needle properties are incomplete or out of order.");
        Assert(needle.Select(property => property.Name).SequenceEqual(
            new[]
            {
                nameof(WidgetDefinition.NeedleEnabled),
                nameof(WidgetDefinition.NeedleThickness),
                nameof(WidgetDefinition.NeedleColor),
                nameof(WidgetDefinition.NeedleStartOffset),
                nameof(WidgetDefinition.NeedleEndOffset),
                nameof(WidgetDefinition.NeedlePointerLength),
                nameof(WidgetDefinition.NeedlePointerThickness),
                nameof(WidgetDefinition.NeedlePointerColor)
            },
            StringComparer.Ordinal),
            "Gauge Needle property identity/order is incorrect.");


        var bar = new WidgetDefinition
        {
            Type = WidgetTypeContract.Bar,
            Id = "gauge-tab-bar",
            Metric = RuntimeMetricContract.Fps,
            Width = 100f,
            Height = 20f
        };
        var barGauge = CurrentPropertyView(
            bar,
            section: EditorPropertySection.Gauge).GetProperties();
        Assert(barGauge.Count == 1 &&
               barGauge["Bar"] is PropertyGroupPropertyDescriptor &&
               GetNestedProperty(barGauge, "Bar", nameof(WidgetDefinition.BarContentMode)) is { DisplayName: "Mode" } &&
               GetNestedProperty(barGauge, "Bar", nameof(WidgetDefinition.Reverse)) is { DisplayName: "Reverse" } &&
               GetNestedProperty(barGauge, "Bar", nameof(WidgetDefinition.Gap)) is { DisplayName: "Gap" },
            "Bar must expose Gauge > Bar > Mode, Reverse, and Gap.");

    }

    private static void StatesPropertyHierarchy()
    {
        var binary = new WidgetDefinition
        {
            Type = WidgetTypeContract.Binary,
            Id = "states-tab-binary",
            Metric = RuntimeMetricContract.Fps,
            Fit = "contain",
            Loop = false,
            Width = 100f,
            Height = 40f
        };
        binary.EnsureStateVisualProfiles();

        static string StateProperty(string key, StateVisualProfileField field) =>
            $"{StateVisualProfilePropertyDescriptor.PropertyPrefix}{key}:{field}";

        var trueSourceType = StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.SourceType);
        var trueSource = StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.Source);
        var trueColor = StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.Color);
        var trueOpacity = StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.Opacity);
        var falseSourceType = StateProperty(BinarySignalContract.FalseKey, StateVisualProfileField.SourceType);
        var falseSource = StateProperty(BinarySignalContract.FalseKey, StateVisualProfileField.Source);
        var falseColor = StateProperty(BinarySignalContract.FalseKey, StateVisualProfileField.Color);
        var falseOpacity = StateProperty(BinarySignalContract.FalseKey, StateVisualProfileField.Opacity);

        var states = CurrentPropertyView(
            binary,
            section: EditorPropertySection.States).GetProperties();

        Assert(states.Count == 2 &&
               states["State: True"] is PropertyGroupPropertyDescriptor &&
               states["State: False"] is PropertyGroupPropertyDescriptor,
            "Binary States tab must expose exactly State: True and State: False as first-level groups.");

        Assert(GetNestedProperty(states, "State: True", trueSourceType) is { DisplayName: "Source Type" } &&
               GetNestedProperty(states, "State: True", trueSource) is { DisplayName: "Source" } &&
               GetNestedProperty(states, "State: False", falseSourceType) is { DisplayName: "Source Type" } &&
               GetNestedProperty(states, "State: False", falseSource) is { DisplayName: "Source" },
            "Icon-backed Binary states must expose Source Type and Source in their corresponding States groups.");

        binary.Profiles![BinarySignalContract.TrueKey].SourceType = StateVisualProfileContract.SourceValue;
        states = CurrentPropertyView(
            binary,
            section: EditorPropertySection.States).GetProperties();

        var trueValueSource = GetNestedProperty(states, "State: True", trueSource);
        Assert(GetNestedProperty(states, "State: True", trueSourceType) is not null &&
               trueValueSource is { IsReadOnly: true } &&
               trueValueSource.Description == "Not applicable for selected Source Type." &&
               GetNestedProperty(states, "State: False", falseSourceType) is not null &&
               GetNestedProperty(states, "State: False", falseSource) is { IsReadOnly: false },
            "Value-backed Binary state must keep Source visible but disabled without affecting the sibling state.");

        binary.Profiles[BinarySignalContract.TrueKey].SourceType = StateVisualProfileContract.SourceFile;
        binary.Profiles[BinarySignalContract.TrueKey].Source = "state.gif";

        var power = new WidgetDefinition
        {
            Type = WidgetTypeContract.Power,
            Id = "states-tab-power",
            Source = PowerMetricContract.UpsSource,
            Width = 40f,
            Height = 40f
        };
        power.EnsureStateVisualProfiles();
        var powerStates = CurrentPropertyView(
            power,
            section: EditorPropertySection.States).GetProperties();
        Assert(powerStates.Count == PowerMetricContract.States.Length &&
               powerStates["State: Online"] is PropertyGroupPropertyDescriptor &&
               powerStates["State: Unavailable"] is PropertyGroupPropertyDescriptor,
            "Power States tab must expose all Power state profiles.");

        var powerSpec = StateVisualProfileContract.GetSpecs(WidgetTypeContract.Power)[0];
        var powerSourceType = StateProperty(powerSpec.Key, StateVisualProfileField.SourceType);
        var powerSource = StateProperty(powerSpec.Key, StateVisualProfileField.Source);
        Assert(GetNestedProperty(powerStates, powerSpec.Category, powerSourceType) is { DisplayName: "Source Type" } &&
               GetNestedProperty(powerStates, powerSpec.Category, powerSource) is { DisplayName: "Source" },
            "Icon-backed Power states must expose Source Type and Source in States.");

    }

    private static void ImagePropertyHierarchy()
    {
        var imageWidget = new C.ImageWidgetDefinition
        {
            Id = "image-tab-file",
            Asset = new C.ImageAssetPresentationDefinition
            {
                SourceType = C.ImageAssetSourceType.File,
                Source = "example.png",
                Fit = StateVisualProfileContract.FitContain,
                Loop = true
            },
            Opacity = 0.75f,
            Width = 100f,
            Height = 100f
        };

        var imageProperties = new EditorPropertyView(imageWidget, section: EditorPropertySection.Image).GetProperties();
        Assert(imageProperties["Image"] is PropertyGroupPropertyDescriptor,
            "Image widget must expose Image as a first-level Image-tab group.");
        Assert(GetNestedProperty(imageProperties, "Image", "Fit") is { DisplayName: "Fit" } &&
               GetNestedProperty(imageProperties, "Image", "Loop") is { DisplayName: "Loop" } &&
               GetNestedProperty(imageProperties, "Image", nameof(C.ImageWidgetDefinition.Opacity)) is { DisplayName: "Opacity" },
            "File-backed Image widget Image group must expose Fit, Loop, and Opacity.");

        imageWidget.Asset.ChangeSourceType(C.ImageAssetSourceType.Icon);
        imageWidget.Asset.Source = "activity";
        var iconProperties = new EditorPropertyView(imageWidget, section: EditorPropertySection.Image).GetProperties();
        var iconLoop = GetNestedProperty(iconProperties, "Image", "Loop");
        Assert(GetNestedProperty(iconProperties, "Image", "Fit") is not null &&
               GetNestedProperty(iconProperties, "Image", nameof(C.ImageWidgetDefinition.Opacity)) is not null &&
               iconLoop is { IsReadOnly: true } &&
               iconLoop.Description == "Not applicable for selected Source.",
            "Icon-backed Image widget must keep Loop visible but disabled.");

        var bar = new C.BarWidgetDefinition
        {
            Id = "image-tab-bar",
            Metric = RuntimeMetricContract.Fps,
            Width = 100f,
            Height = 20f,
            Min = 0,
            Max = 100,
            ContentMode = BarImageContract.ContentModeImage,
            Image = new C.BarImagePresentationDefinition
            {
                Source = "bar.png",
                Fit = BarImageContract.ImageFitContain,
                ProgressMode = BarImageContract.ProgressModeReveal,
                Loop = true
            }
        };

        var barImageProperties = new EditorPropertyView(bar, section: EditorPropertySection.Image).GetProperties();
        Assert(barImageProperties["Image"] is PropertyGroupPropertyDescriptor,
            "Image-mode Bar must expose Image as a first-level Image-tab group.");
        Assert(GetNestedProperty(barImageProperties, "Image", "BarImageSource") is { DisplayName: "Source" } &&
               GetNestedProperty(barImageProperties, "Image", "BarImageFit") is { DisplayName: "Fit" } &&
               GetNestedProperty(barImageProperties, "Image", "BarProgressMode") is { DisplayName: "Progress Mode" } &&
               GetNestedProperty(barImageProperties, "Image", "Loop") is { DisplayName: "Loop" },
            "Image-mode Bar Image group must expose Source, Fit, Progress Mode, and Loop.");

        var barGauge = new EditorPropertyView(bar, section: EditorPropertySection.Gauge).GetProperties();
        Assert(GetNestedProperty(barGauge, "Bar", "BarContentMode") is { DisplayName: "Mode" },
            "Bar Gauge tab must retain Mode.");

        bar.ContentMode = BarImageContract.ContentModeFill;
        var fillImageProperties = new EditorPropertyView(bar, section: EditorPropertySection.Image).GetProperties();
        Assert(fillImageProperties.Count == 0,
            "Fill-mode Bar must have no Image section, allowing the Image tab to be omitted while dormant image settings remain in the model.");
        Assert(bar.Image.Source == "bar.png" && bar.Image.Fit == BarImageContract.ImageFitContain,
            "Switching Bar to Fill must preserve dormant canonical Bar image configuration.");

        var binary = new C.BinaryWidgetDefinition
        {
            Id = "image-tab-binary-stage3",
            Metric = RuntimeMetricContract.Fps,
            Width = 100f,
            Height = 40f
        };
        TextOverflowStateContract.EnsureProfiles(binary);
        var trueProfile = binary.Profiles[BinarySignalContract.TrueKey];
        trueProfile.ContentType = C.StateContentType.Image;
        trueProfile.Asset.SourceType = C.ImageAssetSourceType.File;
        trueProfile.Asset.Source = "binary-state.gif";
        trueProfile.Asset.Fit = StateVisualProfileContract.FitCover;
        trueProfile.Asset.Loop = false;
        trueProfile.Opacity = 0.75f;

        var falseProfile = binary.Profiles[BinarySignalContract.FalseKey];
        falseProfile.ContentType = C.StateContentType.Image;
        falseProfile.Asset.SourceType = C.ImageAssetSourceType.Icon;
        falseProfile.Asset.Source = "lucide:check";
        falseProfile.Asset.Fit = StateVisualProfileContract.FitContain;
        falseProfile.Asset.Loop = false;
        falseProfile.Color = "#FF00FF00";
        falseProfile.Opacity = 0.5f;

        static string StateProperty(string key, StateVisualProfileField field) =>
            $"{StateVisualProfilePropertyDescriptor.PropertyPrefix}{key}:{field}";

        var trueFit = StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.Fit);
        var trueLoop = StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.Loop);
        var trueOpacity = StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.Opacity);
        var trueColor = StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.Color);
        var falseFit = StateProperty(BinarySignalContract.FalseKey, StateVisualProfileField.Fit);
        var falseLoop = StateProperty(BinarySignalContract.FalseKey, StateVisualProfileField.Loop);
        var falseOpacity = StateProperty(BinarySignalContract.FalseKey, StateVisualProfileField.Opacity);
        var falseColor = StateProperty(BinarySignalContract.FalseKey, StateVisualProfileField.Color);

        var binaryImage = new EditorPropertyView(binary, section: EditorPropertySection.Image).GetProperties();
        Assert(binaryImage.Count == 2 &&
               binaryImage["State: True"] is PropertyGroupPropertyDescriptor &&
               binaryImage["State: False"] is PropertyGroupPropertyDescriptor,
            "Binary Image tab must expose state-specific first-level groups for active image/icon states.");
        var trueFileColor = GetNestedProperty(binaryImage, "State: True", trueColor);
        Assert(GetNestedProperty(binaryImage, "State: True", trueFit) is { DisplayName: "Fit" } &&
               GetNestedProperty(binaryImage, "State: True", trueLoop) is { DisplayName: "Loop", IsReadOnly: false } &&
               GetNestedProperty(binaryImage, "State: True", trueOpacity) is { DisplayName: "Opacity" } &&
               trueFileColor is { DisplayName: "Color", IsReadOnly: true } &&
               trueFileColor.Description == "Not applicable for selected Source Type.",
            "File-backed Binary state Image group must keep Color visible but disabled.");
        var falseIconLoop = GetNestedProperty(binaryImage, "State: False", falseLoop);
        Assert(GetNestedProperty(binaryImage, "State: False", falseFit) is { DisplayName: "Fit" } &&
               falseIconLoop is { DisplayName: "Loop", IsReadOnly: true } &&
               falseIconLoop.Description == "Not applicable for selected Source." &&
               GetNestedProperty(binaryImage, "State: False", falseColor) is { DisplayName: "Color", IsReadOnly: false } &&
               GetNestedProperty(binaryImage, "State: False", falseOpacity) is { DisplayName: "Opacity" },
            "Icon-backed Binary state Image group must keep Loop visible but disabled.");

        var trueFitDescriptor = GetNestedProperty(binaryImage, "State: True", trueFit)
            ?? throw new InvalidOperationException("Binary True Fit property is missing.");
        var falseFitDescriptor = GetNestedProperty(binaryImage, "State: False", falseFit)
            ?? throw new InvalidOperationException("Binary False Fit property is missing.");
        trueFitDescriptor.SetValue(null, StateVisualProfileContract.FitStretch);
        falseFitDescriptor.SetValue(null, StateVisualProfileContract.FitCover);
        Assert(trueProfile.Asset.Fit == StateVisualProfileContract.FitStretch &&
               falseProfile.Asset.Fit == StateVisualProfileContract.FitCover,
            "Binary state Fit values must be independently editable.");

        var trueLoopDescriptor = GetNestedProperty(binaryImage, "State: True", trueLoop)
            ?? throw new InvalidOperationException("Binary True Loop property is missing.");
        trueLoopDescriptor.SetValue(null, true);
        Assert(trueProfile.Asset.Loop && !falseProfile.Asset.Loop,
            "Editing Binary True Loop must not overwrite the sibling state Loop value.");

        trueProfile.ContentType = C.StateContentType.Value;
        var mixedBinaryImage = new EditorPropertyView(binary, section: EditorPropertySection.Image).GetProperties();
        Assert(mixedBinaryImage.Count == 1 &&
               mixedBinaryImage["State: True"] is null &&
               mixedBinaryImage["State: False"] is PropertyGroupPropertyDescriptor,
            "Binary Image tab must omit value-backed states while preserving image/icon-backed sibling states.");
    }

    private static void BinaryStateImageSchema()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-binary-image-schema-regression");
        try
        {
            var dashboardPath = Path.Combine(tempRoot, "dashboards", "binary-image-schema.json");

            static string ProfilesJson(bool includeFitLoop) => includeFitLoop
                ? """
                  "profiles": {
                    "true":  { "sourceType": "icon", "source": "lucide:check", "color": "#FFFFFFFF", "opacity": 1.0, "fit": "contain", "loop": false, "text": { "fontFamily": "Roboto", "fontSize": 32, "fontWeight": 400, "italic": false, "align": "left", "verticalAlign": "baseline", "outlineWidth": 0, "outlineColor": "#FF000000", "overflowMode": "Clip", "scrollSpeed": 0, "bumpPauseMs": 500 } },
                    "false": { "sourceType": "icon", "source": "lucide:check", "color": "#FFFFFFFF", "opacity": 0.5, "fit": "cover", "loop": true, "text": { "fontFamily": "Roboto", "fontSize": 32, "fontWeight": 400, "italic": false, "align": "left", "verticalAlign": "baseline", "outlineWidth": 0, "outlineColor": "#FF000000", "overflowMode": "Clip", "scrollSpeed": 0, "bumpPauseMs": 500 } }
                  }
                  """
                : """
                  "profiles": {
                    "true":  { "sourceType": "icon", "source": "lucide:check", "color": "#FFFFFFFF", "opacity": 1.0, "text": { "fontFamily": "Roboto", "fontSize": 32, "fontWeight": 400, "italic": false, "align": "left", "verticalAlign": "baseline", "outlineWidth": 0, "outlineColor": "#FF000000", "overflowMode": "Clip", "scrollSpeed": 0, "bumpPauseMs": 500 } },
                    "false": { "sourceType": "icon", "source": "lucide:check", "color": "#FFFFFFFF", "opacity": 0.5, "text": { "fontFamily": "Roboto", "fontSize": 32, "fontWeight": 400, "italic": false, "align": "left", "verticalAlign": "baseline", "outlineWidth": 0, "outlineColor": "#FF000000", "overflowMode": "Clip", "scrollSpeed": 0, "bumpPauseMs": 500 } }
                  }
                  """;

            var canonical = DashboardDefinition.Parse(
                $$"""
                {
                  "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                  "canvas": { "width": 1920, "height": 480, "orientation": 0, "backgroundColor": "#000000" },
                  "widgets": [
                    {
                      "type": "binary",
                      "id": "binary-schema11",
                      "width": 96,
                      "height": 96,
                      "metric": "{{RuntimeMetricContract.Fps}}",
                      {{ProfilesJson(includeFitLoop: true)}}
                    }
                  ]
                }
                """,
                dashboardPath);

            var binary = canonical.Widgets.Single();
            Assert(binary.Profiles![BinarySignalContract.TrueKey].Fit == StateVisualProfileContract.FitContain &&
                   binary.Profiles[BinarySignalContract.TrueKey].Loop == false &&
                   binary.Profiles[BinarySignalContract.FalseKey].Fit == StateVisualProfileContract.FitCover &&
                   binary.Profiles[BinarySignalContract.FalseKey].Loop == true,
                "Schema 11 Binary must deserialize independent per-state Fit/Loop values.");

            var serializedProfiles = JsonSerializer.SerializeToNode(binary.Profiles) as JsonObject
                ?? throw new InvalidOperationException("Could not serialize Binary state profiles.");
            foreach (var key in new[] { BinarySignalContract.TrueKey, BinarySignalContract.FalseKey })
            {
                var profile = serializedProfiles[key] as JsonObject
                    ?? throw new InvalidOperationException($"Serialized Binary profile '{key}' is missing.");
                Assert(profile.ContainsKey("fit") && profile.ContainsKey("loop"),
                    "Schema 11 Binary profile serialization must persist Fit/Loop explicitly.");
            }

            var newBinary = new WidgetDefinition { Type = WidgetTypeContract.Binary };
            newBinary.EnsureStateVisualProfiles();
            foreach (var profile in newBinary.Profiles!.Values)
            {
                Assert(profile.Fit == StateVisualProfileContract.FitContain && profile.Loop == true,
                    "New schema 11 Binary profiles must default to Fit=contain and Loop=true.");
            }

            var power = new WidgetDefinition { Type = WidgetTypeContract.Power };
            power.EnsureStateVisualProfiles();
            var serializedPowerProfiles = JsonSerializer.SerializeToNode(power.Profiles) as JsonObject
                ?? throw new InvalidOperationException("Could not serialize Power state profiles.");
            Assert(serializedPowerProfiles.All(pair =>
                       pair.Value is JsonObject profile &&
                       profile["fit"]?.GetValue<string>() == StateVisualProfileContract.FitContain &&
                       profile["loop"]?.GetValue<bool>() == true),
                "Power state profiles must persist standard Fit/Loop presentation in schema 14.");

            var obsoleteTopLevelRejected = false;
            try
            {
                _ = DashboardDefinition.Parse(
                    $$"""
                    {
                      "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                      "canvas": { "width": 1920, "height": 480, "orientation": 0, "backgroundColor": "#000000" },
                      "widgets": [
                        {
                          "type": "binary",
                          "id": "binary-obsolete-fit-loop",
                          "width": 96,
                          "height": 96,
                          "metric": "{{RuntimeMetricContract.Fps}}",
                          "fit": "contain",
                          "loop": true,
                          {{ProfilesJson(includeFitLoop: true)}}
                        }
                      ]
                    }
                    """,
                    dashboardPath);
            }
            catch (InvalidDataException)
            {
                obsoleteTopLevelRejected = true;
            }
            Assert(obsoleteTopLevelRejected,
                "Schema 11 must reject obsolete top-level Binary Fit/Loop.");

            var missingProfileImageSettingsRejected = false;
            try
            {
                _ = DashboardDefinition.Parse(
                    $$"""
                    {
                      "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                      "canvas": { "width": 1920, "height": 480, "orientation": 0, "backgroundColor": "#000000" },
                      "widgets": [
                        {
                          "type": "binary",
                          "id": "binary-missing-state-fit-loop",
                          "width": 96,
                          "height": 96,
                          "metric": "{{RuntimeMetricContract.Fps}}",
                          {{ProfilesJson(includeFitLoop: false)}}
                        }
                      ]
                    }
                    """,
                    dashboardPath);
            }
            catch (InvalidDataException)
            {
                missingProfileImageSettingsRejected = true;
            }
            Assert(missingProfileImageSettingsRejected,
                "Schema 11 Binary profiles must explicitly persist Fit/Loop for both states.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void BinaryStateTextSchema()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-binary-text-schema-regression");
        try
        {
            var dashboardPath = Path.Combine(tempRoot, "dashboards", "binary-text-schema.json");

            static JsonObject TextNode(
                float size,
                string align,
                string vertical,
                string overflow) => new()
            {
                ["fontFamily"] = "Roboto",
                ["fontSize"] = size,
                ["fontWeight"] = 400,
                ["italic"] = false,
                ["align"] = align,
                ["verticalAlign"] = vertical,
                ["outlineWidth"] = 0f,
                ["outlineColor"] = "#FF000000",
                ["overflowMode"] = overflow,
                ["scrollSpeed"] = 0f,
                ["bumpPauseMs"] = 500
            };

            static JsonObject ProfileNode(
                string sourceType,
                string source,
                float opacity,
                string fit,
                bool loop,
                JsonObject text) => new()
            {
                ["sourceType"] = sourceType,
                ["source"] = source,
                ["color"] = "#FFFFFFFF",
                ["opacity"] = opacity,
                ["fit"] = fit,
                ["loop"] = loop,
                ["text"] = text
            };

            var binaryNode = new JsonObject
            {
                ["type"] = WidgetTypeContract.Binary,
                ["id"] = "binary-state-text-schema",
                ["width"] = 120f,
                ["height"] = 40f,
                ["metric"] = RuntimeMetricContract.Fps,
                ["profiles"] = new JsonObject
                {
                    [BinarySignalContract.TrueKey] = ProfileNode(
                        StateVisualProfileContract.SourceValue,
                        string.Empty,
                        1f,
                        StateVisualProfileContract.FitContain,
                        true,
                        TextNode(36f, "center", "middle", ValueOverflowContract.Scroll)),
                    [BinarySignalContract.FalseKey] = ProfileNode(
                        StateVisualProfileContract.SourceValue,
                        string.Empty,
                        0.5f,
                        StateVisualProfileContract.FitCover,
                        false,
                        TextNode(18f, "right", "bottom", ValueOverflowContract.Ellipsis))
                }
            };
            var root = new JsonObject
            {
                ["schemaVersion"] = DashboardSchemaMigration.CanvasImageLayerSchemaVersion,
                ["canvas"] = new JsonObject
                {
                    ["width"] = FrameGeometry.DefaultNativeWidth,
                    ["height"] = FrameGeometry.DefaultNativeHeight,
                    ["orientation"] = 0,
                    ["backgroundColor"] = "#000000"
                },
                ["widgets"] = new JsonArray(binaryNode)
            };

            var definition = DashboardDefinition.Parse(root.ToJsonString(), dashboardPath);
            var binary = definition.Widgets.Single();
            var trueText = GetBinaryText(binary, BinarySignalContract.TrueKey);
            var falseText = GetBinaryText(binary, BinarySignalContract.FalseKey);
            Assert(trueText.FontSize == 36f &&
                   trueText.Align == "center" &&
                   trueText.VerticalAlign == "middle" &&
                   trueText.OverflowMode == ValueOverflowContract.Scroll &&
                   falseText.FontSize == 18f &&
                   falseText.Align == "right" &&
                   falseText.VerticalAlign == "bottom" &&
                   falseText.OverflowMode == ValueOverflowContract.Ellipsis,
                "Schema 11 Binary must deserialize independent per-state Text presentation.");

            var serializedProfiles = JsonSerializer.SerializeToNode(binary.Profiles) as JsonObject
                ?? throw new InvalidOperationException("Could not serialize Binary text profiles.");
            foreach (var key in new[] { BinarySignalContract.TrueKey, BinarySignalContract.FalseKey })
            {
                var profile = serializedProfiles[key] as JsonObject
                    ?? throw new InvalidOperationException($"Serialized Binary profile '{key}' is missing.");
                var text = profile["text"] as JsonObject
                    ?? throw new InvalidOperationException($"Serialized Binary profile '{key}' Text is missing.");
                foreach (var property in new[]
                {
                    "fontFamily", "fontSize", "fontWeight", "italic", "align", "verticalAlign",
                    "outlineWidth", "outlineColor", "overflowMode", "scrollSpeed", "bumpPauseMs"
                })
                {
                    Assert(text.ContainsKey(property),
                        $"Schema 11 Binary Text serialization is missing '{property}' in state '{key}'.");
                }
            }

            var newBinary = new WidgetDefinition { Type = WidgetTypeContract.Binary };
            newBinary.EnsureStateVisualProfiles();
            Assert(newBinary.Profiles!.Values.All(profile => profile.Text is not null),
                "New Binary profiles must include Text presentation for both states.");

            var power = new WidgetDefinition { Type = WidgetTypeContract.Power };
            power.EnsureStateVisualProfiles();
            Assert(power.Profiles!.Values.All(profile => profile.Text is not null),
                "Power state profiles must include latent Text presentation for Source Type=value.");

            var obsolete = JsonNode.Parse(root.ToJsonString()) as JsonObject
                ?? throw new InvalidOperationException("Could not clone Binary text schema document.");
            var obsoleteWidget = ((obsolete["widgets"] as JsonArray)![0] as JsonObject)!;
            obsoleteWidget["fontFamily"] = "Roboto";
            var obsoleteRejected = false;
            try
            {
                _ = DashboardDefinition.Parse(obsolete.ToJsonString(), dashboardPath);
            }
            catch (InvalidDataException)
            {
                obsoleteRejected = true;
            }
            Assert(obsoleteRejected,
                "Schema 11 must reject obsolete top-level Binary text presentation.");

            var missingText = JsonNode.Parse(root.ToJsonString()) as JsonObject
                ?? throw new InvalidOperationException("Could not clone Binary text schema document.");
            var missingWidget = ((missingText["widgets"] as JsonArray)![0] as JsonObject)!;
            var missingProfiles = (missingWidget["profiles"] as JsonObject)!;
            ((missingProfiles[BinarySignalContract.FalseKey] as JsonObject)!).Remove("text");
            var missingRejected = false;
            try
            {
                _ = DashboardDefinition.Parse(missingText.ToJsonString(), dashboardPath);
            }
            catch (InvalidDataException)
            {
                missingRejected = true;
            }
            Assert(missingRejected,
                "Schema 11 Binary profiles must explicitly persist Text presentation for both states.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void BinaryStateTextIndependence()
    {
        var widget = new WidgetDefinition
        {
            Type = WidgetTypeContract.Binary,
            Id = "binary-state-text-independence",
            Metric = RuntimeMetricContract.Fps,
            Width = 120f,
            Height = 40f
        };
        widget.EnsureStateVisualProfiles();
        widget.Profiles![BinarySignalContract.TrueKey].SourceType = StateVisualProfileContract.SourceValue;
        widget.Profiles![BinarySignalContract.FalseKey].SourceType = StateVisualProfileContract.SourceValue;
        TextOverflowStateContract.ApplyContextChange(widget, 120f, 32f);
        var currentWidget = (C.BinaryWidgetDefinition)CurrentWidget(widget);

        static string StateProperty(string key, StateVisualProfileField field) =>
            $"{StateVisualProfilePropertyDescriptor.PropertyPrefix}{key}:{field}";

        var textProperties = new EditorPropertyView(
            currentWidget,
            section: EditorPropertySection.Text).GetProperties();

        var trueFont = GetNestedProperty(
            textProperties, "State: True", "Font",
            StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.FontSize))
            ?? throw new InvalidOperationException("Binary True Font Size property is missing.");
        var falseFont = GetNestedProperty(
            textProperties, "State: False", "Font",
            StateProperty(BinarySignalContract.FalseKey, StateVisualProfileField.FontSize))
            ?? throw new InvalidOperationException("Binary False Font Size property is missing.");
        var trueAlign = GetNestedProperty(
            textProperties, "State: True", "Alignment",
            StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.Align))
            ?? throw new InvalidOperationException("Binary True Alignment property is missing.");
        var falseAlign = GetNestedProperty(
            textProperties, "State: False", "Alignment",
            StateProperty(BinarySignalContract.FalseKey, StateVisualProfileField.Align))
            ?? throw new InvalidOperationException("Binary False Alignment property is missing.");
        var trueOverflow = GetNestedProperty(
            textProperties, "State: True", "Overflow",
            StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.OverflowMode))
            ?? throw new InvalidOperationException("Binary True Overflow property is missing.");
        var falseOverflow = GetNestedProperty(
            textProperties, "State: False", "Overflow",
            StateProperty(BinarySignalContract.FalseKey, StateVisualProfileField.OverflowMode))
            ?? throw new InvalidOperationException("Binary False Overflow property is missing.");

        trueFont.SetValue(null, 46f);
        falseFont.SetValue(null, 18f);
        trueAlign.SetValue(null, "center");
        falseAlign.SetValue(null, "right");
        trueOverflow.SetValue(null, ValueOverflowContract.Scroll);
        falseOverflow.SetValue(null, ValueOverflowContract.Ellipsis);

        var trueText = currentWidget.Profiles[BinarySignalContract.TrueKey].TextPresentation;
        var falseText = currentWidget.Profiles[BinarySignalContract.FalseKey].TextPresentation;
        Assert(trueText.FontSize == 46f && falseText.FontSize == 18f &&
               trueText.Align == "center" && falseText.Align == "right" &&
               trueText.OverflowMode == ValueOverflowContract.Scroll &&
               falseText.OverflowMode == ValueOverflowContract.Ellipsis,
            "Binary True/False Text presentation must be independently editable.");

        var refreshed = new EditorPropertyView(
            currentWidget,
            section: EditorPropertySection.Text).GetProperties();
        var trueScrollSpeed = GetNestedProperty(
            refreshed, "State: True", "Overflow",
            StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.ScrollSpeed));
        var falseScrollSpeed = GetNestedProperty(
            refreshed, "State: False", "Overflow",
            StateProperty(BinarySignalContract.FalseKey, StateVisualProfileField.ScrollSpeed));
        Assert(trueScrollSpeed is { IsReadOnly: false } &&
               falseScrollSpeed is { IsReadOnly: true } &&
               falseScrollSpeed.Description == "Not applicable for selected Overflow Mode.",
            "Mode-specific Binary Text controls must remain stable and become context-disabled per state.");


        var currentFalse = currentWidget.Profiles[BinarySignalContract.FalseKey];
        currentFalse.ContentType = C.StateContentType.Image;
        currentFalse.Asset.ChangeSourceType(C.ImageAssetSourceType.Icon);
        currentFalse.Asset.Source = "lucide:check";
        var mixedText = new EditorPropertyView(
            currentWidget,
            section: EditorPropertySection.Text).GetProperties();
        var mixedImage = new EditorPropertyView(
            currentWidget,
            section: EditorPropertySection.Image).GetProperties();
        Assert(mixedText["State: True"] is not null && mixedText["State: False"] is null,
            "Mixed Binary must hide Text presentation for the image-backed state.");
        Assert(mixedImage["State: True"] is null && mixedImage["State: False"] is not null,
            "Mixed Binary must expose Image presentation for the image-backed state.");
    }

    private static void BinaryStateTextRendering()
    {
        const string metric = RuntimeMetricContract.Fps;
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-binary-state-text-rendering");
        try
        {
            var widget = new WidgetDefinition
            {
                Type = WidgetTypeContract.Binary,
                Id = "binary-state-text-rendering",
                Metric = metric,
                X = 10f,
                Y = 10f,
                Width = 0f,
                Height = 0f,
                // Deliberately unrelated legacy widget-level values: Binary rendering must ignore them.
                FontSize = 96f,
                Align = "right",
                VerticalAlign = "bottom",
                OverflowMode = ValueOverflowContract.Clip
            };
            widget.EnsureStateVisualProfiles();
            widget.Profiles![BinarySignalContract.TrueKey].SourceType = StateVisualProfileContract.SourceValue;
            widget.Profiles![BinarySignalContract.FalseKey].SourceType = StateVisualProfileContract.SourceValue;

            var trueText = GetBinaryText(widget, BinarySignalContract.TrueKey);
            trueText.FontSize = 40f;
            trueText.Align = "left";
            trueText.VerticalAlign = "top";
            trueText.OverflowMode = ValueOverflowContract.None;

            var falseText = GetBinaryText(widget, BinarySignalContract.FalseKey);
            falseText.FontSize = 14f;
            falseText.Align = "left";
            falseText.VerticalAlign = "top";
            falseText.OverflowMode = ValueOverflowContract.None;

            widget.Validate(tempRoot);

            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, new[] { CurrentWidget(widget) });
            using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = 42d },
                antialias: false,
                typefaces);

            var trueBounds = PinkieSysMon.Widgets.WidgetGeometry.GetLayoutBounds(CurrentWidget(widget), context);
            context.UpdateMetrics(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = 0d });
            var falseBounds = PinkieSysMon.Widgets.WidgetGeometry.GetLayoutBounds(CurrentWidget(widget), context);

            Assert(trueBounds.Height > falseBounds.Height * 1.8f,
                $"Binary geometry did not follow active state Font Size: true={trueBounds.Height}, false={falseBounds.Height}.");
            Assert(trueBounds.Height < 80f && falseBounds.Height < 40f,
                "Binary geometry appears to be using obsolete widget-level Font Size instead of state Text profiles.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void PowerStatePropertyHierarchy()
    {
        var power = new WidgetDefinition
        {
            Type = WidgetTypeContract.Power,
            Id = "power-state-property-hierarchy",
            Source = PowerMetricContract.UpsSource,
            Width = 48f,
            Height = 48f
        };
        power.EnsureStateVisualProfiles();
        var currentPower = (C.PowerWidgetDefinition)CurrentWidget(power);

        static string StateProperty(string key, StateVisualProfileField field) =>
            $"{StateVisualProfilePropertyDescriptor.PropertyPrefix}{key}:{field}";

        var onlineSourceTypeName = StateProperty(
            PowerMetricContract.StateOnline,
            StateVisualProfileField.SourceType);
        var onlineSourceName = StateProperty(
            PowerMetricContract.StateOnline,
            StateVisualProfileField.Source);

        var states = new EditorPropertyView(
            currentPower,
            section: EditorPropertySection.States).GetProperties();
        var onlineSourceType = GetNestedProperty(states, "State: Online", onlineSourceTypeName)
            ?? throw new InvalidOperationException("Power Online Source Type property is missing.");
        var sourceTypeOptions = onlineSourceType.Converter.GetStandardValues()
            ?.Cast<object>()
            .Select(value => Convert.ToString(value) ?? string.Empty)
            .ToArray() ?? [];
        Assert(sourceTypeOptions.Contains("Icon", StringComparer.OrdinalIgnoreCase) &&
               sourceTypeOptions.Contains("Image", StringComparer.OrdinalIgnoreCase) &&
               sourceTypeOptions.Contains("Value", StringComparer.OrdinalIgnoreCase),
            "Power Source Type must offer icon, file, and value.");

        onlineSourceType.SetValue(null, "Value");
        currentPower.Profiles[PowerMetricContract.StateOnline].TextPresentation.OverflowMode = ValueOverflowContract.Clip;

        states = new EditorPropertyView(
            currentPower,
            section: EditorPropertySection.States).GetProperties();
        var onlineValueSource = GetNestedProperty(states, "State: Online", onlineSourceName);
        Assert(GetNestedProperty(states, "State: Online", onlineSourceTypeName) is not null &&
               onlineValueSource is { IsReadOnly: true } &&
               onlineValueSource.Description == "Not applicable for selected Source Type.",
            "Value-backed Power state must keep Source visible but disabled.");

        var charging = currentPower.Profiles[PowerMetricContract.StateCharging];
        charging.ContentType = C.StateContentType.Image;
        charging.Asset.SourceType = C.ImageAssetSourceType.File;
        charging.Asset.Source = "charging.gif";
        charging.Asset.Fit = StateVisualProfileContract.FitCover;
        charging.Asset.Loop = false;

        var image = new EditorPropertyView(
            currentPower,
            section: EditorPropertySection.Image).GetProperties();
        Assert(image["State: Online"] is null &&
               image["State: Charging"] is PropertyGroupPropertyDescriptor &&
               image["State: Low"] is PropertyGroupPropertyDescriptor,
            "Power Image tab must include only icon/file-backed states.");
        Assert(GetNestedProperty(
                   image,
                   "State: Charging",
                   StateProperty(PowerMetricContract.StateCharging, StateVisualProfileField.Fit)) is { DisplayName: "Fit" } &&
               GetNestedProperty(
                   image,
                   "State: Charging",
                   StateProperty(PowerMetricContract.StateCharging, StateVisualProfileField.Loop)) is { DisplayName: "Loop" } &&
               GetNestedProperty(
                   image,
                   "State: Charging",
                   StateProperty(PowerMetricContract.StateCharging, StateVisualProfileField.Opacity)) is { DisplayName: "Opacity" },
            "File-backed Power state must expose standard Fit, Loop, and Opacity image presentation.");
        Assert(GetNestedProperty(
                   image,
                   "State: Low",
                   StateProperty(PowerMetricContract.StateLow, StateVisualProfileField.Fit)) is { DisplayName: "Fit" } &&
               GetNestedProperty(
                   image,
                   "State: Low",
                   StateProperty(PowerMetricContract.StateLow, StateVisualProfileField.Color)) is { DisplayName: "Color" } &&
               GetNestedProperty(
                   image,
                   "State: Low",
                   StateProperty(PowerMetricContract.StateLow, StateVisualProfileField.Opacity)) is { DisplayName: "Opacity" } &&
               GetNestedProperty(
                   image,
                   "State: Low",
                   StateProperty(PowerMetricContract.StateLow, StateVisualProfileField.Loop)) is { IsReadOnly: true, Description: "Not applicable for selected Source." },
            "Icon-backed Power state must keep Loop visible but disabled.");

        var text = new EditorPropertyView(
            currentPower,
            section: EditorPropertySection.Text).GetProperties();
        Assert(text.Count == 1 && text["State: Online"] is PropertyGroupPropertyDescriptor,
            "Power Text tab must expose only value-backed state profiles.");
        foreach (var group in new[] { "Foreground", "Font", "Alignment", "Outline", "Overflow" })
        {
            Assert(GetNestedProperty(text, "State: Online", group) is PropertyGroupPropertyDescriptor,
                $"Power value state Text presentation is missing '{group}'.");
        }

    }

    private static void PowerStatePresentationSchema()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-power-state-schema-regression");
        try
        {
            var dashboardPath = Path.Combine(tempRoot, "dashboards", "power-state-schema.json");
            var power = new WidgetDefinition
            {
                Type = WidgetTypeContract.Power,
                Id = "power-state-schema",
                Source = PowerMetricContract.UpsSource,
                Width = 0f,
                Height = 0f
            };
            power.EnsureStateVisualProfiles();

            Assert(power.Profiles!.Count == PowerMetricContract.States.Length &&
                   power.Profiles.Values.All(profile =>
                       profile.Fit == StateVisualProfileContract.FitContain &&
                       profile.Loop == true &&
                       profile.Text is not null),
                "Schema 14 Power profiles must provide standard Fit/Loop/Text presentation for every state.");

            foreach (var profile in power.Profiles.Values)
            {
                profile.SourceType = StateVisualProfileContract.SourceValue;
                profile.Source = string.Empty;
                profile.Text!.OverflowMode = ValueOverflowContract.None;
            }

            var powerNode = new JsonObject
            {
                ["type"] = WidgetTypeContract.Power,
                ["id"] = power.Id,
                ["width"] = 0f,
                ["height"] = 0f,
                ["source"] = PowerMetricContract.UpsSource,
                ["profiles"] = JsonSerializer.SerializeToNode(power.Profiles)
            };
            var root = new JsonObject
            {
                ["schemaVersion"] = DashboardSchemaMigration.CanvasImageLayerSchemaVersion,
                ["canvas"] = new JsonObject
                {
                    ["width"] = FrameGeometry.DefaultNativeWidth,
                    ["height"] = FrameGeometry.DefaultNativeHeight,
                    ["orientation"] = 0,
                    ["backgroundColor"] = "#000000"
                },
                ["widgets"] = new JsonArray(powerNode)
            };

            var definition = DashboardDefinition.Parse(root.ToJsonString(), dashboardPath);
            var parsed = definition.Widgets.Single();
            Assert(StateVisualProfileContract.UsesOnlyValueSources(parsed),
                "Schema 14 must deserialize value-backed Power profiles.");

            var missingText = JsonNode.Parse(root.ToJsonString()) as JsonObject
                ?? throw new InvalidOperationException("Could not clone Power schema document.");
            var missingWidget = ((missingText["widgets"] as JsonArray)![0] as JsonObject)!;
            var missingProfiles = (missingWidget["profiles"] as JsonObject)!;
            ((missingProfiles[PowerMetricContract.StateCritical] as JsonObject)!).Remove("text");
            var missingRejected = false;
            try
            {
                _ = DashboardDefinition.Parse(missingText.ToJsonString(), dashboardPath);
            }
            catch (InvalidDataException)
            {
                missingRejected = true;
            }
            Assert(missingRejected,
                "Schema 14 Power profiles must explicitly persist Text presentation for every state.");

            var obsolete = JsonNode.Parse(root.ToJsonString()) as JsonObject
                ?? throw new InvalidOperationException("Could not clone Power obsolete-presentation document.");
            var obsoleteWidget = ((obsolete["widgets"] as JsonArray)![0] as JsonObject)!;
            obsoleteWidget["fit"] = StateVisualProfileContract.FitContain;
            var obsoleteRejected = false;
            try
            {
                _ = DashboardDefinition.Parse(obsolete.ToJsonString(), dashboardPath);
            }
            catch (InvalidDataException)
            {
                obsoleteRejected = true;
            }
            Assert(obsoleteRejected,
                "Schema 14 must reject obsolete top-level Power image/text presentation.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void PowerStateTextRendering()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-power-state-text-rendering");
        try
        {
            var widget = new WidgetDefinition
            {
                Type = WidgetTypeContract.Power,
                Id = "power-state-text-rendering",
                Source = PowerMetricContract.UpsSource,
                X = 10f,
                Y = 10f,
                Width = 0f,
                Height = 0f,
                FontSize = 96f,
                Align = "right",
                VerticalAlign = "bottom",
                OverflowMode = ValueOverflowContract.Clip
            };
            widget.EnsureStateVisualProfiles();
            foreach (var stateProfile in widget.Profiles!.Values)
            {
                stateProfile.SourceType = StateVisualProfileContract.SourceValue;
                stateProfile.Source = string.Empty;
                stateProfile.Text!.OverflowMode = ValueOverflowContract.None;
            }

            var onlineText = widget.Profiles[PowerMetricContract.StateOnline].Text!;
            onlineText.FontSize = 40f;
            onlineText.Align = "left";
            onlineText.VerticalAlign = "top";

            var chargingText = widget.Profiles[PowerMetricContract.StateCharging].Text!;
            chargingText.FontSize = 14f;
            chargingText.Align = "left";
            chargingText.VerticalAlign = "top";

            widget.Validate(tempRoot);

            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, new[] { CurrentWidget(widget) });
            using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    [PowerMetricContract.UpsStateMetric] = PowerMetricContract.StateOnline
                },
                antialias: false,
                typefaces);

            var canonicalWidget = (PinkieSysMon.DashboardModel.PowerWidgetDefinition)
                PropertyModelNormalizationMigration.MapLegacyWidget(widget);
            Assert(context.TryResolvePowerVisualProfile(canonicalWidget, out var stateKey, out var profile) &&
                   stateKey == PowerMetricContract.StateOnline &&
                   StateVisualProfileContract.IsValueSource(profile.SourceType) &&
                   profile.Text?.FontSize == 40f,
                "Power state resolver must select the profile driven by the semantic state metric.");

            var onlineBounds = PinkieSysMon.Widgets.WidgetGeometry.GetLayoutBounds(CurrentWidget(widget), context);
            context.UpdateMetrics(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    [PowerMetricContract.UpsStateMetric] = PowerMetricContract.StateCharging
                });
            var chargingBounds = PinkieSysMon.Widgets.WidgetGeometry.GetLayoutBounds(CurrentWidget(widget), context);

            Assert(onlineBounds.Height > chargingBounds.Height * 1.8f,
                $"Power geometry did not follow active state Font Size: online={onlineBounds.Height}, charging={chargingBounds.Height}.");
            Assert(onlineBounds.Height < 80f && chargingBounds.Height < 40f,
                "Power geometry appears to be using obsolete widget-level Font Size instead of state Text profiles.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void PowerSchema13Migration()
    {
        var root = JsonNode.Parse(
            $$"""
            {
              "schemaVersion": 13,
              "canvas": { "orientation": 0, "backgroundColor": "#000000" },
              "widgets": [
                {
                  "type": "power",
                  "id": "power-schema13",
                  "width": 48,
                  "height": 48,
                  "source": "{{PowerMetricContract.UpsSource}}",
                  "profiles": {
                    "{{PowerMetricContract.StateOnline}}": {
                      "sourceType": "icon",
                      "source": "lucide:battery-full",
                      "color": "#FF123456",
                      "opacity": 0.75
                    }
                  }
                }
              ]
            }
            """) as JsonObject ?? throw new InvalidOperationException("Could not create schema 13 Power migration document.");

        Assert(DashboardSchemaMigration.UpgradeToCurrent(root),
            "Schema 13 dashboard must migrate Power presentation to the current schema.");
        Assert(root["schemaVersion"]!.GetValue<int>() == DashboardSchemaMigration.PropertyModelNormalizationSchemaVersion,
            "Power migration must end at current schema 19.");

        var widget = ((root["widgets"] as JsonArray)?[0] as JsonObject)
            ?? throw new InvalidOperationException("Migrated Power widget is missing.");
        Assert(widget["powerSource"]?.GetValue<string>() == PowerMetricContract.UpsSource &&
               !widget.ContainsKey("source"),
            "Schema 13 migration must replace overloaded Power source with canonical powerSource.");

        var profiles = widget["profiles"] as JsonObject
            ?? throw new InvalidOperationException("Migrated Power profiles are missing.");
        Assert(profiles.Count == PowerMetricContract.States.Length,
            "Power migration must materialize every canonical state profile.");

        foreach (var spec in StateVisualProfileContract.GetSpecs(WidgetTypeContract.Power))
            AssertCanonicalMigratedStateProfile(profiles, spec.Key, StateVisualProfileContract.FitContain);

        var online = profiles[PowerMetricContract.StateOnline] as JsonObject
            ?? throw new InvalidOperationException("Migrated Power Online profile is missing.");
        var asset = online["asset"] as JsonObject
            ?? throw new InvalidOperationException("Migrated Power Online asset is missing.");
        Assert(online["contentType"]?.GetValue<string>() == C.StateContentType.Image &&
               asset["sourceType"]?.GetValue<string>() == C.ImageAssetSourceType.Icon &&
               asset["source"]?.GetValue<string>() == "lucide:battery-full" &&
               online["color"]?.GetValue<string>() == "#FF123456" &&
               Math.Abs(online["opacity"]!.GetValue<float>() - 0.75f) < 0.001f,
            "Power migration must preserve existing state source/color/opacity settings in canonical profile structure.");
    }

    private static void MediaPlayerStatePropertyHierarchy()
    {
        var mediaPlayer = new WidgetDefinition
        {
            Type = WidgetTypeContract.MediaPlayer,
            Id = "media-player-state-property-hierarchy",
            Width = 48f,
            Height = 48f
        };
        mediaPlayer.EnsureStateVisualProfiles();
        var currentMediaPlayer = (C.MediaPlayerWidgetDefinition)CurrentWidget(mediaPlayer);

        static string StateProperty(string key, StateVisualProfileField field) =>
            $"{StateVisualProfilePropertyDescriptor.PropertyPrefix}{key}:{field}";

        var playingSourceTypeName = StateProperty(
            MediaMetricContract.StatusPlaying,
            StateVisualProfileField.SourceType);
        var playingSourceName = StateProperty(
            MediaMetricContract.StatusPlaying,
            StateVisualProfileField.Source);

        var states = new EditorPropertyView(
            currentMediaPlayer,
            section: EditorPropertySection.States).GetProperties();
        var playingSourceType = GetNestedProperty(states, "State: Playing", playingSourceTypeName)
            ?? throw new InvalidOperationException("Media Player Playing Source Type property is missing.");
        var sourceTypeOptions = playingSourceType.Converter.GetStandardValues()
            ?.Cast<object>()
            .Select(value => Convert.ToString(value) ?? string.Empty)
            .ToArray() ?? [];
        Assert(sourceTypeOptions.Contains("Icon", StringComparer.OrdinalIgnoreCase) &&
               sourceTypeOptions.Contains("Image", StringComparer.OrdinalIgnoreCase) &&
               sourceTypeOptions.Contains("Value", StringComparer.OrdinalIgnoreCase),
            "Media Player Source Type must offer icon, file, and value.");

        playingSourceType.SetValue(null, "Value");
        currentMediaPlayer.Profiles[MediaMetricContract.StatusPlaying].TextPresentation.OverflowMode = ValueOverflowContract.Clip;

        states = new EditorPropertyView(
            currentMediaPlayer,
            section: EditorPropertySection.States).GetProperties();
        var playingValueSource = GetNestedProperty(states, "State: Playing", playingSourceName);
        Assert(GetNestedProperty(states, "State: Playing", playingSourceTypeName) is not null &&
               playingValueSource is { IsReadOnly: true } &&
               playingValueSource.Description == "Not applicable for selected Source Type.",
            "Value-backed Media Player state must keep Source visible but disabled.");

        var paused = currentMediaPlayer.Profiles[MediaMetricContract.StatusPaused];
        paused.ContentType = C.StateContentType.Image;
        paused.Asset.SourceType = C.ImageAssetSourceType.File;
        paused.Asset.Source = "paused.gif";
        paused.Asset.Fit = StateVisualProfileContract.FitCover;
        paused.Asset.Loop = false;

        var image = new EditorPropertyView(
            currentMediaPlayer,
            section: EditorPropertySection.Image).GetProperties();
        Assert(image["State: Playing"] is null &&
               image["State: Paused"] is PropertyGroupPropertyDescriptor &&
               image["State: Stopped"] is PropertyGroupPropertyDescriptor,
            "Media Player Image tab must include only icon/file-backed states.");
        Assert(GetNestedProperty(
                   image,
                   "State: Paused",
                   StateProperty(MediaMetricContract.StatusPaused, StateVisualProfileField.Fit)) is { DisplayName: "Fit" } &&
               GetNestedProperty(
                   image,
                   "State: Paused",
                   StateProperty(MediaMetricContract.StatusPaused, StateVisualProfileField.Loop)) is { DisplayName: "Loop" } &&
               GetNestedProperty(
                   image,
                   "State: Paused",
                   StateProperty(MediaMetricContract.StatusPaused, StateVisualProfileField.Opacity)) is { DisplayName: "Opacity" },
            "File-backed Media Player state must expose standard Fit, Loop, and Opacity image presentation.");
        Assert(GetNestedProperty(
                   image,
                   "State: Stopped",
                   StateProperty(MediaMetricContract.StatusStopped, StateVisualProfileField.Fit)) is { DisplayName: "Fit" } &&
               GetNestedProperty(
                   image,
                   "State: Stopped",
                   StateProperty(MediaMetricContract.StatusStopped, StateVisualProfileField.Color)) is { DisplayName: "Color" } &&
               GetNestedProperty(
                   image,
                   "State: Stopped",
                   StateProperty(MediaMetricContract.StatusStopped, StateVisualProfileField.Opacity)) is { DisplayName: "Opacity" } &&
               GetNestedProperty(
                   image,
                   "State: Stopped",
                   StateProperty(MediaMetricContract.StatusStopped, StateVisualProfileField.Loop)) is { IsReadOnly: true, Description: "Not applicable for selected Source." },
            "Icon-backed Media Player state must keep Loop visible but disabled.");

        var text = new EditorPropertyView(
            currentMediaPlayer,
            section: EditorPropertySection.Text).GetProperties();
        Assert(text.Count == 1 && text["State: Playing"] is PropertyGroupPropertyDescriptor,
            "Media Player Text tab must expose only value-backed state profiles.");
        foreach (var group in new[] { "Foreground", "Font", "Alignment", "Outline", "Overflow" })
        {
            Assert(GetNestedProperty(text, "State: Playing", group) is PropertyGroupPropertyDescriptor,
                $"Media Player value state Text presentation is missing '{group}'.");
        }

    }

    private static void MediaPlayerStatePresentationSchema()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-media-player-state-schema-regression");
        try
        {
            var dashboardPath = Path.Combine(tempRoot, "dashboards", "media-player-state-schema.json");
            var mediaPlayer = new WidgetDefinition
            {
                Type = WidgetTypeContract.MediaPlayer,
                Id = "media-player-state-schema",
                Width = 0f,
                Height = 0f
            };
            mediaPlayer.EnsureStateVisualProfiles();

            Assert(mediaPlayer.Profiles!.Count == 4 &&
                   mediaPlayer.Profiles.Values.All(profile =>
                       profile.Fit == StateVisualProfileContract.FitContain &&
                       profile.Loop == true &&
                       profile.Text is not null),
                "Current Media Player profiles must provide standard Fit/Loop/Text presentation for every state.");

            foreach (var stateProfile in mediaPlayer.Profiles.Values)
            {
                stateProfile.SourceType = StateVisualProfileContract.SourceValue;
                stateProfile.Source = string.Empty;
                stateProfile.Text!.OverflowMode = ValueOverflowContract.None;
            }

            var mediaPlayerNode = new JsonObject
            {
                ["type"] = WidgetTypeContract.MediaPlayer,
                ["id"] = mediaPlayer.Id,
                ["width"] = 0f,
                ["height"] = 0f,
                ["profiles"] = JsonSerializer.SerializeToNode(mediaPlayer.Profiles)
            };
            var root = new JsonObject
            {
                ["schemaVersion"] = DashboardSchemaMigration.CanvasImageLayerSchemaVersion,
                ["canvas"] = new JsonObject
                {
                    ["width"] = FrameGeometry.DefaultNativeWidth,
                    ["height"] = FrameGeometry.DefaultNativeHeight,
                    ["orientation"] = 0,
                    ["backgroundColor"] = "#000000"
                },
                ["widgets"] = new JsonArray(mediaPlayerNode)
            };

            var definition = DashboardDefinition.Parse(root.ToJsonString(), dashboardPath);
            var parsed = definition.Widgets.Single();
            Assert(StateVisualProfileContract.UsesOnlyValueSources(parsed),
                "Current schema must deserialize value-backed Media Player profiles.");

            var missingText = JsonNode.Parse(root.ToJsonString()) as JsonObject
                ?? throw new InvalidOperationException("Could not clone Media Player schema document.");
            var missingWidget = ((missingText["widgets"] as JsonArray)![0] as JsonObject)!;
            var missingProfiles = (missingWidget["profiles"] as JsonObject)!;
            ((missingProfiles[MediaMetricContract.StatusPaused] as JsonObject)!).Remove("text");
            var missingRejected = false;
            try
            {
                _ = DashboardDefinition.Parse(missingText.ToJsonString(), dashboardPath);
            }
            catch (InvalidDataException)
            {
                missingRejected = true;
            }
            Assert(missingRejected,
                "Current Media Player profiles must explicitly persist Text presentation for every state.");

            var obsolete = JsonNode.Parse(root.ToJsonString()) as JsonObject
                ?? throw new InvalidOperationException("Could not clone Media Player obsolete-presentation document.");
            var obsoleteWidget = ((obsolete["widgets"] as JsonArray)![0] as JsonObject)!;
            obsoleteWidget["fit"] = StateVisualProfileContract.FitContain;
            var obsoleteRejected = false;
            try
            {
                _ = DashboardDefinition.Parse(obsolete.ToJsonString(), dashboardPath);
            }
            catch (InvalidDataException)
            {
                obsoleteRejected = true;
            }
            Assert(obsoleteRejected,
                "Current schema must reject obsolete top-level Media Player image/text presentation.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void MediaPlayerStateTextRendering()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-media-player-state-text-rendering");
        try
        {
            var widget = new WidgetDefinition
            {
                Type = WidgetTypeContract.MediaPlayer,
                Id = "media-player-state-text-rendering",
                X = 10f,
                Y = 10f,
                Width = 0f,
                Height = 0f,
                FontSize = 96f,
                Align = "right",
                VerticalAlign = "bottom",
                OverflowMode = ValueOverflowContract.Clip
            };
            widget.EnsureStateVisualProfiles();
            foreach (var stateProfile in widget.Profiles!.Values)
            {
                stateProfile.SourceType = StateVisualProfileContract.SourceValue;
                stateProfile.Source = string.Empty;
                stateProfile.Text!.OverflowMode = ValueOverflowContract.None;
            }

            var playingText = widget.Profiles[MediaMetricContract.StatusPlaying].Text!;
            playingText.FontSize = 40f;
            playingText.Align = "left";
            playingText.VerticalAlign = "top";

            var pausedText = widget.Profiles[MediaMetricContract.StatusPaused].Text!;
            pausedText.FontSize = 14f;
            pausedText.Align = "left";
            pausedText.VerticalAlign = "top";

            widget.Validate(tempRoot);

            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, new[] { CurrentWidget(widget) });
            using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    [MediaMetricContract.PlaybackStatus] = MediaMetricContract.StatusPlaying
                },
                antialias: false,
                typefaces);

            var canonicalWidget = (PinkieSysMon.DashboardModel.MediaPlayerWidgetDefinition)
                PropertyModelNormalizationMigration.MapLegacyWidget(widget);
            Assert(context.TryResolveMediaPlayerVisualProfile(canonicalWidget, out var stateKey, out var activeProfile) &&
                   stateKey == MediaMetricContract.StatusPlaying &&
                   StateVisualProfileContract.IsValueSource(activeProfile.SourceType) &&
                   activeProfile.Text?.FontSize == 40f,
                "Media Player state resolver must select the profile driven by Playback Status.");

            var playingBounds = PinkieSysMon.Widgets.WidgetGeometry.GetLayoutBounds(CurrentWidget(widget), context);
            context.UpdateMetrics(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    [MediaMetricContract.PlaybackStatus] = MediaMetricContract.StatusPaused
                });
            var pausedBounds = PinkieSysMon.Widgets.WidgetGeometry.GetLayoutBounds(CurrentWidget(widget), context);

            Assert(playingBounds.Height > pausedBounds.Height * 1.8f,
                $"Media Player geometry did not follow active state Font Size: playing={playingBounds.Height}, paused={pausedBounds.Height}.");
            Assert(playingBounds.Height < 80f && pausedBounds.Height < 40f,
                "Media Player geometry appears to be using obsolete widget-level Font Size instead of state Text profiles.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void MediaPlayerSchema14Migration()
    {
        var root = JsonNode.Parse(
            $$"""
            {
              "schemaVersion": 14,
              "canvas": { "orientation": 0, "backgroundColor": "#000000" },
              "widgets": [
                {
                  "type": "media.player",
                  "id": "media-player-schema14",
                  "width": 48,
                  "height": 48,
                  "source": "{{MediaMetricContract.PlaybackSource}}",
                  "profiles": {
                    "{{MediaMetricContract.StatusPlaying}}": {
                      "sourceType": "icon",
                      "source": "lucide:play",
                      "color": "#FF123456",
                      "opacity": 0.75
                    }
                  }
                }
              ]
            }
            """) as JsonObject ?? throw new InvalidOperationException("Could not create schema 14 Media Player migration document.");

        Assert(DashboardSchemaMigration.UpgradeToCurrent(root),
            "Schema 14 dashboard must migrate Media Player presentation to the current schema.");
        Assert(root["schemaVersion"]!.GetValue<int>() == DashboardSchemaMigration.PropertyModelNormalizationSchemaVersion,
            "Media Player migration must end at current schema 19.");

        var widget = ((root["widgets"] as JsonArray)?[0] as JsonObject)
            ?? throw new InvalidOperationException("Migrated Media Player widget is missing.");
        Assert(!widget.ContainsKey("source"),
            "Canonical Media Player must not persist the obsolete playback Source field.");

        var profiles = widget["profiles"] as JsonObject
            ?? throw new InvalidOperationException("Migrated Media Player profiles are missing.");
        Assert(profiles.Count == 4,
            "Media Player migration must materialize every canonical state profile.");

        foreach (var spec in StateVisualProfileContract.GetSpecs(WidgetTypeContract.MediaPlayer))
            AssertCanonicalMigratedStateProfile(profiles, spec.Key, StateVisualProfileContract.FitContain);

        var playing = profiles[MediaMetricContract.StatusPlaying] as JsonObject
            ?? throw new InvalidOperationException("Migrated Media Player Playing profile is missing.");
        var asset = playing["asset"] as JsonObject
            ?? throw new InvalidOperationException("Migrated Media Player Playing asset is missing.");
        Assert(playing["contentType"]?.GetValue<string>() == C.StateContentType.Image &&
               asset["sourceType"]?.GetValue<string>() == C.ImageAssetSourceType.Icon &&
               asset["source"]?.GetValue<string>() == "lucide:play" &&
               playing["color"]?.GetValue<string>() == "#FF123456" &&
               Math.Abs(playing["opacity"]!.GetValue<float>() - 0.75f) < 0.001f,
            "Media Player migration must preserve existing state source/color/opacity settings in canonical profile structure.");
    }

    private static void MediaSystemStatePropertyHierarchy()
    {
        var mediaSystem = new WidgetDefinition
        {
            Type = WidgetTypeContract.MediaSystem,
            Id = "media-system-state-property-hierarchy",
            Width = 64f,
            Height = 64f,
            Source = MediaMetricContract.OutputSource
        };
        mediaSystem.EnsureStateVisualProfiles();
        var currentMediaSystem = (C.MediaSystemWidgetDefinition)CurrentWidget(mediaSystem);

        static string StateProperty(string key, StateVisualProfileField field) =>
            $"{StateVisualProfilePropertyDescriptor.PropertyPrefix}{key}:{field}";

        var speakersSourceTypeName = StateProperty(
            MediaMetricContract.EndpointSpeakers,
            StateVisualProfileField.SourceType);
        var speakersSourceName = StateProperty(
            MediaMetricContract.EndpointSpeakers,
            StateVisualProfileField.Source);

        var states = new EditorPropertyView(
            currentMediaSystem,
            section: EditorPropertySection.States).GetProperties();
        var speakersSourceType = GetNestedProperty(states, "Type: Speakers", speakersSourceTypeName)
            ?? throw new InvalidOperationException("Media System Speakers Source Type property is missing.");
        var sourceTypeOptions = speakersSourceType.Converter.GetStandardValues()
            ?.Cast<object>()
            .Select(value => Convert.ToString(value) ?? string.Empty)
            .ToArray() ?? [];
        Assert(sourceTypeOptions.Contains("Icon", StringComparer.OrdinalIgnoreCase) &&
               sourceTypeOptions.Contains("Image", StringComparer.OrdinalIgnoreCase) &&
               sourceTypeOptions.Contains("Value", StringComparer.OrdinalIgnoreCase),
            "Media System Source Type must offer icon, file, and value.");

        speakersSourceType.SetValue(null, "Value");
        currentMediaSystem.Profiles[MediaMetricContract.EndpointSpeakers].TextPresentation.OverflowMode = ValueOverflowContract.Clip;

        states = new EditorPropertyView(
            currentMediaSystem,
            section: EditorPropertySection.States).GetProperties();
        var speakersValueSource = GetNestedProperty(states, "Type: Speakers", speakersSourceName);
        Assert(GetNestedProperty(states, "Type: Speakers", speakersSourceTypeName) is not null &&
               speakersValueSource is { IsReadOnly: true } &&
               speakersValueSource.Description == "Not applicable for selected Source Type.",
            "Value-backed Media System state must keep Source visible but disabled.");

        var headphones = currentMediaSystem.Profiles[MediaMetricContract.EndpointHeadphones];
        headphones.ContentType = C.StateContentType.Image;
        headphones.Asset.SourceType = C.ImageAssetSourceType.File;
        headphones.Asset.Source = "headphones.gif";
        headphones.Asset.Fit = StateVisualProfileContract.FitCover;
        headphones.Asset.Loop = false;

        var image = new EditorPropertyView(
            currentMediaSystem,
            section: EditorPropertySection.Image).GetProperties();
        Assert(image["Type: Speakers"] is null &&
               image["Type: Headphones"] is PropertyGroupPropertyDescriptor &&
               image["Type: Line Level"] is PropertyGroupPropertyDescriptor,
            "Media System Image tab must include only icon/file-backed states.");
        Assert(GetNestedProperty(
                   image,
                   "Type: Headphones",
                   StateProperty(MediaMetricContract.EndpointHeadphones, StateVisualProfileField.Fit)) is { DisplayName: "Fit" } &&
               GetNestedProperty(
                   image,
                   "Type: Headphones",
                   StateProperty(MediaMetricContract.EndpointHeadphones, StateVisualProfileField.Loop)) is { DisplayName: "Loop" } &&
               GetNestedProperty(
                   image,
                   "Type: Headphones",
                   StateProperty(MediaMetricContract.EndpointHeadphones, StateVisualProfileField.Opacity)) is { DisplayName: "Opacity" },
            "File-backed Media System state must expose standard Fit, Loop, and Opacity image presentation.");
        Assert(GetNestedProperty(
                   image,
                   "Type: Line Level",
                   StateProperty(MediaMetricContract.EndpointLineLevel, StateVisualProfileField.Fit)) is { DisplayName: "Fit" } &&
               GetNestedProperty(
                   image,
                   "Type: Line Level",
                   StateProperty(MediaMetricContract.EndpointLineLevel, StateVisualProfileField.Color)) is { DisplayName: "Color" } &&
               GetNestedProperty(
                   image,
                   "Type: Line Level",
                   StateProperty(MediaMetricContract.EndpointLineLevel, StateVisualProfileField.Opacity)) is { DisplayName: "Opacity" } &&
               GetNestedProperty(
                   image,
                   "Type: Line Level",
                   StateProperty(MediaMetricContract.EndpointLineLevel, StateVisualProfileField.Loop)) is { IsReadOnly: true, Description: "Not applicable for selected Source." },
            "Icon-backed Media System state must keep Loop visible but disabled.");

        var text = new EditorPropertyView(
            currentMediaSystem,
            section: EditorPropertySection.Text).GetProperties();
        Assert(text.Count == 1 && text["Type: Speakers"] is PropertyGroupPropertyDescriptor,
            "Media System Text tab must expose only value-backed state profiles.");
        foreach (var group in new[] { "Foreground", "Font", "Alignment", "Outline", "Overflow" })
        {
            Assert(GetNestedProperty(text, "Type: Speakers", group) is PropertyGroupPropertyDescriptor,
                $"Media System value state Text presentation is missing '{group}'.");
        }

        var endpoints = new[]
        {
            new WindowsMediaTelemetrySource.MediaEndpointInfo("endpoint-1", "Speakers A"),
            new WindowsMediaTelemetrySource.MediaEndpointInfo("endpoint-2", "Headset B")
        };
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        var data = new EditorPropertyView(
            currentMediaSystem,
            mediaEndpoints: endpoints,
            endpointTypeOverrides: overrides,
            section: EditorPropertySection.Data).GetProperties();
        Assert(data["Endpoint Overrides"] is PropertyGroupPropertyDescriptor,
            "Media System Endpoint Overrides must be exposed on the Data tab.");
        Assert(GetNestedProperty(
                   data,
                   "Endpoint Overrides",
                   EndpointOverridePropertyDescriptor.PropertyPrefix + "endpoint-1") is { DisplayName: "Speakers A" } &&
               GetNestedProperty(
                   data,
                   "Endpoint Overrides",
                   EndpointOverridePropertyDescriptor.PropertyPrefix + "endpoint-2") is { DisplayName: "Headset B" },
            "Media System Data tab must contain endpoint override properties for discovered endpoints.");

    }

    private static void MediaSystemStatePresentationSchema()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-media-system-state-schema-regression");
        try
        {
            var dashboardPath = Path.Combine(tempRoot, "dashboards", "media-system-state-schema.json");
            Directory.CreateDirectory(Path.GetDirectoryName(dashboardPath)!);

            var mediaSystem = new WidgetDefinition
            {
                Type = WidgetTypeContract.MediaSystem,
                Id = "media-system-state-schema",
                Width = 0f,
                Height = 0f,
                Source = MediaMetricContract.OutputSource
            };
            mediaSystem.EnsureStateVisualProfiles();
            Assert(mediaSystem.Profiles!.Values.All(profile =>
                       profile.Fit == StateVisualProfileContract.FitContain &&
                       profile.Loop == true &&
                       profile.Text is not null),
                "Current Media System profiles must provide standard Fit/Loop/Text presentation for every state.");

            foreach (var stateProfile in mediaSystem.Profiles.Values)
            {
                stateProfile.SourceType = StateVisualProfileContract.SourceValue;
                stateProfile.Source = string.Empty;
                stateProfile.Text!.OverflowMode = ValueOverflowContract.None;
            }

            var mediaSystemNode = new JsonObject
            {
                ["type"] = WidgetTypeContract.MediaSystem,
                ["id"] = mediaSystem.Id,
                ["width"] = 0f,
                ["height"] = 0f,
                ["source"] = MediaMetricContract.OutputSource,
                ["profiles"] = JsonSerializer.SerializeToNode(mediaSystem.Profiles)
            };
            var root = new JsonObject
            {
                ["schemaVersion"] = DashboardSchemaMigration.CanvasImageLayerSchemaVersion,
                ["canvas"] = new JsonObject
                {
                    ["width"] = FrameGeometry.DefaultNativeWidth,
                    ["height"] = FrameGeometry.DefaultNativeHeight,
                    ["orientation"] = 0,
                    ["backgroundColor"] = "#000000"
                },
                ["widgets"] = new JsonArray(mediaSystemNode)
            };

            var definition = DashboardDefinition.Parse(root.ToJsonString(), dashboardPath);
            var parsed = definition.Widgets.Single();
            Assert(StateVisualProfileContract.UsesOnlyValueSources(parsed),
                "Current schema must deserialize value-backed Media System profiles.");

            var missingText = JsonNode.Parse(root.ToJsonString()) as JsonObject
                ?? throw new InvalidOperationException("Could not clone Media System schema document.");
            var missingWidget = ((missingText["widgets"] as JsonArray)![0] as JsonObject)!;
            var missingProfiles = (missingWidget["profiles"] as JsonObject)!;
            ((missingProfiles[MediaMetricContract.EndpointSpeakers] as JsonObject)!).Remove("text");
            var missingRejected = false;
            try
            {
                _ = DashboardDefinition.Parse(missingText.ToJsonString(), dashboardPath);
            }
            catch (InvalidDataException)
            {
                missingRejected = true;
            }
            Assert(missingRejected,
                "Current Media System profiles must explicitly persist Text presentation for every state.");

            var obsolete = JsonNode.Parse(root.ToJsonString()) as JsonObject
                ?? throw new InvalidOperationException("Could not clone Media System obsolete-presentation document.");
            var obsoleteWidget = ((obsolete["widgets"] as JsonArray)![0] as JsonObject)!;
            obsoleteWidget["fit"] = StateVisualProfileContract.FitContain;
            var obsoleteRejected = false;
            try
            {
                _ = DashboardDefinition.Parse(obsolete.ToJsonString(), dashboardPath);
            }
            catch (InvalidDataException)
            {
                obsoleteRejected = true;
            }
            Assert(obsoleteRejected,
                "Current schema must reject obsolete top-level Media System image/text presentation.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void MediaSystemStateTextRendering()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-media-system-state-text-rendering");
        try
        {
            var widget = new WidgetDefinition
            {
                Type = WidgetTypeContract.MediaSystem,
                Id = "media-system-state-text-rendering",
                X = 10f,
                Y = 10f,
                Width = 0f,
                Height = 0f,
                Source = MediaMetricContract.OutputSource,
                FontSize = 96f,
                Align = "right",
                VerticalAlign = "bottom",
                OverflowMode = ValueOverflowContract.Clip
            };
            widget.EnsureStateVisualProfiles();
            foreach (var stateProfile in widget.Profiles!.Values)
            {
                stateProfile.SourceType = StateVisualProfileContract.SourceValue;
                stateProfile.Source = string.Empty;
                stateProfile.Text!.OverflowMode = ValueOverflowContract.None;
            }

            var speakersText = widget.Profiles[MediaMetricContract.EndpointSpeakers].Text!;
            speakersText.FontSize = 40f;
            speakersText.Align = "left";
            speakersText.VerticalAlign = "top";

            var lineText = widget.Profiles[MediaMetricContract.EndpointLineLevel].Text!;
            lineText.FontSize = 14f;
            lineText.Align = "left";
            lineText.VerticalAlign = "top";

            widget.Validate(tempRoot);

            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, new[] { CurrentWidget(widget) });
            using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    [MediaMetricContract.OutputAvailable] = true,
                    [MediaMetricContract.OutputType] = MediaMetricContract.EndpointSpeakers
                },
                antialias: false,
                typefaces);

            var canonicalWidget = (PinkieSysMon.DashboardModel.MediaSystemWidgetDefinition)
                PropertyModelNormalizationMigration.MapLegacyWidget(widget);
            Assert(context.TryResolveMediaSystemVisualProfile(canonicalWidget, out var stateKey, out var activeProfile) &&
                   stateKey == MediaMetricContract.EndpointSpeakers &&
                   StateVisualProfileContract.IsValueSource(activeProfile.SourceType) &&
                   activeProfile.Text?.FontSize == 40f,
                "Media System state resolver must select the profile driven by the active endpoint type.");

            var speakersBounds = PinkieSysMon.Widgets.WidgetGeometry.GetLayoutBounds(CurrentWidget(widget), context);
            context.UpdateMetrics(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    [MediaMetricContract.OutputAvailable] = true,
                    [MediaMetricContract.OutputType] = MediaMetricContract.EndpointLineLevel
                });
            var lineBounds = PinkieSysMon.Widgets.WidgetGeometry.GetLayoutBounds(CurrentWidget(widget), context);

            Assert(speakersBounds.Height > lineBounds.Height * 1.8f,
                $"Media System geometry did not follow active state Font Size: speakers={speakersBounds.Height}, line-level={lineBounds.Height}.");
            Assert(speakersBounds.Height < 80f && lineBounds.Height < 40f,
                "Media System geometry appears to be using obsolete widget-level Font Size instead of state Text profiles.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void MediaSystemSchema15Migration()
    {
        var root = JsonNode.Parse(
            $$"""
            {
              "schemaVersion": 15,
              "canvas": { "orientation": 0, "backgroundColor": "#000000" },
              "widgets": [
                {
                  "type": "media.system",
                  "id": "media-system-schema15",
                  "width": 64,
                  "height": 64,
                  "source": "{{MediaMetricContract.OutputSource}}",
                  "profiles": {
                    "{{MediaMetricContract.EndpointSpeakers}}": {
                      "sourceType": "icon",
                      "source": "lucide:speaker",
                      "color": "#FF123456",
                      "opacity": 0.75
                    }
                  }
                }
              ]
            }
            """) as JsonObject ?? throw new InvalidOperationException("Could not create schema 15 Media System migration document.");

        Assert(DashboardSchemaMigration.UpgradeToCurrent(root),
            "Schema 15 dashboard must migrate Media System presentation to the current schema.");
        Assert(root["schemaVersion"]!.GetValue<int>() == DashboardSchemaMigration.PropertyModelNormalizationSchemaVersion,
            "Media System migration must end at current schema 19.");

        var widget = ((root["widgets"] as JsonArray)?[0] as JsonObject)
            ?? throw new InvalidOperationException("Migrated Media System widget is missing.");
        Assert(widget["mediaSource"]?.GetValue<string>() == MediaMetricContract.OutputSource &&
               !widget.ContainsKey("source"),
            "Schema 15 migration must replace overloaded Media System source with canonical mediaSource.");

        var profiles = widget["profiles"] as JsonObject
            ?? throw new InvalidOperationException("Migrated Media System profiles are missing.");
        Assert(profiles.Count == StateVisualProfileContract.GetSpecs(WidgetTypeContract.MediaSystem).Count,
            "Media System migration must materialize every canonical state profile.");

        foreach (var spec in StateVisualProfileContract.GetSpecs(WidgetTypeContract.MediaSystem))
            AssertCanonicalMigratedStateProfile(profiles, spec.Key, StateVisualProfileContract.FitContain);

        var speakers = profiles[MediaMetricContract.EndpointSpeakers] as JsonObject
            ?? throw new InvalidOperationException("Migrated Media System Speakers profile is missing.");
        var asset = speakers["asset"] as JsonObject
            ?? throw new InvalidOperationException("Migrated Media System Speakers asset is missing.");
        Assert(speakers["contentType"]?.GetValue<string>() == C.StateContentType.Image &&
               asset["sourceType"]?.GetValue<string>() == C.ImageAssetSourceType.Icon &&
               asset["source"]?.GetValue<string>() == "lucide:speaker" &&
               speakers["color"]?.GetValue<string>() == "#FF123456" &&
               Math.Abs(speakers["opacity"]!.GetValue<float>() - 0.75f) < 0.001f,
            "Media System migration must preserve existing state source/color/opacity settings in canonical profile structure.");
    }

    private static void BinarySchema10Migration()
    {
        var root = JsonNode.Parse(
            $$"""
            {
              "schemaVersion": 10,
              "canvas": { "orientation": 0, "backgroundColor": "#000000" },
              "widgets": [
                {
                  "type": "binary",
                  "id": "binary-explicit-legacy-image-settings",
                  "width": 96,
                  "height": 96,
                  "metric": "{{RuntimeMetricContract.Fps}}",
                  "fit": "CoVeR",
                  "loop": false,
                  "fontFamily": "Roboto",
                  "fontSize": 27,
                  "fontWeight": 700,
                  "italic": true,
                  "align": "center",
                  "verticalAlign": "middle",
                  "outlineWidth": 2,
                  "outlineColor": "#FF112233",
                  "overflowMode": "Clip",
                  "scrollSpeed": 33,
                  "bumpPauseMs": 900,
                  "profiles": {
                    "true":  { "sourceType": "icon", "source": "lucide:check", "color": "#FFFFFFFF", "opacity": 1.0 },
                    "false": { "sourceType": "icon", "source": "lucide:check", "color": "#FFFFFFFF", "opacity": 0.5 }
                  }
                },
                {
                  "type": "binary",
                  "id": "binary-fit-only-legacy-image-settings",
                  "width": 96,
                  "height": 96,
                  "metric": "{{RuntimeMetricContract.Fps}}",
                  "fit": "contain"
                },
                {
                  "type": "binary",
                  "id": "binary-implicit-legacy-image-settings",
                  "width": 96,
                  "height": 96,
                  "metric": "{{RuntimeMetricContract.Fps}}"
                },
                {
                  "type": "image",
                  "id": "image-global-fit-loop-preserved",
                  "width": 96,
                  "height": 96,
                  "sourceType": "icon",
                  "source": "lucide:check",
                  "fit": "cover",
                  "loop": false
                }
              ]
            }
            """) as JsonObject ?? throw new InvalidOperationException("Could not create schema 10 migration test document.");

        Assert(DashboardSchemaMigration.UpgradeToCurrent(root),
            "Schema 10 dashboard must report a migration to the current schema.");
        Assert(root["schemaVersion"]!.GetValue<int>() == DashboardSchemaMigration.PropertyModelNormalizationSchemaVersion,
            "Schema 10 migration must end at current schema 19.");

        var widgets = root["widgets"] as JsonArray
            ?? throw new InvalidOperationException("Migrated dashboard widgets are missing.");
        var explicitWidget = widgets[0] as JsonObject
            ?? throw new InvalidOperationException("Explicit legacy Binary is missing.");
        var fitOnlyWidget = widgets[1] as JsonObject
            ?? throw new InvalidOperationException("Fit-only legacy Binary is missing.");
        var implicitWidget = widgets[2] as JsonObject
            ?? throw new InvalidOperationException("Implicit legacy Binary is missing.");
        var imageWidget = widgets[3] as JsonObject
            ?? throw new InvalidOperationException("Legacy Image widget is missing.");

        static JsonObject Profile(JsonObject widget, string key) =>
            ((widget["profiles"] as JsonObject)?[key] as JsonObject)
            ?? throw new InvalidOperationException($"Migrated Binary profile '{key}' is missing.");

        Assert(!explicitWidget.ContainsKey("fit") && !explicitWidget.ContainsKey("loop") &&
               !explicitWidget.ContainsKey("fontFamily") && !explicitWidget.ContainsKey("fontSize") &&
               !explicitWidget.ContainsKey("fontWeight") && !explicitWidget.ContainsKey("italic") &&
               !explicitWidget.ContainsKey("align") && !explicitWidget.ContainsKey("verticalAlign") &&
               !explicitWidget.ContainsKey("outlineWidth") && !explicitWidget.ContainsKey("outlineColor") &&
               !explicitWidget.ContainsKey("overflowMode") && !explicitWidget.ContainsKey("scrollSpeed") &&
               !explicitWidget.ContainsKey("bumpPauseMs"),
            "Schema 10 migration must remove obsolete top-level Binary image and text presentation.");

        foreach (var key in new[] { BinarySignalContract.TrueKey, BinarySignalContract.FalseKey })
        {
            var profile = Profile(explicitWidget, key);
            var asset = profile["asset"] as JsonObject
                ?? throw new InvalidOperationException($"Migrated Binary profile '{key}' asset is missing.");
            var text = profile["textPresentation"] as JsonObject
                ?? throw new InvalidOperationException($"Migrated Binary profile '{key}' TextPresentation is missing.");
            Assert(profile["contentType"]?.GetValue<string>() == C.StateContentType.Image &&
                   asset["fit"]?.GetValue<string>() == StateVisualProfileContract.FitCover &&
                   asset["loop"]?.GetValue<bool>() == false,
                "Schema 10 migration must preserve explicit legacy Fit/Loop semantically in canonical state assets.");
            Assert(text["fontFamily"]!.GetValue<string>() == "Roboto" &&
                   Math.Abs(text["fontSize"]!.GetValue<float>() - 27f) < 0.001f &&
                   text["fontWeight"]!.GetValue<int>() == 700 &&
                   text["italic"]!.GetValue<bool>() &&
                   text["align"]!.GetValue<string>() == "center" &&
                   text["verticalAlign"]!.GetValue<string>() == "middle" &&
                   Math.Abs(text["outlineWidth"]!.GetValue<float>() - 2f) < 0.001f &&
                   text["outlineColor"]!.GetValue<string>() == "#FF112233" &&
                   text["overflowMode"]!.GetValue<string>() == ValueOverflowContract.Clip &&
                   Math.Abs(text["scrollSpeed"]!.GetValue<float>() - 33f) < 0.001f &&
                   text["bumpPauseMs"]!.GetValue<int>() == 900,
                "Schema 10 migration must preserve legacy global Text presentation in canonical state TextPresentation.");
        }

        foreach (var key in new[] { BinarySignalContract.TrueKey, BinarySignalContract.FalseKey })
        {
            var profile = Profile(fitOnlyWidget, key);
            var asset = profile["asset"] as JsonObject
                ?? throw new InvalidOperationException($"Migrated fit-only Binary profile '{key}' asset is missing.");
            Assert(asset["fit"]?.GetValue<string>() == StateVisualProfileContract.FitContain &&
                   asset["loop"]?.GetValue<bool>() == true &&
                   profile["textPresentation"] is JsonObject,
                "Schema 10 migration must preserve explicit Fit, runtime Loop=true, and canonical TextPresentation.");
        }

        foreach (var key in new[] { BinarySignalContract.TrueKey, BinarySignalContract.FalseKey })
        {
            var profile = Profile(implicitWidget, key);
            var asset = profile["asset"] as JsonObject
                ?? throw new InvalidOperationException($"Migrated implicit Binary profile '{key}' asset is missing.");
            Assert(asset["fit"]?.GetValue<string>() == StateVisualProfileContract.FitStretch &&
                   asset["loop"]?.GetValue<bool>() == true &&
                   profile["textPresentation"] is JsonObject,
                "Schema 10 migration must preserve runtime image defaults and create canonical TextPresentation.");
        }

        var imageAsset = imageWidget["asset"] as JsonObject
            ?? throw new InvalidOperationException("Migrated Image asset is missing.");
        Assert(imageAsset["fit"]?.GetValue<string>() == StateVisualProfileContract.FitCover &&
               imageAsset["loop"]?.GetValue<bool>() == false &&
               imageAsset["sourceType"]?.GetValue<string>() == C.ImageAssetSourceType.Icon &&
               imageAsset["source"]?.GetValue<string>() == "lucide:check",
            "Schema 10 migration must preserve ordinary Image source/Fit/Loop in canonical Asset.");

        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-binary-migration-parse-regression");
        try
        {
            var dashboardPath = Path.Combine(tempRoot, "dashboards", "migrated-current-schema.json");
            var definition = DashboardJson.ParseCurrent(root.ToJsonString(), dashboardPath);
            Assert(definition.SchemaVersion == DashboardSchemaMigration.PropertyModelNormalizationSchemaVersion &&
                   definition.Widgets.Count == 4,
                "Migrated Binary dashboard must satisfy the canonical current schema contract.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void RemovedLegacyWidgetTypes()
    {
        Assert(!WidgetTypeContract.KnownTypes.Contains("grid", StringComparer.OrdinalIgnoreCase) &&
               !WidgetTypeContract.KnownTypes.Contains("rect", StringComparer.OrdinalIgnoreCase) &&
               !WidgetTypeContract.KnownTypes.Contains("graph", StringComparer.OrdinalIgnoreCase),
            "Removed Grid/Rect/Graph widget types must not remain in the runtime type contract.");
        Assert(!WidgetTypeContract.EditorAddableTypes.Contains("grid", StringComparer.OrdinalIgnoreCase) &&
               !WidgetTypeContract.EditorAddableTypes.Contains("rect", StringComparer.OrdinalIgnoreCase) &&
               !WidgetTypeContract.EditorAddableTypes.Contains("graph", StringComparer.OrdinalIgnoreCase),
            "Removed Grid/Rect/Graph widget types must not remain addable in the editor.");

        var widgetProperties = TypeDescriptor.GetProperties(typeof(C.WidgetDefinition));
        Assert(widgetProperties["Spacing"] is null &&
               widgetProperties["FillColor"] is null &&
               widgetProperties["History"] is null &&
               widgetProperties["SampleIntervalMs"] is null &&
               widgetProperties["StrokeWidth"] is null,
            "Removed legacy widget-only properties must not remain in the canonical model.");

        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-removed-widget-regression");
        try
        {
            var dashboardPath = Path.Combine(tempRoot, "dashboards", "removed-widgets.json");
            foreach (var type in new[] { "grid", "rect", "graph" })
            {
                var rejected = false;
                try
                {
                    _ = DashboardJson.ParseCurrent(
                        $$"""
                        {
                          "schemaVersion": {{DashboardSchemaMigration.PropertyModelNormalizationSchemaVersion}},
                          "canvas": {
                            "width": 1920,
                            "height": 480,
                            "orientation": 0,
                            "backgroundColor": "#000000",
                            "backgroundImage": { "asset": { "sourceType": "file", "source": null, "fit": "stretch", "loop": true }, "opacity": 1, "color": "#FFFFFFFF" },
                            "foregroundImage": { "asset": { "sourceType": "file", "source": null, "fit": "stretch", "loop": true }, "opacity": 1, "color": "#FFFFFFFF" }
                          },
                          "widgets": [
                            { "type": "{{type}}", "id": "removed-{{type}}", "width": 20, "height": 20 }
                          ]
                        }
                        """,
                        dashboardPath);
                }
                catch (InvalidDataException)
                {
                    rejected = true;
                }

                Assert(rejected, $"Removed widget type '{type}' must be rejected by the canonical current dashboard reader.");
            }

            var legacy = JsonNode.Parse(
                """
                {
                  "schemaVersion": 11,
                  "canvas": { "orientation": 0, "backgroundColor": "#000000" },
                  "widgets": [
                    {
                      "type": "value",
                      "id": "legacy-inert-fields",
                      "width": 100,
                      "height": 20,
                      "valueSource": "text",
                      "text": "OK",
                      "spacing": 48,
                      "fillColor": "#FFFFFFFF",
                      "history": 120,
                      "sampleIntervalMs": 500,
                      "strokeWidth": 2
                    }
                  ]
                }
                """) as JsonObject ?? throw new InvalidOperationException("Could not create schema 11 legacy-property migration document.");

            Assert(DashboardSchemaMigration.UpgradeToCurrent(legacy),
                "Schema 11 dashboard must migrate to the canonical current schema after legacy primitive removal.");
            Assert(legacy["schemaVersion"]?.GetValue<int>() == DashboardSchemaMigration.PropertyModelNormalizationSchemaVersion,
                "Legacy primitive removal chain must terminate at schema 19.");
            var migratedWidget = ((legacy["widgets"] as JsonArray)?[0] as JsonObject)
                ?? throw new InvalidOperationException("Migrated legacy-property widget is missing.");
            Assert(!migratedWidget.ContainsKey("spacing") &&
                   !migratedWidget.ContainsKey("fillColor") &&
                   !migratedWidget.ContainsKey("history") &&
                   !migratedWidget.ContainsKey("sampleIntervalMs") &&
                   !migratedWidget.ContainsKey("strokeWidth"),
                "Schema 11 migration must remove inert properties owned only by removed legacy widgets.");
            _ = DashboardJson.ParseCurrent(legacy.ToJsonString(), dashboardPath);

            foreach (var type in new[] { "grid", "rect", "graph" })
            {
                var obsolete = JsonNode.Parse(
                    $$"""
                    {
                      "schemaVersion": 11,
                      "canvas": { "orientation": 0, "backgroundColor": "#000000" },
                      "widgets": [
                        { "type": "{{type}}", "id": "legacy-{{type}}", "width": 20, "height": 20 }
                      ]
                    }
                    """) as JsonObject ?? throw new InvalidOperationException("Could not create schema 11 removed-widget migration document.");

                var migrationRejected = false;
                try
                {
                    _ = DashboardSchemaMigration.UpgradeToCurrent(obsolete);
                }
                catch (InvalidDataException)
                {
                    migrationRejected = true;
                }

                Assert(migrationRejected,
                    $"Schema 11 migration must reject removed widget type '{type}' instead of carrying it into schema 19.");
            }
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void TextPropertyHierarchy()
    {
        var value = new WidgetDefinition
        {
            Type = WidgetTypeContract.Value,
            Id = "text-tab-value",
            ValueSource = TextValueContract.SourceText,
            Text = "STATIC",
            Width = 100f,
            OverflowMode = ValueOverflowContract.Clip
        };
        var valueText = CurrentPropertyView(value, section: EditorPropertySection.Text).GetProperties();
        Assert(valueText["Font"] is PropertyGroupPropertyDescriptor &&
               valueText["Alignment"] is PropertyGroupPropertyDescriptor &&
               valueText["Outline"] is PropertyGroupPropertyDescriptor &&
               valueText["Overflow"] is PropertyGroupPropertyDescriptor,
            "Text/Value must expose Font, Alignment, Outline, and Overflow as first-level Text groups.");
        Assert(GetNestedProperty(valueText, "Font", nameof(WidgetDefinition.FontFamily)) is { DisplayName: "Family" } &&
               GetNestedProperty(valueText, "Font", nameof(WidgetDefinition.FontSize)) is { DisplayName: "Size" } &&
               GetNestedProperty(valueText, "Font", nameof(WidgetDefinition.FontWeight)) is { DisplayName: "Weight" } &&
               GetNestedProperty(valueText, "Font", nameof(WidgetDefinition.Italic)) is { DisplayName: "Italic" },
            "Text/Value Font group is incomplete.");
        Assert(GetNestedProperty(valueText, "Alignment", nameof(WidgetDefinition.Align)) is { DisplayName: "Horizontal" } &&
               GetNestedProperty(valueText, "Alignment", nameof(WidgetDefinition.VerticalAlign)) is { DisplayName: "Vertical" },
            "Text/Value Alignment group is incomplete.");
        Assert(GetNestedProperty(valueText, "Outline", nameof(WidgetDefinition.OutlineColor)) is { DisplayName: "Color" } &&
               GetNestedProperty(valueText, "Outline", nameof(WidgetDefinition.OutlineWidth)) is { DisplayName: "Width" },
            "Text/Value Outline group is incomplete.");
        var valueScrollSpeed = GetNestedProperty(valueText, "Overflow", nameof(WidgetDefinition.ScrollSpeed));
        var valueBumpPause = GetNestedProperty(valueText, "Overflow", nameof(WidgetDefinition.BumpPauseMs));
        Assert(GetNestedProperty(valueText, "Overflow", nameof(WidgetDefinition.OverflowMode)) is { DisplayName: "Mode" } &&
               valueScrollSpeed is { DisplayName: "Scroll Speed", IsReadOnly: true } &&
               valueBumpPause is { DisplayName: "Bump Pause", IsReadOnly: true } &&
               valueScrollSpeed.Description == "Not applicable for selected Overflow Mode." &&
               valueBumpPause.Description == "Not applicable for selected Overflow Mode.",
            "Text/Value Overflow group must keep Scroll Speed and Bump Pause visible but disabled for Clip mode.");

        var iconBinary = new WidgetDefinition
        {
            Type = WidgetTypeContract.Binary,
            Id = "text-tab-binary-icon",
            Metric = RuntimeMetricContract.Fps,
            Width = 100f,
            Height = 40f
        };
        iconBinary.EnsureStateVisualProfiles();
        var iconBinaryText = CurrentPropertyView(iconBinary, section: EditorPropertySection.Text).GetProperties();
        Assert(iconBinaryText.Count == 0,
            "Icon-only Binary must have no Text section, allowing the Text tab to be omitted.");

        iconBinary.Profiles![BinarySignalContract.TrueKey].SourceType = StateVisualProfileContract.SourceValue;
        TextOverflowStateContract.ApplyContextChange(iconBinary, 100f, 32f);
        iconBinary.Profiles[BinarySignalContract.TrueKey].Text!.OverflowMode = ValueOverflowContract.Clip;
        var valueBinaryText = CurrentPropertyView(iconBinary, section: EditorPropertySection.Text).GetProperties();
        Assert(valueBinaryText["State: True"] is PropertyGroupPropertyDescriptor &&
               valueBinaryText["State: False"] is null,
            "Binary with one value-backed state must expose only that state as a first-level Text group.");
        var trueFontName =
            $"{StateVisualProfilePropertyDescriptor.PropertyPrefix}{BinarySignalContract.TrueKey}:{StateVisualProfileField.FontFamily}";
        var trueAlignName =
            $"{StateVisualProfilePropertyDescriptor.PropertyPrefix}{BinarySignalContract.TrueKey}:{StateVisualProfileField.Align}";
        var trueOutlineName =
            $"{StateVisualProfilePropertyDescriptor.PropertyPrefix}{BinarySignalContract.TrueKey}:{StateVisualProfileField.OutlineColor}";
        var trueOverflowName =
            $"{StateVisualProfilePropertyDescriptor.PropertyPrefix}{BinarySignalContract.TrueKey}:{StateVisualProfileField.OverflowMode}";
        var trueScrollSpeedName =
            $"{StateVisualProfilePropertyDescriptor.PropertyPrefix}{BinarySignalContract.TrueKey}:{StateVisualProfileField.ScrollSpeed}";
        var trueBumpPauseName =
            $"{StateVisualProfilePropertyDescriptor.PropertyPrefix}{BinarySignalContract.TrueKey}:{StateVisualProfileField.BumpPauseMs}";
        var trueScrollSpeed = GetNestedProperty(valueBinaryText, "State: True", "Overflow", trueScrollSpeedName);
        var trueBumpPause = GetNestedProperty(valueBinaryText, "State: True", "Overflow", trueBumpPauseName);
        Assert(GetNestedProperty(valueBinaryText, "State: True", "Font", trueFontName) is { DisplayName: "Family" } &&
               GetNestedProperty(valueBinaryText, "State: True", "Alignment", trueAlignName) is { DisplayName: "Horizontal" } &&
               GetNestedProperty(valueBinaryText, "State: True", "Outline", trueOutlineName) is { DisplayName: "Color" } &&
               GetNestedProperty(valueBinaryText, "State: True", "Overflow", trueOverflowName) is { DisplayName: "Mode" } &&
               trueScrollSpeed is { DisplayName: "Scroll Speed", IsReadOnly: true } &&
               trueBumpPause is { DisplayName: "Bump Pause", IsReadOnly: true } &&
               trueScrollSpeed.Description == "Not applicable for selected Overflow Mode." &&
               trueBumpPause.Description == "Not applicable for selected Overflow Mode.",
            "Binary state Text group must keep contextual Overflow rows visible but disabled for Clip mode.");

        iconBinary.Profiles[BinarySignalContract.FalseKey].SourceType = StateVisualProfileContract.SourceValue;
        TextOverflowStateContract.ApplyContextChange(iconBinary, 100f, 32f);
        var bothValueText = CurrentPropertyView(iconBinary, section: EditorPropertySection.Text).GetProperties();
        Assert(bothValueText["State: True"] is not null && bothValueText["State: False"] is not null,
            "Value-only Binary must expose independent first-level Text groups for True and False.");

        var gauge = new WidgetDefinition
        {
            Type = WidgetTypeContract.Gauge,
            Id = "text-tab-gauge",
            Metric = RuntimeMetricContract.Fps,
            Width = 100f,
            Height = 100f
        };
        var gaugeText = CurrentPropertyView(gauge, section: EditorPropertySection.Text).GetProperties();
        Assert(gaugeText.Count == 0,
            "Gauge must not expose the contextual Text section without text presentation.");
    }

    private static void PropertyApplicabilityContract()
    {
        const string overflowReason = "Not applicable for selected Overflow Mode.";
        const string enabledReason = "Not applicable for selected Enabled.";
        const string typeReason = "Not applicable for selected Type.";
        const string modeReason = "Not applicable for selected Mode.";

        static string[] ChildDisplayNames(PropertyDescriptorCollection properties, string groupName)
        {
            var group = properties[groupName]
                ?? throw new InvalidOperationException($"Property group '{groupName}' is missing.");
            var value = group.GetValue(null)
                ?? throw new InvalidOperationException($"Property group '{groupName}' has no value.");
            return TypeDescriptor.GetProperties(value)
                .Cast<PropertyDescriptor>()
                .Select(property => property.DisplayName)
                .ToArray();
        }

        static void AssertDisabled(PropertyDescriptor? property, string reason, string context)
        {
            Assert(property is not null, $"{context}: property is missing.");
            Assert(property!.IsReadOnly, $"{context}: property must be disabled/read-only.");
            Assert(string.Equals(property.Description, reason, StringComparison.Ordinal),
                $"{context}: disabled reason is '{property.Description}', expected '{reason}'.");
        }

        static void AssertEnabled(PropertyDescriptor? property, string context)
        {
            Assert(property is not null, $"{context}: property is missing.");
            Assert(!property!.IsReadOnly, $"{context}: property must be editable.");
        }

        var value = new WidgetDefinition
        {
            Type = WidgetTypeContract.Value,
            Id = "applicability-value",
            ValueSource = TextValueContract.SourceMetric,
            Metric = RuntimeMetricContract.Version,
            Width = 100f,
            OverflowMode = ValueOverflowContract.Clip,
            ShadowEnabled = false
        };

        var sourceMetric = CurrentPropertyView(value, section: EditorPropertySection.General).GetProperties();
        var metricSourceOrder = ChildDisplayNames(sourceMetric, "Source");
        AssertEnabled(GetNestedProperty(sourceMetric, "Source", nameof(WidgetDefinition.Metric)),
            "Metric-source Value/Metric");
        AssertDisabled(GetNestedProperty(sourceMetric, "Source", nameof(WidgetDefinition.Text)), typeReason,
            "Metric-source Value/Text");

        value.ValueSource = TextValueContract.SourceText;
        value.Text = "STATIC";
        var sourceText = CurrentPropertyView(value, section: EditorPropertySection.General).GetProperties();
        Assert(metricSourceOrder.SequenceEqual(ChildDisplayNames(sourceText, "Source")),
            "Value Source row order must remain stable when Type changes.");
        AssertDisabled(GetNestedProperty(sourceText, "Source", nameof(WidgetDefinition.Metric)), typeReason,
            "Text-source Value/Metric");
        AssertEnabled(GetNestedProperty(sourceText, "Source", nameof(WidgetDefinition.Text)),
            "Text-source Value/Text");

        var textClip = CurrentPropertyView(value, section: EditorPropertySection.Text).GetProperties();
        var overflowOrder = ChildDisplayNames(textClip, "Overflow");
        AssertDisabled(GetNestedProperty(textClip, "Overflow", nameof(WidgetDefinition.ScrollSpeed)), overflowReason,
            "Clip overflow/Scroll Speed");
        AssertDisabled(GetNestedProperty(textClip, "Overflow", nameof(WidgetDefinition.BumpPauseMs)), overflowReason,
            "Clip overflow/Bump Pause");

        value.OverflowMode = ValueOverflowContract.Scroll;
        var textScroll = CurrentPropertyView(value, section: EditorPropertySection.Text).GetProperties();
        Assert(overflowOrder.SequenceEqual(ChildDisplayNames(textScroll, "Overflow")),
            "Overflow property order must remain stable when Mode changes.");
        AssertEnabled(GetNestedProperty(textScroll, "Overflow", nameof(WidgetDefinition.ScrollSpeed)),
            "Scroll overflow/Scroll Speed");
        AssertDisabled(GetNestedProperty(textScroll, "Overflow", nameof(WidgetDefinition.BumpPauseMs)), overflowReason,
            "Scroll overflow/Bump Pause");

        value.OverflowMode = ValueOverflowContract.Bump;
        var textBump = CurrentPropertyView(value, section: EditorPropertySection.Text).GetProperties();
        Assert(overflowOrder.SequenceEqual(ChildDisplayNames(textBump, "Overflow")),
            "Overflow property order must remain stable for Bump mode.");
        AssertEnabled(GetNestedProperty(textBump, "Overflow", nameof(WidgetDefinition.ScrollSpeed)),
            "Bump overflow/Scroll Speed");
        AssertEnabled(GetNestedProperty(textBump, "Overflow", nameof(WidgetDefinition.BumpPauseMs)),
            "Bump overflow/Bump Pause");

        var shadowOff = CurrentPropertyView(value, section: EditorPropertySection.Appearance).GetProperties();
        var shadowOrder = ChildDisplayNames(shadowOff, "Shadow");
        AssertDisabled(GetNestedProperty(shadowOff, "Shadow", nameof(WidgetDefinition.ShadowOffsetX)), enabledReason,
            "Disabled shadow/Offset X");
        AssertDisabled(GetNestedProperty(shadowOff, "Shadow", nameof(WidgetDefinition.ShadowColor)), enabledReason,
            "Disabled shadow/Color");

        value.ShadowEnabled = true;
        var shadowOn = CurrentPropertyView(value, section: EditorPropertySection.Appearance).GetProperties();
        Assert(shadowOrder.SequenceEqual(ChildDisplayNames(shadowOn, "Shadow")),
            "Shadow property order must remain stable when Enabled changes.");
        AssertEnabled(GetNestedProperty(shadowOn, "Shadow", nameof(WidgetDefinition.ShadowOffsetX)),
            "Enabled shadow/Offset X");
        AssertEnabled(GetNestedProperty(shadowOn, "Shadow", nameof(WidgetDefinition.ShadowColor)),
            "Enabled shadow/Color");

        var gauge = new WidgetDefinition
        {
            Type = WidgetTypeContract.Gauge,
            Id = "applicability-gauge",
            Metric = RuntimeMetricContract.Fps,
            Width = 100f,
            Height = 100f,
            TrackEnabled = false,
            NeedleEnabled = false,
            Threshold1Enabled = false
        };
        var gaugeAppearanceOff = CurrentPropertyView(gauge, section: EditorPropertySection.Appearance).GetProperties();
        var thresholdOrder = ChildDisplayNames(gaugeAppearanceOff, "Threshold 1");
        AssertDisabled(GetNestedProperty(gaugeAppearanceOff, "Threshold 1", nameof(WidgetDefinition.Threshold1Value)), enabledReason,
            "Disabled threshold/Value");
        AssertDisabled(GetNestedProperty(gaugeAppearanceOff, "Threshold 1", nameof(WidgetDefinition.Threshold1Color)), enabledReason,
            "Disabled threshold/Color");

        gauge.Threshold1Enabled = true;
        var gaugeAppearanceOn = CurrentPropertyView(gauge, section: EditorPropertySection.Appearance).GetProperties();
        Assert(thresholdOrder.SequenceEqual(ChildDisplayNames(gaugeAppearanceOn, "Threshold 1")),
            "Threshold property order must remain stable when Enabled changes.");
        AssertEnabled(GetNestedProperty(gaugeAppearanceOn, "Threshold 1", nameof(WidgetDefinition.Threshold1Value)),
            "Enabled threshold/Value");
        AssertEnabled(GetNestedProperty(gaugeAppearanceOn, "Threshold 1", nameof(WidgetDefinition.Threshold1Color)),
            "Enabled threshold/Color");

        var gaugeOff = CurrentPropertyView(gauge, section: EditorPropertySection.Gauge).GetProperties();
        var horseshoeOrder = ChildDisplayNames(gaugeOff, "Horseshoe");
        var needleOrder = ChildDisplayNames(gaugeOff, "Needle");
        AssertEnabled(GetNestedProperty(gaugeOff, "Horseshoe", nameof(WidgetDefinition.Reverse)),
            "Disabled Gauge track/Reverse");
        AssertDisabled(GetNestedProperty(gaugeOff, "Horseshoe", nameof(WidgetDefinition.Thickness)), enabledReason,
            "Disabled Gauge track/Thickness");
        AssertDisabled(GetNestedProperty(gaugeOff, "Needle", nameof(WidgetDefinition.NeedleThickness)), enabledReason,
            "Disabled Gauge needle/Thickness");

        gauge.TrackEnabled = true;
        gauge.NeedleEnabled = true;
        var gaugeOn = CurrentPropertyView(gauge, section: EditorPropertySection.Gauge).GetProperties();
        Assert(horseshoeOrder.SequenceEqual(ChildDisplayNames(gaugeOn, "Horseshoe")) &&
               needleOrder.SequenceEqual(ChildDisplayNames(gaugeOn, "Needle")),
            "Gauge property order must remain stable when track/needle Enabled changes.");
        AssertEnabled(GetNestedProperty(gaugeOn, "Horseshoe", nameof(WidgetDefinition.Thickness)),
            "Enabled Gauge track/Thickness");
        AssertEnabled(GetNestedProperty(gaugeOn, "Needle", nameof(WidgetDefinition.NeedleThickness)),
            "Enabled Gauge needle/Thickness");

        var image = new WidgetDefinition
        {
            Type = WidgetTypeContract.Image,
            Id = "applicability-image",
            SourceType = "icon",
            Source = "lucide:activity",
            Width = 100f,
            Height = 100f
        };
        var iconImage = CurrentPropertyView(image, section: EditorPropertySection.Image).GetProperties();
        var imageOrder = ChildDisplayNames(iconImage, "Image");
        AssertDisabled(GetNestedProperty(iconImage, "Image", nameof(WidgetDefinition.Loop)), "Not applicable for selected Source.",
            "Icon image/Loop");

        image.SourceType = "file";
        image.Source = "example.gif";
        var fileImage = CurrentPropertyView(image, section: EditorPropertySection.Image).GetProperties();
        Assert(imageOrder.SequenceEqual(ChildDisplayNames(fileImage, "Image")),
            "Image property order must remain stable when Source Type changes.");
        AssertEnabled(GetNestedProperty(fileImage, "Image", nameof(WidgetDefinition.Loop)),
            "File image/Loop");

        var binary = new WidgetDefinition
        {
            Type = WidgetTypeContract.Binary,
            Id = "applicability-binary",
            Metric = RuntimeMetricContract.Fps,
            Width = 100f,
            Height = 40f,
            EvaluationMode = BinarySignalContract.EvaluationModeAuto
        };
        binary.EnsureStateVisualProfiles();
        var binaryAuto = CurrentPropertyView(binary, section: EditorPropertySection.Data).GetProperties();
        var evaluationOrder = ChildDisplayNames(binaryAuto, "Evaluation");
        AssertDisabled(GetNestedProperty(binaryAuto, "Evaluation", nameof(WidgetDefinition.TrueIf)), modeReason,
            "Binary Auto/True If");
        AssertDisabled(GetNestedProperty(binaryAuto, "Evaluation", nameof(WidgetDefinition.Setpoint)), modeReason,
            "Binary Auto/Setpoint");

        binary.EvaluationMode = BinarySignalContract.EvaluationModeSetpoint;
        var binarySetpoint = CurrentPropertyView(binary, section: EditorPropertySection.Data).GetProperties();
        Assert(evaluationOrder.SequenceEqual(ChildDisplayNames(binarySetpoint, "Evaluation")),
            "Binary Evaluation property order must remain stable when Mode changes.");
        AssertEnabled(GetNestedProperty(binarySetpoint, "Evaluation", nameof(WidgetDefinition.TrueIf)),
            "Binary Setpoint/True If");
        AssertEnabled(GetNestedProperty(binarySetpoint, "Evaluation", nameof(WidgetDefinition.Setpoint)),
            "Binary Setpoint/Setpoint");
    }

    private static void ContextDisabledCustomEditors()
    {
        var disabledGauge = new WidgetDefinition
        {
            Type = WidgetTypeContract.Gauge,
            Id = "context-editor-disabled-gauge",
            Metric = RuntimeMetricContract.Fps,
            TrackEnabled = true,
            NeedleEnabled = false
        };

        var disabledProperties = CurrentPropertyView(
            disabledGauge, section: EditorPropertySection.Gauge).GetProperties();
        var needleColor = GetNestedProperty(
            disabledProperties, "Needle", nameof(WidgetDefinition.NeedleColor));
        var pointerColor = GetNestedProperty(
            disabledProperties, "Needle", nameof(WidgetDefinition.NeedlePointerColor));
        Assert(needleColor is { IsReadOnly: true } &&
               needleColor.GetEditor(typeof(UITypeEditor)) is null &&
               pointerColor is { IsReadOnly: true } &&
               pointerColor.GetEditor(typeof(UITypeEditor)) is null,
            "Context-disabled Gauge color properties must suppress their custom UITypeEditor instead of looking editable.");

        disabledGauge.NeedleEnabled = true;
        var enabledProperties = CurrentPropertyView(
            disabledGauge, section: EditorPropertySection.Gauge).GetProperties();
        needleColor = GetNestedProperty(
            enabledProperties, "Needle", nameof(WidgetDefinition.NeedleColor));
        pointerColor = GetNestedProperty(
            enabledProperties, "Needle", nameof(WidgetDefinition.NeedlePointerColor));
        Assert(needleColor is { IsReadOnly: false } &&
               needleColor.GetEditor(typeof(UITypeEditor)) is RgbaColorEditor &&
               pointerColor is { IsReadOnly: false } &&
               pointerColor.GetEditor(typeof(UITypeEditor)) is RgbaColorEditor,
            "Re-enabling a contextual Gauge property must restore its custom editor immediately.");

        var mixedDisabled = new WidgetDefinition
        {
            Type = WidgetTypeContract.Gauge,
            Id = "context-editor-mixed-disabled",
            Metric = RuntimeMetricContract.Fps,
            NeedleEnabled = false
        };
        var mixedEnabled = new WidgetDefinition
        {
            Type = WidgetTypeContract.Gauge,
            Id = "context-editor-mixed-enabled",
            Metric = RuntimeMetricContract.Fps,
            NeedleEnabled = true
        };
        var mixedProperties = new EditorMultiPropertyView([
            CurrentPropertyView(mixedDisabled, section: EditorPropertySection.Gauge),
            CurrentPropertyView(mixedEnabled, section: EditorPropertySection.Gauge)
        ]).GetProperties();
        var mixedNeedleColor = GetNestedProperty(
            mixedProperties, "Needle", nameof(WidgetDefinition.NeedleColor));
        Assert(mixedNeedleColor is { IsReadOnly: true } &&
               mixedNeedleColor.GetEditor(typeof(UITypeEditor)) is null &&
               string.Equals(
                   mixedNeedleColor.Description,
                   "Not applicable for selected Enabled.",
                   StringComparison.Ordinal),
            "A custom-editor property disabled for one selected widget must remain in the multi-selection intersection but expose no editor.");

        var binary = new WidgetDefinition
        {
            Type = WidgetTypeContract.Binary,
            Id = "context-editor-state-profile",
            Metric = "test.context.editor"
        };
        binary.EnsureStateVisualProfiles();
        var trueProfile = binary.Profiles![BinarySignalContract.TrueKey];
        trueProfile.SourceType = StateVisualProfileContract.SourceFile;
        trueProfile.Source = "state.png";

        var stateImage = CurrentPropertyView(
            binary, section: EditorPropertySection.Image).GetProperties();
        var stateColor = GetNestedProperty(
            stateImage,
            "State: True",
            $"{StateVisualProfilePropertyDescriptor.PropertyPrefix}{BinarySignalContract.TrueKey}:{StateVisualProfileField.Color}");
        Assert(stateColor is { IsReadOnly: true } &&
               stateColor.GetEditor(typeof(UITypeEditor)) is null,
            "Context-disabled state-profile color properties must suppress their custom editor too.");
    }

    private static void DateTimeFormatEditorContract()
    {
        var widget = new C.ValueWidgetDefinition
        {
            Id = "datetime-format-editor",
            SourceKind = C.ValueSourceKind.Metric,
            Metric = SystemMetricContract.DateTime,
            Width = 180f,
            TextPresentation = new C.TextPresentationDefinition { OverflowMode = ValueOverflowContract.Clip }
        };

        var data = new EditorPropertyView(widget, section: EditorPropertySection.Data).GetProperties();
        var format = GetNestedProperty(data, "Value", nameof(C.ValueWidgetDefinition.Format));
        Assert(format is not null && !format.IsReadOnly,
            "DateTime Data/Value must expose an editable Format property.");
        Assert(string.Equals(
                Convert.ToString(format!.GetValue(data)),
                MetricValueFormatter.DefaultDateTimeFormat,
                StringComparison.Ordinal),
            "An empty DateTime Format must display the runtime default mask.");

        var editor = format.GetEditor(typeof(UITypeEditor)) as UITypeEditor;
        Assert(editor is DateTimeFormatEditor,
            "DateTime Format must expose the dedicated format selector/editor.");
        Assert(editor!.GetEditStyle(null) == UITypeEditorEditStyle.DropDown,
            "DateTime Format selector must use a PropertyGrid drop-down editor.");

        var expectedPresets = new[]
        {
            "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd", "yyyy", "MMMM", "MMM", "MM", "dd", "dddd", "ddd",
            "HH:mm", "HH:mm:ss", "hh:mm tt", "hh:mm:ss tt", "MMMM d", "MMMM d, yyyy",
            "dddd, MMMM d", "dddd, MMMM d, yyyy"
        };
        Assert(expectedPresets.SequenceEqual(DateTimeFormatCatalog.Formats),
            "DateTime Format selector preset order/content does not match the PASS 4 contract.");

        var sample = new DateTime(2026, 9, 29, 17, 8, 9, DateTimeKind.Local);
        var enUs = CultureInfo.GetCultureInfo("en-US");
        var presets = DateTimeFormatCatalog.CreatePresets(sample, enUs);
        Assert(presets.Count == expectedPresets.Length &&
               presets.All(preset => !string.IsNullOrWhiteSpace(preset.Preview)),
            "Every DateTime preset must expose a live example.");
        Assert(string.Equals(
                presets.Single(preset => preset.Format == "MMMM").Preview,
                "September",
                StringComparison.Ordinal),
            "MMMM preset preview must render the full month name for the selected culture.");

        const string custom = "dddd, MMMM d, yyyy 'at' HH:mm:ss";
        format.SetValue(data, custom);
        Assert(string.Equals(widget.Format, custom, StringComparison.Ordinal),
            "A valid custom DateTime format must be stored without rewriting it.");
        Assert(string.Equals(Convert.ToString(format.GetValue(data)), custom, StringComparison.Ordinal),
            "A valid custom DateTime format must remain editable even when it is not a preset.");

        var reloaded = (C.ValueWidgetDefinition)DashboardJson.CloneWidget(widget);
        Assert(string.Equals(reloaded.Format, custom, StringComparison.Ordinal),
            "A valid custom DateTime format must round-trip through canonical schema-19 widget JSON unchanged.");

        var descriptor = MetricContract.GetDescriptor(SystemMetricContract.DateTime)
            ?? throw new InvalidOperationException("DateTime metric descriptor is missing.");
        Assert(string.Equals(
                MetricValueFormatter.Format(sample, descriptor, null, custom, enUs),
                "Tuesday, September 29, 2026 at 17:08:09",
                StringComparison.Ordinal),
            "Custom DateTime format must continue to use the runtime formatter with current-culture semantics.");

        var previous = widget.Format;
        var rejected = false;
        try
        {
            format.SetValue(data, "yyyy-MM-dd '");
        }
        catch (FormatException)
        {
            rejected = true;
        }
        Assert(rejected, "An invalid custom DateTime format must be rejected by the editor descriptor.");
        Assert(string.Equals(widget.Format, previous, StringComparison.Ordinal),
            "Rejecting an invalid DateTime format must not mutate the persisted widget value.");

        Assert(DateTimeFormatCatalog.Tokens.Any(token => token.Pattern == "yyyy") &&
               DateTimeFormatCatalog.Tokens.Any(token => token.Pattern == "MMMM") &&
               DateTimeFormatCatalog.Tokens.Any(token => token.Pattern == "dddd") &&
               DateTimeFormatCatalog.Tokens.Any(token => token.Pattern == "HH") &&
               DateTimeFormatCatalog.Tokens.Any(token => token.Pattern == "mm") &&
               DateTimeFormatCatalog.Tokens.Any(token => token.Pattern == "ss") &&
               DateTimeFormatCatalog.Tokens.Any(token => token.Pattern == "tt") &&
               DateTimeFormatCatalog.Tokens.Any(token => token.Pattern == "%/") &&
               DateTimeFormatCatalog.Tokens.Any(token => token.Pattern == "%:"),
            "Custom DateTime builder must expose year/month/day/day-of-week/time/AM-PM/separator token choices.");
        Assert(string.Equals(DateTimeFormatCatalog.QuoteLiteral("at"), "'at'", StringComparison.Ordinal),
            "Custom DateTime builder must support safe literal-text insertion.");
    }

    private static void MultiSelectionCommonPropertyEditing()
    {
        var first = new C.ValueWidgetDefinition
        {
            Id = "multi-value-a",
            Name = "A",
            SourceKind = C.ValueSourceKind.Metric,
            Metric = RuntimeMetricContract.Fps,
            X = 10f,
            Y = 20f,
            Width = 110f,
            TextPresentation = new C.TextPresentationDefinition { OverflowMode = ValueOverflowContract.Clip }
        };
        var second = new C.ValueWidgetDefinition
        {
            Id = "multi-value-b",
            Name = "B",
            SourceKind = C.ValueSourceKind.Metric,
            Metric = RuntimeMetricContract.Fps,
            X = 30f,
            Y = 20f,
            Width = 140f,
            TextPresentation = new C.TextPresentationDefinition { OverflowMode = ValueOverflowContract.Clip }
        };

        var view = new EditorMultiPropertyView([
            new EditorPropertyView(first, section: EditorPropertySection.General),
            new EditorPropertyView(second, section: EditorPropertySection.General)
        ]);
        var properties = view.GetProperties();

        var x = GetNestedProperty(properties, "Geometry", nameof(C.WidgetDefinition.X));
        var y = GetNestedProperty(properties, "Geometry", nameof(C.WidgetDefinition.Y));
        var name = GetNestedProperty(properties, "Identity", nameof(C.WidgetDefinition.Name));
        Assert(x is MultiSelectionPropertyDescriptor && y is MultiSelectionPropertyDescriptor &&
               name is MultiSelectionPropertyDescriptor,
            "Canonical multi-selection common properties must use the multi-selection descriptor.");
        Assert(x!.GetValue(null) is null &&
               string.Equals(x.Converter.ConvertToInvariantString(x.GetValue(null)), "...", StringComparison.Ordinal),
            "Different canonical X values must be represented as a mixed value.");
        Assert(Convert.ToSingle(y!.GetValue(null), CultureInfo.InvariantCulture) == 20f,
            "Equal canonical common values must be shown normally.");

        x.SetValue(view, 64f);
        Assert(first.X == 64f && second.X == 64f,
            "Editing a canonical common mixed property must apply to every selected widget.");
        name!.SetValue(view, "Shared name");
        Assert(first.Name == "Shared name" && second.Name == "Shared name",
            "Editing a canonical common string property must apply to every selected widget.");

        first.Width = 111f;
        second.Width = 222f;
        _ = GetNestedProperty(properties, "Geometry", nameof(C.WidgetDefinition.Width))!.GetValue(null);
        Assert(first.Width == 111f && second.Width == 222f,
            "Reading an untouched canonical mixed property must preserve individual values.");

        var reversed = new EditorMultiPropertyView([
            new EditorPropertyView(second, section: EditorPropertySection.General),
            new EditorPropertyView(first, section: EditorPropertySection.General)
        ]).GetProperties();
        Assert(properties.Cast<PropertyDescriptor>().Select(property => property.Name)
                .SequenceEqual(reversed.Cast<PropertyDescriptor>().Select(property => property.Name)),
            "Selection order must not change canonical common top-level property order.");
        Assert(string.Equals(
                GetNestedProperty(reversed, "Geometry", nameof(C.WidgetDefinition.Width))!.Converter.ConvertToInvariantString(
                    GetNestedProperty(reversed, "Geometry", nameof(C.WidgetDefinition.Width))!.GetValue(null)),
                "...", StringComparison.Ordinal),
            "Selection order must not change canonical mixed-value semantics.");

        var gauge = new C.GaugeWidgetDefinition
        {
            Id = "multi-gauge",
            Metric = RuntimeMetricContract.Fps,
            X = 5f,
            Y = 6f,
            Width = 100f,
            Height = 100f
        };
        var heterogeneous = new EditorMultiPropertyView([
            new EditorPropertyView(first, section: EditorPropertySection.General),
            new EditorPropertyView(gauge, section: EditorPropertySection.General)
        ]).GetProperties();
        Assert(GetNestedProperty(heterogeneous, "Geometry", nameof(C.WidgetDefinition.X)) is not null,
            "Different canonical widget types must retain semantically common Geometry properties.");
        Assert(GetNestedProperty(heterogeneous, "Source", nameof(C.ValueWidgetDefinition.Metric)) is not null,
            "Different metric-backed canonical widgets must intersect by the shared metric semantic key.");
        Assert(GetNestedProperty(heterogeneous, "Source", nameof(C.ValueWidgetDefinition.SourceKind)) is null,
            "Value-only SourceKind must not leak into heterogeneous canonical multi-selection.");
    }

    private static void MultiSelectionApplicabilityAndContext()
    {
        const string overflowReason = "Not applicable for selected Overflow Mode.";
        const string sourceTypeReason = "Not applicable for selected Type.";

        var scroll = new C.ValueWidgetDefinition
        {
            Id = "multi-scroll",
            SourceKind = C.ValueSourceKind.Text,
            Text = "A",
            Width = 100f,
            TextPresentation = new C.TextPresentationDefinition
            {
                OverflowMode = ValueOverflowContract.Scroll,
                ScrollSpeed = 20f
            }
        };
        var clip = new C.ValueWidgetDefinition
        {
            Id = "multi-clip",
            SourceKind = C.ValueSourceKind.Text,
            Text = "B",
            Width = 100f,
            TextPresentation = new C.TextPresentationDefinition
            {
                OverflowMode = ValueOverflowContract.Clip,
                ScrollSpeed = 30f
            }
        };

        var mixedOverflow = new EditorMultiPropertyView([
            new EditorPropertyView(scroll, section: EditorPropertySection.Text),
            new EditorPropertyView(clip, section: EditorPropertySection.Text)
        ]);
        var scrollSpeed = GetNestedProperty(mixedOverflow.GetProperties(), "Overflow", nameof(C.TextPresentationDefinition.ScrollSpeed));
        Assert(scrollSpeed is not null && scrollSpeed.IsReadOnly &&
               string.Equals(scrollSpeed.Description, overflowReason, StringComparison.Ordinal),
            "A canonical property disabled for any selected widget must remain visible with its contextual reason.");
        scrollSpeed!.SetValue(mixedOverflow, 99f);
        Assert(scroll.TextPresentation.ScrollSpeed == 20f && clip.TextPresentation.ScrollSpeed == 30f,
            "Disabled canonical multi-selection edits must not partially apply.");

        clip.TextPresentation.OverflowMode = ValueOverflowContract.Scroll;
        var enabledOverflow = new EditorMultiPropertyView([
            new EditorPropertyView(scroll, section: EditorPropertySection.Text),
            new EditorPropertyView(clip, section: EditorPropertySection.Text)
        ]);
        scrollSpeed = GetNestedProperty(enabledOverflow.GetProperties(), "Overflow", nameof(C.TextPresentationDefinition.ScrollSpeed));
        Assert(scrollSpeed is not null && !scrollSpeed.IsReadOnly,
            "A canonical common property must become editable when applicable to every selected widget.");
        scrollSpeed!.SetValue(enabledOverflow, 55f);
        Assert(scroll.TextPresentation.ScrollSpeed == 55f && clip.TextPresentation.ScrollSpeed == 55f,
            "An enabled canonical multi-selection edit must apply to all selected widgets.");

        var fileImage = new C.ImageWidgetDefinition
        {
            Id = "multi-image-file",
            Width = 100f,
            Height = 100f,
            Asset = new C.ImageAssetPresentationDefinition { SourceType = C.ImageAssetSourceType.File, Source = "images/a.png" }
        };
        var iconImage = new C.ImageWidgetDefinition
        {
            Id = "multi-image-icon",
            Width = 100f,
            Height = 100f,
            Asset = new C.ImageAssetPresentationDefinition { SourceType = C.ImageAssetSourceType.Icon, Source = "activity" }
        };
        var imageView = new EditorMultiPropertyView([
            new EditorPropertyView(fileImage, "root", "dashboard", section: EditorPropertySection.General),
            new EditorPropertyView(iconImage, "root", "dashboard", section: EditorPropertySection.General)
        ]);
        var imageSource = GetNestedProperty(imageView.GetProperties(), "Source", "Source");
        Assert(imageSource is not null && imageSource.IsReadOnly &&
               string.Equals(imageSource.Description, sourceTypeReason, StringComparison.Ordinal),
            "A canonical image source editor with mixed Source Type context must remain visible but disabled.");

        var power = new C.PowerWidgetDefinition
        {
            Id = "multi-power",
            PowerSource = PowerMetricContract.UpsSource
        };
        var media = new C.MediaSystemWidgetDefinition
        {
            Id = "multi-media",
            MediaSource = MediaMetricContract.OutputSource
        };
        var differentSourceSemantics = new EditorMultiPropertyView([
            new EditorPropertyView(power, section: EditorPropertySection.General),
            new EditorPropertyView(media, section: EditorPropertySection.General)
        ]).GetProperties();
        Assert(differentSourceSemantics["Source"] is null,
            "Same display label with incompatible semantic keys must not create a false common Source group.");

        var firstBox = new MultiEditTestBox { Value = 1 };
        var secondBox = new MultiEditTestBox { Value = 2 };
        var atomic = new MultiSelectionPropertyDescriptor([
            new MultiEditTestPropertyDescriptor(firstBox, throwsOnSet: false),
            new MultiEditTestPropertyDescriptor(secondBox, throwsOnSet: true)
        ]);
        var threw = false;
        try
        {
            atomic.SetValue(null, 9);
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }
        Assert(threw, "Synthetic multi-edit failure fixture must throw.");
        Assert(firstBox.Value == 1 && secondBox.Value == 2,
            "A failed multi-selection write must roll back earlier targets instead of partially applying.");
    }

    private static void CanvasNormalizedPropertyHierarchy()
    {
        var canvas = new CanvasDefinition
        {
            Width = 1920,
            Height = 480,
            Orientation = 90,
            BackgroundColor = "#112233",
            BackgroundImage = new CanvasImageLayerDefinition
            {
                SourceType = StateVisualProfileContract.SourceFile,
                Source = @"images\background.png",
                Fit = StateVisualProfileContract.FitStretch
            },
            ForegroundImage = new CanvasImageLayerDefinition
            {
                SourceType = StateVisualProfileContract.SourceFile,
                Source = @"images\foreground.png",
                Fit = StateVisualProfileContract.FitStretch
            }
        };

        static string Semantic(PropertyDescriptor? descriptor) =>
            (descriptor as IPropertySemanticKeyDescriptor)?.SemanticKey ?? string.Empty;

        var general = CurrentPropertyView(canvas, section: EditorPropertySection.General).GetProperties();
        Assert(general.Count == 3 &&
               general["Background Image"] is PropertyGroupPropertyDescriptor &&
               general["Foreground Image"] is PropertyGroupPropertyDescriptor &&
               general["Geometry"] is PropertyGroupPropertyDescriptor,
            "Canvas General must expose Background Image, Foreground Image, and Geometry groups.");

        var width = GetNestedProperty(general, "Geometry", nameof(C.CanvasDefinition.Width));
        var height = GetNestedProperty(general, "Geometry", nameof(C.CanvasDefinition.Height));
        var orientation = GetNestedProperty(general, "Geometry", nameof(C.CanvasDefinition.Orientation));
        Assert(width is not null && height is not null && orientation is not null,
            "Canvas General -> Geometry must expose Width, Height, and Orientation.");
        Assert(Semantic(width) == "general.geometry.width" &&
               Semantic(height) == "general.geometry.height" &&
               Semantic(orientation) == "general.geometry.orientation",
            "Canvas geometry must use the canonical General/Geometry semantic identities.");

        foreach (var (group, role) in new[]
                 {
                     ("Background Image", "background_image"),
                     ("Foreground Image", "foreground_image")
                 })
        {
            var sourceType = GetNestedProperty(general, group, nameof(C.ImageAssetPresentationDefinition.SourceType));
            var source = GetNestedProperty(general, group, nameof(C.ImageAssetPresentationDefinition.Source));
            Assert(sourceType is { DisplayName: "Type" } && source is { DisplayName: "Source" },
                $"Canvas General -> {group} must expose canonical Type and Source asset properties.");
            Assert(Semantic(sourceType) == $"general.{role}.asset.kind" &&
                   Semantic(source) == $"general.{role}.asset.source",
                $"Canvas General -> {group} must use canonical nested asset semantic identities.");
            Assert(GetNestedProperty(general, group, nameof(C.ImageAssetPresentationDefinition.Fit)) is null &&
                   GetNestedProperty(general, group, nameof(C.CanvasImageLayerDefinition.Opacity)) is null &&
                   GetNestedProperty(general, group, nameof(C.ImageAssetPresentationDefinition.Loop)) is null &&
                   GetNestedProperty(general, group, nameof(C.CanvasImageLayerDefinition.Color)) is null,
                $"Canvas General -> {group} must contain source selection only, not image presentation.");
        }

        var appearance = CurrentPropertyView(canvas, section: EditorPropertySection.Appearance).GetProperties();
        Assert(appearance.Count == 1 && appearance["Background"] is PropertyGroupPropertyDescriptor,
            "Canvas Appearance must expose one Background group.");
        var color = GetNestedProperty(appearance, "Background", nameof(C.CanvasDefinition.BackgroundColor));
        Assert(color is not null && color.DisplayName == "Color" && Semantic(color) == "appearance.background.color",
            "Canvas Appearance -> Background must share the canonical Background/Color semantic identity.");

        var image = CurrentPropertyView(canvas, section: EditorPropertySection.Image).GetProperties();
        Assert(image.Count == 2 &&
               image["Background Image"] is PropertyGroupPropertyDescriptor &&
               image["Foreground Image"] is PropertyGroupPropertyDescriptor,
            "Canvas Image must expose Background Image and Foreground Image groups.");

        foreach (var (group, role) in new[]
                 {
                     ("Background Image", "background_image"),
                     ("Foreground Image", "foreground_image")
                 })
        {
            Assert(GetNestedProperty(image, group, nameof(C.ImageAssetPresentationDefinition.SourceType)) is null &&
                   GetNestedProperty(image, group, nameof(C.ImageAssetPresentationDefinition.Source)) is null,
                $"Canvas Image -> {group} must not duplicate Type or Source from General.");

            var fit = GetNestedProperty(image, group, nameof(C.ImageAssetPresentationDefinition.Fit));
            var opacity = GetNestedProperty(image, group, nameof(C.CanvasImageLayerDefinition.Opacity));
            var loop = GetNestedProperty(image, group, nameof(C.ImageAssetPresentationDefinition.Loop));
            var layerColor = GetNestedProperty(image, group, nameof(C.CanvasImageLayerDefinition.Color));
            Assert(fit is { DisplayName: "Fit" } && opacity is { DisplayName: "Opacity" } &&
                   loop is { DisplayName: "Loop" } && layerColor is { DisplayName: "Color" },
                $"Canvas Image -> {group} must expose canonical image presentation properties.");
            Assert(Semantic(fit) == $"image.{role}.asset.fit" &&
                   Semantic(opacity) == $"image.{role}.opacity" &&
                   Semantic(loop) == $"image.{role}.asset.loop" &&
                   Semantic(layerColor) == $"image.{role}.color",
                $"Canvas Image -> {group} must use canonical image-presentation semantic identities.");
        }

        var widget = new C.ValueWidgetDefinition { Id = "canvas-shared-semantic-parity" };
        var widgetGeneral = CurrentPropertyView(widget, section: EditorPropertySection.General).GetProperties();
        var widgetAppearance = CurrentPropertyView(widget, section: EditorPropertySection.Appearance).GetProperties();
        Assert(Semantic(GetNestedProperty(widgetGeneral, "Geometry", nameof(C.WidgetDefinition.Width))) == Semantic(width) &&
               Semantic(GetNestedProperty(widgetGeneral, "Geometry", nameof(C.WidgetDefinition.Height))) == Semantic(height) &&
               Semantic(GetNestedProperty(widgetAppearance, "Background", nameof(C.WidgetDefinition.BackgroundColor))) == Semantic(color),
            "Canvas and widgets must share semantic identities for common Geometry and Background properties.");

        foreach (var section in new[]
        {
            EditorPropertySection.Data,
            EditorPropertySection.Gauge,
            EditorPropertySection.States,
            EditorPropertySection.Text
        })
        {
            Assert(CurrentPropertyView(canvas, section: section).GetProperties().Count == 0,
                $"Canvas must not expose irrelevant {section} properties.");
        }

        var all = CurrentPropertyView(canvas, section: EditorPropertySection.All).GetProperties();
        Assert(FindLeafProperty(all, nameof(C.CanvasDefinition.Width)) is not null &&
               FindLeafProperty(all, nameof(C.CanvasDefinition.Height)) is not null &&
               FindLeafProperty(all, nameof(C.CanvasDefinition.Orientation)) is not null &&
               FindLeafProperty(all, nameof(C.CanvasDefinition.BackgroundColor)) is not null &&
               GetNestedProperty(all, "Background Image", nameof(C.ImageAssetPresentationDefinition.Source)) is not null &&
               GetNestedProperty(all, "Foreground Image", nameof(C.ImageAssetPresentationDefinition.Source)) is not null,
            "Canvas All view must expose persisted geometry, appearance, and both image-layer contracts through its canonical groups.");
        Assert(!all.Cast<PropertyDescriptor>()
                .SelectMany(FlattenProperties)
                .Any(property => property.Name.StartsWith("CanvasImage:", StringComparison.Ordinal)),
            "Canvas property views must not reintroduce synthetic CanvasImage:* descriptor identities.");
    }

    private static void CanvasImageSourcePresentationNormalization()
    {
        var canvas = new CanvasDefinition
        {
            BackgroundImage = new CanvasImageLayerDefinition
            {
                SourceType = StateVisualProfileContract.SourceIcon,
                Source = "lucide:cpu"
            },
            ForegroundImage = new CanvasImageLayerDefinition
            {
                SourceType = StateVisualProfileContract.SourceFile,
                Source = @"images\foreground.png"
            }
        };

        var sharedEditorRoot = Path.GetTempPath();
        var canvasGeneral = CurrentPropertyView(
            canvas,
            applicationRoot: sharedEditorRoot,
            dashboardDirectory: sharedEditorRoot,
            section: EditorPropertySection.General).GetProperties();
        var canvasImage = CurrentPropertyView(canvas, section: EditorPropertySection.Image).GetProperties();

        foreach (var group in new[] { "Background Image", "Foreground Image" })
        {
            foreach (var name in new[]
                     {
                         nameof(C.ImageAssetPresentationDefinition.SourceType),
                         nameof(C.ImageAssetPresentationDefinition.Source)
                     })
            {
                Assert(GetNestedProperty(canvasGeneral, group, name) is not null &&
                       GetNestedProperty(canvasImage, group, name) is null,
                    $"Canvas {group} {name} must belong exclusively to General.");
            }

            foreach (var name in new[]
                     {
                         nameof(C.ImageAssetPresentationDefinition.Fit),
                         nameof(C.CanvasImageLayerDefinition.Opacity),
                         nameof(C.ImageAssetPresentationDefinition.Loop),
                         nameof(C.CanvasImageLayerDefinition.Color)
                     })
            {
                Assert(GetNestedProperty(canvasGeneral, group, name) is null &&
                       GetNestedProperty(canvasImage, group, name) is not null,
                    $"Canvas {group} {name} must belong exclusively to Image.");
            }
        }

        var imageWidget = new WidgetDefinition
        {
            Type = WidgetTypeContract.Image,
            Id = "canvas-source-presentation-parity",
            SourceType = StateVisualProfileContract.SourceIcon,
            Source = "lucide:cpu",
            Width = 100f,
            Height = 100f
        };
        var widgetGeneral = CurrentPropertyView(
            imageWidget,
            applicationRoot: sharedEditorRoot,
            dashboardDirectory: sharedEditorRoot,
            section: EditorPropertySection.General).GetProperties();
        var widgetImage = CurrentPropertyView(imageWidget, section: EditorPropertySection.Image).GetProperties();

        var canvasSourceType = GetNestedProperty(canvasGeneral, "Background Image", nameof(C.ImageAssetPresentationDefinition.SourceType));
        var canvasSource = GetNestedProperty(canvasGeneral, "Background Image", nameof(C.ImageAssetPresentationDefinition.Source));
        var widgetSourceType = GetNestedProperty(widgetGeneral, "Source", nameof(C.ImageAssetPresentationDefinition.SourceType));
        var widgetSource = GetNestedProperty(widgetGeneral, "Source", nameof(C.ImageAssetPresentationDefinition.Source));
        Assert(canvasSourceType is not null && canvasSource is not null && widgetSourceType is not null && widgetSource is not null &&
               canvasSourceType.ComponentType == typeof(C.ImageAssetPresentationDefinition) &&
               canvasSource.ComponentType == typeof(C.ImageAssetPresentationDefinition) &&
               widgetSourceType.ComponentType == typeof(C.ImageAssetPresentationDefinition) &&
               widgetSource.ComponentType == typeof(C.ImageAssetPresentationDefinition),
            "Canvas and Image widget source selection must be owned by the shared canonical ImageAssetPresentationDefinition.");
        Assert(canvasSource.GetEditor(typeof(UITypeEditor)) is ImageAssetSourceEditor &&
               widgetSource.GetEditor(typeof(UITypeEditor)) is ImageAssetSourceEditor,
            "Canvas and Image widget file/icon selection must use the shared canonical image-asset source editor.");

        Assert(widgetSourceType is not null && widgetSource is not null,
            "Image widget source selection must remain in General/Source through the shared canonical image-asset descriptors.");
        Assert(GetNestedProperty(widgetImage, "Image", nameof(C.ImageAssetPresentationDefinition.Fit)) is not null &&
               GetNestedProperty(widgetImage, "Image", nameof(C.ImageAssetPresentationDefinition.Loop)) is not null &&
               GetNestedProperty(widgetImage, "Image", nameof(C.ImageWidgetDefinition.Opacity)) is not null,
            "Image widget presentation must remain in Image/Image.");
    }

    private static void CanvasSchema16GeometryMigration()
    {
        var root = JsonNode.Parse(
            """
            {
              "schemaVersion": 16,
              "canvas": {
                "width": 777,
                "height": 888,
                "orientation": 90,
                "background": "images\\background.png",
                "backgroundColor": "#112233",
                "foreground": "images\\foreground.png"
              },
              "widgets": []
            }
            """) as JsonObject ?? throw new InvalidOperationException("Could not create schema 16 Canvas migration document.");

        Assert(DashboardSchemaMigration.UpgradeToCurrent(root),
            "Schema 16 dashboard must migrate Canvas geometry and image layers to the current schema.");
        Assert(root["schemaVersion"]!.GetValue<int>() == DashboardSchemaMigration.PropertyModelNormalizationSchemaVersion,
            "Canvas migration must end at current schema 19.");

        var canvas = root["canvas"] as JsonObject
            ?? throw new InvalidOperationException("Migrated Canvas is missing.");
        Assert(canvas["width"]!.GetValue<int>() == FrameGeometry.DefaultNativeWidth &&
               canvas["height"]!.GetValue<int>() == FrameGeometry.DefaultNativeHeight,
            "Schema 16 -> 17 migration must materialize the historical 1920x480 native Canvas geometry.");
        Assert(canvas["orientation"]!.GetValue<int>() == 90 &&
               canvas["backgroundColor"]!.GetValue<string>() == "#112233" &&
               !canvas.ContainsKey("background") && !canvas.ContainsKey("foreground"),
            "Canvas migration must preserve orientation/background color and remove obsolete raw image paths.");

        AssertMigratedCanvasImageLayer(canvas, "backgroundImage", @"images\background.png");
        AssertMigratedCanvasImageLayer(canvas, "foregroundImage", @"images\foreground.png");
    }

    private static void CanvasSchema17ImageLayerMigration()
    {
        var root = JsonNode.Parse(
            """
            {
              "schemaVersion": 17,
              "canvas": {
                "width": 800,
                "height": 300,
                "orientation": 270,
                "background": "images\\background.png",
                "backgroundColor": "#010203",
                "foreground": null
              },
              "widgets": []
            }
            """) as JsonObject ?? throw new InvalidOperationException("Could not create schema 17 Canvas migration document.");

        Assert(DashboardSchemaMigration.UpgradeToCurrent(root),
            "Schema 17 dashboard must migrate raw Canvas paths through canonical schema 19.");
        Assert(root["schemaVersion"]!.GetValue<int>() == DashboardSchemaMigration.PropertyModelNormalizationSchemaVersion,
            "Schema 17 migration must end at current schema 19.");

        var canvas = root["canvas"] as JsonObject
            ?? throw new InvalidOperationException("Migrated Canvas is missing.");
        Assert(canvas["width"]!.GetValue<int>() == 800 &&
               canvas["height"]!.GetValue<int>() == 300 &&
               canvas["orientation"]!.GetValue<int>() == 270 &&
               canvas["backgroundColor"]!.GetValue<string>() == "#010203",
            "Schema 17 -> current migration must preserve configurable geometry and Canvas appearance.");
        AssertMigratedCanvasImageLayer(canvas, "backgroundImage", @"images\background.png");
        AssertMigratedCanvasImageLayer(canvas, "foregroundImage", null);
        Assert(!canvas.ContainsKey("background") && !canvas.ContainsKey("foreground"),
            "Schema 17 migration must remove obsolete raw Canvas image-path properties.");
    }

    private static void AssertMigratedCanvasImageLayer(JsonObject canvas, string name, string? expectedSource)
    {
        var layer = canvas[name] as JsonObject
            ?? throw new InvalidOperationException($"Migrated Canvas layer '{name}' is missing.");
        var asset = layer["asset"] as JsonObject
            ?? throw new InvalidOperationException($"Migrated Canvas layer '{name}' canonical Asset is missing.");
        Assert(asset["sourceType"]?.GetValue<string>() == C.ImageAssetSourceType.File &&
               ((expectedSource is null && asset["source"] is null) ||
                (expectedSource is not null && asset["source"]?.GetValue<string>() == expectedSource)) &&
               asset["fit"]?.GetValue<string>() == StateVisualProfileContract.FitStretch &&
               asset["loop"]?.GetValue<bool>() == true &&
               Math.Abs(layer["opacity"]!.GetValue<float>() - 1f) < 0.001f &&
               layer["color"]?.GetValue<string>() == "#FFFFFFFF",
            $"Migrated Canvas layer '{name}' did not preserve legacy stretch behavior with canonical image defaults.");
    }

    private static void AssertCanonicalMigratedStateProfile(
        JsonObject profiles,
        string key,
        string expectedFit)
    {
        var profile = profiles[key] as JsonObject
            ?? throw new InvalidOperationException($"Migrated state profile '{key}' is missing.");
        var asset = profile["asset"] as JsonObject
            ?? throw new InvalidOperationException($"Migrated state profile '{key}' Asset is missing.");
        Assert(profile["contentType"] is JsonValue &&
               asset["sourceType"] is JsonValue &&
               asset["fit"]?.GetValue<string>() == expectedFit &&
               asset["loop"]?.GetValue<bool>() == true &&
               profile["textPresentation"] is JsonObject,
            $"Migrated state profile '{key}' must use canonical ContentType/Asset/TextPresentation structure.");
    }
    private static void GlobalIconFormatDiscoveryAndCapabilities()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-global-icon-formats");
        try
        {
            var libraryRoot = WriteGlobalIconTestAssets(tempRoot, includeBroken: true);
            File.WriteAllText(Path.Combine(libraryRoot, "ignored.tiff"), "unsupported");

            var entries = GlobalAssetResolver.EnumerateIconsFromRoot(tempRoot)
                .Where(entry => string.Equals(entry.Library, "Mixed", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var logicalNames = entries.Select(entry => entry.LogicalName).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var expected in new[]
                     {
                         "mixed:vector",
                         "mixed:red.png",
                         "mixed:red.jpg",
                         "mixed:red.jpeg",
                         "mixed:red.bmp",
                         "mixed:red.gif",
                         "mixed:red.ico",
                         "mixed:red.webp",
                         "mixed:animated.gif",
                         "mixed:broken.png"
                     })
            {
                Assert(logicalNames.Contains(expected), $"Global icon library did not enumerate '{expected}'.");
            }

            Assert(!entries.Any(entry => entry.Path.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase)),
                "Unsupported icon extensions must not enter the global icon library.");
            Assert(entries.Single(entry => entry.LogicalName == "mixed:vector").Format == "SVG" &&
                   entries.Single(entry => entry.LogicalName == "mixed:red.webp").Format == "WEBP",
                "Global icon enumeration must report each asset format.");

            Assert(GlobalAssetResolver.ResolveIconFromRoot(tempRoot, "mixed:vector")
                       .EndsWith("vector.svg", StringComparison.OrdinalIgnoreCase),
                "Extensionless SVG logical identity must remain backward compatible.");
            Assert(GlobalAssetResolver.ResolveIconFromRoot(tempRoot, "mixed:red.png")
                       .EndsWith("red.png", StringComparison.OrdinalIgnoreCase),
                "Raster icon logical identity must resolve the explicit extension.");

            var ambiguous = false;
            try
            {
                _ = GlobalAssetResolver.ResolveIconFromRoot(tempRoot, "mixed:red");
            }
            catch (InvalidDataException ex)
            {
                ambiguous = ex.Message.Contains("ambiguous", StringComparison.OrdinalIgnoreCase);
            }
            Assert(ambiguous,
                "Extensionless raster identity must fail clearly when multiple formats share one stem.");

            var svgCapabilities = IconAssetLoader.ReadCapabilities(Path.Combine(libraryRoot, "vector.svg"));
            Assert(svgCapabilities.Tintable && !svgCapabilities.UsesIntrinsicColor &&
                   !svgCapabilities.IsAnimated && svgCapabilities.SupportsAlpha,
                "SVG global icons must advertise tintable static alpha-capable semantics.");

            var pngCapabilities = IconAssetLoader.ReadCapabilities(Path.Combine(libraryRoot, "red.png"));
            Assert(!pngCapabilities.Tintable && pngCapabilities.UsesIntrinsicColor && !pngCapabilities.IsAnimated,
                "Raster global icons must advertise intrinsic-color semantics.");

            var jpegCapabilities = IconAssetLoader.ReadCapabilities(Path.Combine(libraryRoot, "red.jpg"));
            Assert(!jpegCapabilities.SupportsAlpha,
                "JPEG global icons must report opaque/no-alpha semantics.");

            var animatedCapabilities = IconAssetLoader.ReadCapabilities(Path.Combine(libraryRoot, "animated.gif"));
            Assert(animatedCapabilities.IsAnimated && animatedCapabilities.HasFrameTiming,
                "Animated global icon assets must expose animation and frame-timing capabilities.");

            var corruptReported = false;
            try
            {
                _ = IconAssetLoader.ReadCapabilities(Path.Combine(libraryRoot, "broken.png"));
            }
            catch (InvalidDataException ex)
            {
                corruptReported = ex.Message.Contains("decode", StringComparison.OrdinalIgnoreCase) ||
                                  ex.Message.Contains("icon", StringComparison.OrdinalIgnoreCase);
            }
            Assert(corruptReported,
                "Corrupt supported icon files must fail with a clear asset/decode error.");
            Assert(IconAssetLoader.ReadCapabilities(Path.Combine(libraryRoot, "red.webp")).Format == "WebP",
                "A corrupt sibling icon must not poison the library capability scan/cache.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void GlobalIconWindowsSafeLogicalNames()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-global-icon-logical-names");
        try
        {
            var libraryName = "Pinkie Pie! (custom)+&";
            var fileName = "pinkie neutral! [01]+&.png";
            var libraryRoot = Path.Combine(tempRoot, "assets", "icons", libraryName);
            Directory.CreateDirectory(libraryRoot);
            var assetPath = Path.Combine(libraryRoot, fileName);
            WriteSolidTestImage(assetPath, SkiaSharp.SKColors.Magenta, width: 2, height: 2);

            var expectedLogicalName = $"{libraryName.ToLowerInvariant()}:{fileName}";
            var entry = GlobalAssetResolver.EnumerateIconsFromRoot(tempRoot)
                .SingleOrDefault(candidate =>
                    string.Equals(candidate.LogicalName, expectedLogicalName, StringComparison.Ordinal));
            Assert(entry is not null,
                "Global icon discovery must preserve ordinary Windows-valid punctuation and spaces in library/file names.");

            var resolved = GlobalAssetResolver.ResolveIconFromRoot(tempRoot, expectedLogicalName);
            Assert(string.Equals(Path.GetFullPath(resolved), Path.GetFullPath(assetPath), StringComparison.OrdinalIgnoreCase),
                "A discovered icon with Windows-valid punctuation must resolve through its logical library:name identity.");

            foreach (var invalidLogicalName in new[]
                     {
                         "bad/library:icon.png",
                         "bad\\library:icon.png",
                         "bad|library:icon.png",
                         "CON:icon.png",
                         "COM¹:icon.png",
                         "valid:NUL.png",
                         "valid:LPT².png",
                         "valid:icon?.png",
                         "valid:.."
                     })
            {
                var rejected = false;
                try
                {
                    _ = GlobalAssetResolver.ResolveIconFromRoot(tempRoot, invalidLogicalName);
                }
                catch (InvalidDataException)
                {
                    rejected = true;
                }

                Assert(rejected,
                    $"Unsafe or Win32-invalid logical icon name '{invalidLogicalName}' must be rejected before path resolution.");
            }
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void WindowsPathComponentValidationContract()
    {
        foreach (var valid in new[]
                 {
                     "Pinkie Pie! (custom)+&",
                     ".hidden",
                     "name.with.multiple.extensions.png"
                 })
        {
            Assert(WindowsPathComponentValidator.IsSafe(valid),
                $"Windows-valid path component '{valid}' must remain accepted.");
        }

        var invalid = new Dictionary<string, WindowsPathComponentValidationError>(StringComparer.Ordinal)
        {
            ["."] = WindowsPathComponentValidationError.DotSegment,
            [".."] = WindowsPathComponentValidationError.DotSegment,
            ["bad/name"] = WindowsPathComponentValidationError.InvalidCharacter,
            ["bad\\name"] = WindowsPathComponentValidationError.InvalidCharacter,
            ["bad."] = WindowsPathComponentValidationError.TrailingSpaceOrPeriod,
            ["bad "] = WindowsPathComponentValidationError.TrailingSpaceOrPeriod,
            ["NUL.txt"] = WindowsPathComponentValidationError.ReservedDeviceName,
            ["COM¹.log"] = WindowsPathComponentValidationError.ReservedDeviceName,
            ["LPT³"] = WindowsPathComponentValidationError.ReservedDeviceName
        };

        foreach (var pair in invalid)
        {
            var actual = WindowsPathComponentValidator.Validate(pair.Key);
            Assert(actual == pair.Value,
                $"Windows path component '{pair.Key}' must be rejected as {pair.Value}; got {actual}.");
        }
    }

    private static void GlobalIconRenderingAndApplicability()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-global-icon-rendering");
        try
        {
            _ = WriteGlobalIconTestAssets(tempRoot, includeBroken: false);
            var dashboardRoot = Path.Combine(tempRoot, "dashboards");
            using var icons = new IconAssetCache(dashboardRoot);

            SkiaSharp.SKColor RenderCenter(string logicalName, SkiaSharp.SKColor requestedColor)
            {
                using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(
                    16, 16, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul))
                    ?? throw new InvalidOperationException("Could not create global icon regression surface.");
                surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
                var icon = icons.Get(logicalName);
                icon.Draw(
                    surface.Canvas,
                    new SkiaSharp.SKRect(0, 0, 16, 16),
                    requestedColor,
                    1f,
                    "stretch",
                    loop: false,
                    antialias: false,
                    elapsedMs: 0);
                surface.Canvas.Flush();
                using var pixels = surface.PeekPixels()
                    ?? throw new InvalidOperationException("Could not inspect global icon regression pixels.");
                return pixels.GetPixelColor(8, 8);
            }

            var svg = RenderCenter("mixed:vector", SkiaSharp.SKColors.Lime);
            Assert(svg.Green > 220 && svg.Red < 40 && svg.Blue < 40,
                "Tintable SVG global icons must use the configured icon Color.");

            foreach (var logicalName in new[]
                     {
                         "mixed:red.png",
                         "mixed:red.jpg",
                         "mixed:red.jpeg",
                         "mixed:red.bmp",
                         "mixed:red.gif",
                         "mixed:red.ico",
                         "mixed:red.webp"
                     })
            {
                var rendered = RenderCenter(logicalName, SkiaSharp.SKColors.Lime);
                Assert(rendered.Red > 160 && rendered.Green < 100 && rendered.Blue < 100 && rendered.Alpha > 200,
                    $"Raster global icon '{logicalName}' did not render with its intrinsic red color.");
            }

            var image = new C.ImageWidgetDefinition
            {
                Id = "global-icon-image",
                Width = 32f,
                Height = 32f,
                Asset = new C.ImageAssetPresentationDefinition
                {
                    SourceType = C.ImageAssetSourceType.Icon,
                    Source = "mixed:vector",
                    Loop = true
                }
            };

            var svgAppearance = new EditorPropertyView(
                image, tempRoot, dashboardRoot, section: EditorPropertySection.Appearance).GetProperties();
            var svgColor = GetNestedProperty(svgAppearance, "Foreground", nameof(C.WidgetDefinition.Color));
            var svgImage = new EditorPropertyView(
                image, tempRoot, dashboardRoot, section: EditorPropertySection.Image).GetProperties();
            var svgLoop = GetNestedProperty(svgImage, "Image", nameof(C.ImageAssetPresentationDefinition.Loop));
            Assert(svgColor is { IsReadOnly: false } &&
                   svgLoop is { IsReadOnly: true, Description: "Not applicable for selected Source." },
                "Static tintable SVG icons must enable Color and disable Loop by asset capability.");

            image.Asset.Source = "mixed:red.png";
            var pngAppearance = new EditorPropertyView(
                image, tempRoot, dashboardRoot, section: EditorPropertySection.Appearance).GetProperties();
            var pngColor = GetNestedProperty(pngAppearance, "Foreground", nameof(C.WidgetDefinition.Color));
            var pngImage = new EditorPropertyView(
                image, tempRoot, dashboardRoot, section: EditorPropertySection.Image).GetProperties();
            var pngLoop = GetNestedProperty(pngImage, "Image", nameof(C.ImageAssetPresentationDefinition.Loop));
            Assert(pngColor is { IsReadOnly: true, Description: "Not applicable for selected Source." } &&
                   pngLoop is { IsReadOnly: true, Description: "Not applicable for selected Source." },
                "Static intrinsic-color raster icons must keep Color/Loop visible but disabled.");

            image.Asset.Source = "mixed:animated.gif";
            var gifAppearance = new EditorPropertyView(
                image, tempRoot, dashboardRoot, section: EditorPropertySection.Appearance).GetProperties();
            var gifColor = GetNestedProperty(gifAppearance, "Foreground", nameof(C.WidgetDefinition.Color));
            var gifImage = new EditorPropertyView(
                image, tempRoot, dashboardRoot, section: EditorPropertySection.Image).GetProperties();
            var gifLoop = GetNestedProperty(gifImage, "Image", nameof(C.ImageAssetPresentationDefinition.Loop));
            Assert(gifColor is { IsReadOnly: true } && gifLoop is { IsReadOnly: false },
                "Animated raster icons must keep intrinsic Color disabled while enabling Loop.");

            var binary = new C.BinaryWidgetDefinition
            {
                Id = "animated-global-icon-state",
                Metric = "test.global-icon.animation",
                X = 0,
                Y = 0,
                Width = 8,
                Height = 8
            };
            TextOverflowStateContract.EnsureProfiles(binary);
            var trueProfile = binary.Profiles[BinarySignalContract.TrueKey];
            trueProfile.ContentType = C.StateContentType.Image;
            trueProfile.Asset.SourceType = C.ImageAssetSourceType.Icon;
            trueProfile.Asset.Source = "mixed:animated.gif";
            trueProfile.Asset.Fit = StateVisualProfileContract.FitStretch;
            trueProfile.Asset.Loop = false;
            trueProfile.Opacity = 1f;

            var stateImage = new EditorPropertyView(
                binary, tempRoot, dashboardRoot, section: EditorPropertySection.Image).GetProperties();
            static string StateProperty(string key, StateVisualProfileField field) =>
                $"{StateVisualProfilePropertyDescriptor.PropertyPrefix}{key}:{field}";
            var trueLoop = GetNestedProperty(
                stateImage,
                "State: True",
                StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.Loop));
            var trueColor = GetNestedProperty(
                stateImage,
                "State: True",
                StateProperty(BinarySignalContract.TrueKey, StateVisualProfileField.Color));
            Assert(trueLoop is { IsReadOnly: false } &&
                   trueColor is { IsReadOnly: true, Description: "Not applicable for selected Source." },
                "Stateful animated raster icons must enable Loop and disable tint Color.");

            long nowMs = 10_000;
            using var images = new ImageAssetCache(dashboardRoot);
            icons.Preload(new C.WidgetDefinition[] { binary });
            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, new C.WidgetDefinition[] { binary });
            using var renderer = new PinkieSysMon.Widgets.BinaryWidgetRenderer(images, icons, () => nowMs);
            using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    [binary.Metric!] = true
                },
                antialias: false,
                typefaces);
            using var stateSurface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(8, 8))
                ?? throw new InvalidOperationException("Could not create animated icon state surface.");

            SkiaSharp.SKColor RenderStateCenter()
            {
                stateSurface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
                renderer.Render(stateSurface.Canvas, binary, context);
                stateSurface.Canvas.Flush();
                using var pixels = stateSurface.PeekPixels()
                    ?? throw new InvalidOperationException("Could not inspect animated icon state pixels.");
                return pixels.GetPixelColor(4, 4);
            }

            var first = RenderStateCenter();
            nowMs = 10_150;
            var second = RenderStateCenter();
            Assert(first.Red > 240 && first.Blue < 20 && second.Blue > 240 && second.Red < 20,
                "Stateful animated global icons must use activation-relative frame timing.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void CanvasImageLayerApplicability()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-canvas-image-applicability");
        try
        {
            _ = WriteGlobalIconTestAssets(tempRoot, includeBroken: false);
            var dashboardRoot = Path.Combine(tempRoot, "dashboards");
            var imagesRoot = Path.Combine(dashboardRoot, "images");
            Directory.CreateDirectory(imagesRoot);
            WriteSolidTestImage(Path.Combine(imagesRoot, "static.png"), SkiaSharp.SKColors.Red, 2, 2);
            File.WriteAllBytes(
                Path.Combine(imagesRoot, "animated.gif"),
                Convert.FromBase64String("R0lGODlhAgACAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQICgAAACwAAAAAAgACAAAIBgABCAQQEAAh+QQICgAAACwAAAAAAgACAIEAAP8AAAAAAAAAAAAIBgABCAQQEAA7"));

            var canvas = new CanvasDefinition
            {
                BackgroundImage = new CanvasImageLayerDefinition
                {
                    SourceType = StateVisualProfileContract.SourceFile,
                    Source = @"images\static.png"
                }
            };

            PropertyDescriptorCollection ImageProperties() => CurrentPropertyView(
                canvas,
                applicationRoot: tempRoot,
                dashboardDirectory: dashboardRoot,
                section: EditorPropertySection.Image).GetProperties();

            var fileProperties = ImageProperties();
            Assert(GetNestedProperty(fileProperties, "Background Image", nameof(C.ImageAssetPresentationDefinition.Fit)) is { IsReadOnly: false } &&
                   GetNestedProperty(fileProperties, "Background Image", nameof(C.CanvasImageLayerDefinition.Opacity)) is { IsReadOnly: false } &&
                   GetNestedProperty(fileProperties, "Background Image", nameof(C.ImageAssetPresentationDefinition.Loop)) is { IsReadOnly: true, Description: "Not applicable for selected Source." } &&
                   GetNestedProperty(fileProperties, "Background Image", nameof(C.CanvasImageLayerDefinition.Color)) is { IsReadOnly: true, Description: "Not applicable for selected Type." },
                "Static file Canvas layers must keep Fit/Opacity active while disabling unsupported Loop/Color rows.");

            canvas.BackgroundImage.Source = @"images\animated.gif";
            var animatedFileProperties = ImageProperties();
            Assert(GetNestedProperty(animatedFileProperties, "Background Image", nameof(C.ImageAssetPresentationDefinition.Loop)) is { IsReadOnly: false },
                "Animated file Canvas layers must enable Loop by asset capability.");

            canvas.BackgroundImage.SourceType = StateVisualProfileContract.SourceIcon;
            canvas.BackgroundImage.Source = "mixed:vector";
            var svgProperties = ImageProperties();
            Assert(GetNestedProperty(svgProperties, "Background Image", nameof(C.CanvasImageLayerDefinition.Color)) is { IsReadOnly: false } &&
                   GetNestedProperty(svgProperties, "Background Image", nameof(C.ImageAssetPresentationDefinition.Loop)) is { IsReadOnly: true },
                "Tintable static SVG Canvas layers must enable Color and disable Loop.");

            canvas.BackgroundImage.Source = "mixed:animated.gif";
            var animatedIconProperties = ImageProperties();
            Assert(GetNestedProperty(animatedIconProperties, "Background Image", nameof(C.CanvasImageLayerDefinition.Color)) is { IsReadOnly: true } &&
                   GetNestedProperty(animatedIconProperties, "Background Image", nameof(C.ImageAssetPresentationDefinition.Loop)) is { IsReadOnly: false },
                "Animated raster icon Canvas layers must preserve intrinsic color and enable Loop.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void CanvasImageLayerRenderOrder()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-canvas-image-render-order");
        try
        {
            var dashboardRoot = Path.Combine(tempRoot, "dashboards");
            var imagesRoot = Path.Combine(dashboardRoot, "images");
            Directory.CreateDirectory(imagesRoot);
            WriteSolidTestImage(Path.Combine(imagesRoot, "background.png"), SkiaSharp.SKColors.Red, 4, 4);
            WriteSolidTestImage(Path.Combine(imagesRoot, "widget.png"), SkiaSharp.SKColors.Lime, 4, 4);
            WriteSolidTestImage(Path.Combine(imagesRoot, "foreground.png"), SkiaSharp.SKColors.Blue, 4, 4);

            var dashboardPath = Path.Combine(dashboardRoot, "canvas-layers.json");
            var definition = DashboardDefinition.Parse(
                $$"""
                {
                  "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                  "canvas": {
                    "width": 40,
                    "height": 20,
                    "orientation": 0,
                    "backgroundColor": "#FF000000",
                    "backgroundImage": {
                      "sourceType": "file",
                      "source": "images\\background.png",
                      "fit": "stretch",
                      "opacity": 1.0,
                      "loop": true,
                      "color": "#FFFFFFFF"
                    },
                    "foregroundImage": {
                      "sourceType": "file",
                      "source": "images\\foreground.png",
                      "fit": "stretch",
                      "opacity": 0.5,
                      "loop": true,
                      "color": "#FFFFFFFF"
                    }
                  },
                  "widgets": [
                    {
                      "type": "image",
                      "id": "middle",
                      "x": 0,
                      "y": 0,
                      "width": 20,
                      "height": 20,
                      "sourceType": "file",
                      "source": "images\\widget.png",
                      "fit": "stretch",
                      "opacity": 1.0,
                      "loop": true
                    }
                  ]
                }
                """,
                dashboardPath);

            using var preview = new DashboardPreviewRenderer(PropertyModelNormalizationMigration.MapLegacyDefinition(definition));
            using var bitmap = SkiaSharp.SKBitmap.Decode(preview.RenderToPng(new Dictionary<string, object?>()));
            var overWidget = bitmap.GetPixel(10, 10);
            var overBackground = bitmap.GetPixel(30, 10);
            Assert(overWidget.Green > 110 && overWidget.Blue > 110 && overWidget.Red < 30,
                "Canvas foreground image must render above widgets with configured opacity.");
            Assert(overBackground.Red > 110 && overBackground.Blue > 110 && overBackground.Green < 30,
                "Canvas background image must render below widgets while foreground remains above all content.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void CanvasImageLayerFitModes()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-canvas-image-fit");
        try
        {
            var dashboardRoot = Path.Combine(tempRoot, "dashboards");
            var imagesRoot = Path.Combine(dashboardRoot, "images");
            Directory.CreateDirectory(imagesRoot);
            WriteVerticalBandTestImage(Path.Combine(imagesRoot, "bands.png"));
            var dashboardPath = Path.Combine(dashboardRoot, "canvas-fit.json");

            SkiaSharp.SKBitmap Render(string fit)
            {
                var definition = DashboardDefinition.Parse(
                    $$"""
                    {
                      "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                      "canvas": {
                        "width": 40,
                        "height": 20,
                        "orientation": 0,
                        "backgroundColor": "#000000",
                        "backgroundImage": {
                          "sourceType": "file",
                          "source": "images\\bands.png",
                          "fit": "{{fit}}",
                          "opacity": 1.0,
                          "loop": true,
                          "color": "#FFFFFFFF"
                        },
                        "foregroundImage": {
                          "sourceType": "file",
                          "source": null,
                          "fit": "stretch",
                          "opacity": 1.0,
                          "loop": true,
                          "color": "#FFFFFFFF"
                        }
                      },
                      "widgets": []
                    }
                    """,
                    dashboardPath);
                using var preview = new DashboardPreviewRenderer(PropertyModelNormalizationMigration.MapLegacyDefinition(definition));
                return SkiaSharp.SKBitmap.Decode(preview.RenderToPng(new Dictionary<string, object?>()));
            }

            using var contain = Render(StateVisualProfileContract.FitContain);
            using var cover = Render(StateVisualProfileContract.FitCover);
            using var stretch = Render(StateVisualProfileContract.FitStretch);

            Assert(contain.GetPixel(2, 10) == SkiaSharp.SKColors.Black &&
                   contain.GetPixel(20, 10).Green > 220,
                "Canvas contain fit must preserve aspect ratio and letterbox a mismatched image.");
            Assert(cover.GetPixel(2, 10).Green > 220,
                "Canvas cover fit must preserve aspect ratio while filling and clipping the Canvas.");
            Assert(stretch.GetPixel(20, 1).Red > 220 && stretch.GetPixel(20, 18).Blue > 220,
                "Canvas stretch fit must map the complete source image across the Canvas surface.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void CanvasGlobalIconImageLayerRendering()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-canvas-icon-rendering");
        try
        {
            _ = WriteGlobalIconTestAssets(tempRoot, includeBroken: false);
            var dashboardRoot = Path.Combine(tempRoot, "dashboards");
            var dashboardPath = Path.Combine(dashboardRoot, "canvas-icon.json");
            var definition = DashboardDefinition.Parse(
                $$"""
                {
                  "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                  "canvas": {
                    "width": 20,
                    "height": 20,
                    "orientation": 0,
                    "backgroundColor": "#000000",
                    "backgroundImage": {
                      "sourceType": "icon",
                      "source": "mixed:vector",
                      "fit": "stretch",
                      "opacity": 1.0,
                      "loop": true,
                      "color": "#FF00FF00"
                    },
                    "foregroundImage": {
                      "sourceType": "file",
                      "source": null,
                      "fit": "stretch",
                      "opacity": 1.0,
                      "loop": true,
                      "color": "#FFFFFFFF"
                    }
                  },
                  "widgets": []
                }
                """,
                dashboardPath);

            using var preview = new DashboardPreviewRenderer(PropertyModelNormalizationMigration.MapLegacyDefinition(definition));
            using var bitmap = SkiaSharp.SKBitmap.Decode(preview.RenderToPng(new Dictionary<string, object?>()));
            var center = bitmap.GetPixel(10, 10);
            Assert(center.Green > 220 && center.Red < 40 && center.Blue < 40,
                "Canvas image layers must render tintable global icons through the shared icon asset pipeline.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void CanvasAnimatedImageLayerRefresh()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-canvas-image-animation");
        try
        {
            var dashboardRoot = Path.Combine(tempRoot, "dashboards");
            var imagesRoot = Path.Combine(dashboardRoot, "images");
            Directory.CreateDirectory(imagesRoot);
            File.WriteAllBytes(
                Path.Combine(imagesRoot, "animated.gif"),
                Convert.FromBase64String("R0lGODlhAgACAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQICgAAACwAAAAAAgACAAAIBgABCAQQEAAh+QQICgAAACwAAAAAAgACAIEAAP8AAAAAAAAAAAAIBgABCAQQEAA7"));

            var dashboardPath = Path.Combine(dashboardRoot, "canvas-animation.json");
            var definition = DashboardDefinition.Parse(
                $$"""
                {
                  "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                  "canvas": {
                    "width": 20,
                    "height": 20,
                    "orientation": 0,
                    "backgroundColor": "#000000",
                    "backgroundImage": {
                      "sourceType": "file",
                      "source": "images\\animated.gif",
                      "fit": "stretch",
                      "opacity": 1.0,
                      "loop": true,
                      "color": "#FFFFFFFF"
                    },
                    "foregroundImage": {
                      "sourceType": "file",
                      "source": null,
                      "fit": "stretch",
                      "opacity": 1.0,
                      "loop": true,
                      "color": "#FFFFFFFF"
                    }
                  },
                  "widgets": []
                }
                """,
                dashboardPath);

            using var preview = new DashboardPreviewRenderer(PropertyModelNormalizationMigration.MapLegacyDefinition(definition));
            Assert(preview.RequiresFrequentRefresh && preview.RecommendedRefreshIntervalMs == 250,
                "Animated Canvas image layers must participate in the standard preview animation refresh cadence.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void SharedImageVisualPreservesFileFallback()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-image-visual-file-fallback");
        try
        {
            var dashboardRoot = Path.Combine(tempRoot, "dashboards");
            var imagesRoot = Path.Combine(dashboardRoot, "images");
            Directory.CreateDirectory(imagesRoot);
            WriteSolidTestImage(Path.Combine(imagesRoot, "red.png"), SkiaSharp.SKColors.Red, 2, 2);
            var dashboardPath = Path.Combine(dashboardRoot, "image-fallback.json");
            var definition = DashboardDefinition.Parse(
                $$"""
                {
                  "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                  "canvas": {
                    "width": 20,
                    "height": 20,
                    "orientation": 0,
                    "backgroundColor": "#000000"
                  },
                  "widgets": [
                    {
                      "type": "image",
                      "id": "legacy-file-fallback",
                      "x": 0,
                      "y": 0,
                      "width": 20,
                      "height": 20,
                      "sourceType": "",
                      "source": "images\\red.png",
                      "fit": "stretch",
                      "opacity": 1,
                      "loop": true
                    }
                  ]
                }
                """,
                dashboardPath);

            using var preview = new DashboardPreviewRenderer(PropertyModelNormalizationMigration.MapLegacyDefinition(definition));
            using var bitmap = SkiaSharp.SKBitmap.Decode(preview.RenderToPng(new Dictionary<string, object?>()));
            var center = bitmap.GetPixel(10, 10);
            Assert(center.Red > 220 && center.Green < 40 && center.Blue < 40,
                "Shared ImageVisual must preserve the Image-widget contract where blank SourceType falls back to file.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void CanvasCurrentSchemaRejectsLegacyPaths()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-canvas-current-schema-contract");
        try
        {
            var rejected = false;
            try
            {
                _ = DashboardDefinition.Parse(
                    $$"""
                    {
                      "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                      "canvas": {
                        "width": 64,
                        "height": 32,
                        "orientation": 0,
                        "backgroundColor": "#000000",
                        "background": null,
                        "foreground": null
                      },
                      "widgets": []
                    }
                    """,
                    Path.Combine(tempRoot, "dashboards", "legacy-current.json"));
            }
            catch (InvalidDataException ex)
            {
                rejected = ex.Message.Contains("obsolete Background/Foreground", StringComparison.Ordinal);
            }

            Assert(rejected,
                "Current Canvas schema must reject obsolete raw Background/Foreground path properties.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void CanvasOrientationRenderMatrix()
    {
        const int nativeWidth = 40;
        const int nativeHeight = 24;

        foreach (var orientation in new[] { 0, 90, 180, 270 })
        {
            var logical = FrameGeometry.GetLogicalSize(nativeWidth, nativeHeight, orientation);
            using var logicalSurface = SkiaSharp.SKSurface.Create(
                new SkiaSharp.SKImageInfo(logical.Width, logical.Height, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul))
                ?? throw new InvalidOperationException("Could not create logical Canvas orientation test surface.");
            using (var paint = new SkiaSharp.SKPaint { IsAntialias = false, Style = SkiaSharp.SKPaintStyle.Fill })
            {
                var halfWidth = logical.Width / 2f;
                var halfHeight = logical.Height / 2f;

                paint.Color = SkiaSharp.SKColors.Red;
                logicalSurface.Canvas.DrawRect(new SkiaSharp.SKRect(0, 0, halfWidth, halfHeight), paint);
                paint.Color = SkiaSharp.SKColors.Lime;
                logicalSurface.Canvas.DrawRect(new SkiaSharp.SKRect(halfWidth, 0, logical.Width, halfHeight), paint);
                paint.Color = SkiaSharp.SKColors.Blue;
                logicalSurface.Canvas.DrawRect(new SkiaSharp.SKRect(0, halfHeight, halfWidth, logical.Height), paint);
                paint.Color = SkiaSharp.SKColors.Yellow;
                logicalSurface.Canvas.DrawRect(new SkiaSharp.SKRect(halfWidth, halfHeight, logical.Width, logical.Height), paint);
            }
            logicalSurface.Canvas.Flush();

            using var wireSurface = SkiaSharp.SKSurface.Create(
                new SkiaSharp.SKImageInfo(nativeWidth, nativeHeight, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul))
                ?? throw new InvalidOperationException("Could not create wire Canvas orientation test surface.");
            wireSurface.Canvas.Clear(SkiaSharp.SKColors.Black);
            FrameGeometry.DrawLogicalToWire(
                wireSurface.Canvas,
                logicalSurface,
                logical.Width,
                logical.Height,
                nativeWidth,
                nativeHeight,
                orientation,
                wireRotationDegrees: 0);
            wireSurface.Canvas.Flush();

            using var wireImage = wireSurface.Snapshot();
            using var png = wireImage.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100)
                ?? throw new InvalidOperationException("Could not encode Canvas orientation test frame.");
            using var bitmap = SkiaSharp.SKBitmap.Decode(png.ToArray())
                ?? throw new InvalidOperationException("Could not decode Canvas orientation test frame.");

            var cornerClasses = new HashSet<string>(StringComparer.Ordinal)
            {
                ClassifyCorner(bitmap.GetPixel(3, 3)),
                ClassifyCorner(bitmap.GetPixel(nativeWidth - 4, 3)),
                ClassifyCorner(bitmap.GetPixel(3, nativeHeight - 4)),
                ClassifyCorner(bitmap.GetPixel(nativeWidth - 4, nativeHeight - 4))
            };

            Assert(cornerClasses.SetEquals(new[] { "R", "G", "B", "Y" }),
                $"Canvas orientation {orientation} did not map the complete logical surface onto configured native geometry.");
        }

        static string ClassifyCorner(SkiaSharp.SKColor color)
        {
            if (color.Red > 200 && color.Green < 80 && color.Blue < 80)
                return "R";
            if (color.Green > 180 && color.Red < 80 && color.Blue < 80)
                return "G";
            if (color.Blue > 180 && color.Red < 80 && color.Green < 80)
                return "B";
            if (color.Red > 180 && color.Green > 180 && color.Blue < 80)
                return "Y";
            return "?";
        }
    }

    private static void CanvasRuntimeEditorImageLayerParity()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-canvas-runtime-editor-parity");
        try
        {
            var dashboardRoot = Path.Combine(tempRoot, "dashboards");
            var imagesRoot = Path.Combine(dashboardRoot, "images");
            Directory.CreateDirectory(imagesRoot);
            WriteSolidTestImage(Path.Combine(imagesRoot, "background.png"), SkiaSharp.SKColors.Red, 8, 8);
            WriteSolidTestImage(Path.Combine(imagesRoot, "widget.png"), SkiaSharp.SKColors.Lime, 8, 8);
            WriteSolidTestImage(Path.Combine(imagesRoot, "foreground.png"), SkiaSharp.SKColors.Blue, 8, 8);

            var dashboardPath = Path.Combine(dashboardRoot, "canvas-parity.json");
            var definition = DashboardDefinition.Parse(
                $$"""
                {
                  "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                  "canvas": {
                    "width": 80,
                    "height": 40,
                    "orientation": 0,
                    "backgroundColor": "#000000",
                    "backgroundImage": {
                      "sourceType": "file",
                      "source": "images\\background.png",
                      "fit": "stretch",
                      "opacity": 1.0,
                      "loop": true,
                      "color": "#FFFFFFFF"
                    },
                    "foregroundImage": {
                      "sourceType": "file",
                      "source": "images\\foreground.png",
                      "fit": "stretch",
                      "opacity": 0.5,
                      "loop": true,
                      "color": "#FFFFFFFF"
                    }
                  },
                  "widgets": [
                    {
                      "type": "image",
                      "id": "center",
                      "x": 20,
                      "y": 10,
                      "width": 40,
                      "height": 20,
                      "sourceType": "file",
                      "source": "images\\widget.png",
                      "fit": "stretch",
                      "opacity": 1.0,
                      "loop": true
                    }
                  ]
                }
                """,
                dashboardPath);

            using var preview = new DashboardPreviewRenderer(PropertyModelNormalizationMigration.MapLegacyDefinition(definition));
            using var previewBitmap = SkiaSharp.SKBitmap.Decode(
                preview.RenderToPng(new Dictionary<string, object?>()))
                ?? throw new InvalidOperationException("Could not decode Canvas preview parity frame.");

            using var renderer = new DashboardRenderer(CurrentDashboard(definition));
            using var frame = renderer.RenderJpeg(
                new Dictionary<string, object?>(),
                wireRotationDegrees: 0,
                jpegQuality: 100);
            using var runtimeBitmap = SkiaSharp.SKBitmap.Decode(frame.EncodedData)
                ?? throw new InvalidOperationException("Could not decode Canvas runtime parity frame.");

            Assert(previewBitmap.Width == runtimeBitmap.Width && previewBitmap.Height == runtimeBitmap.Height,
                "Runtime and Editor must use the same configured Canvas dimensions at orientation 0.");

            AssertColorsNear(previewBitmap.GetPixel(5, 5), runtimeBitmap.GetPixel(5, 5), 35,
                "Runtime and Editor background/foreground Canvas composition diverged.");
            AssertColorsNear(previewBitmap.GetPixel(40, 20), runtimeBitmap.GetPixel(40, 20), 35,
                "Runtime and Editor widget/foreground Canvas composition diverged.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }

        static void AssertColorsNear(
            SkiaSharp.SKColor expected,
            SkiaSharp.SKColor actual,
            int tolerance,
            string message)
        {
            Assert(Math.Abs(expected.Red - actual.Red) <= tolerance &&
                   Math.Abs(expected.Green - actual.Green) <= tolerance &&
                   Math.Abs(expected.Blue - actual.Blue) <= tolerance,
                $"{message} Preview={expected}; Runtime={actual}.");
        }
    }

    private static void CanvasLargeLogicalGeometryHitTesting()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-canvas-large-logical-hit-test");
        try
        {
            var dashboardRoot = Path.Combine(tempRoot, "dashboards");
            var imagesRoot = Path.Combine(dashboardRoot, "images");
            Directory.CreateDirectory(imagesRoot);
            WriteSolidTestImage(Path.Combine(imagesRoot, "marker.png"), SkiaSharp.SKColors.White, 4, 4);
            var dashboardPath = Path.Combine(dashboardRoot, "large-logical.json");
            var definition = DashboardDefinition.Parse(
                $$"""
                {
                  "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                  "canvas": {
                    "width": 2300,
                    "height": 640,
                    "orientation": 90,
                    "backgroundColor": "#000000"
                  },
                  "widgets": [
                    {
                      "type": "image",
                      "id": "far-edge",
                      "x": 100,
                      "y": 2100,
                      "width": 120,
                      "height": 60,
                      "sourceType": "file",
                      "source": "images\\marker.png",
                      "fit": "stretch",
                      "opacity": 1.0,
                      "loop": true
                    }
                  ]
                }
                """,
                dashboardPath);

            var canonicalDefinition = PropertyModelNormalizationMigration.MapLegacyDefinition(definition);
            var widget = canonicalDefinition.Widgets.Single();
            using var preview = new DashboardPreviewRenderer(canonicalDefinition);
            Assert(preview.Width == 640 && preview.Height == 2300,
                "Configured 90-degree Canvas geometry must expose the complete large logical surface.");
            Assert(preview.HitTestWidget(widget, new Dictionary<string, object?>(), 110, 2110),
                "Editor hit testing must use configured logical Canvas coordinates beyond the historical 1920-pixel extent.");
            Assert(!preview.HitTestWidget(widget, new Dictionary<string, object?>(), 20, 20),
                "Editor hit testing must not alias far-edge geometry onto historical Canvas coordinates.");

            var outline = preview.GetWidgetOutline(widget, new Dictionary<string, object?>());
            Assert(outline.Max(point => point.Y) > 2100f,
                "Editor selection geometry must preserve coordinates across the complete configured logical Canvas.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void ConfigurableCanvasGeometry()
    {
        var landscape = FrameGeometry.GetLogicalSize(800, 300, 0);
        var upsideDown = FrameGeometry.GetLogicalSize(800, 300, 180);
        var clockwise = FrameGeometry.GetLogicalSize(800, 300, 90);
        var counterClockwise = FrameGeometry.GetLogicalSize(800, 300, 270);

        Assert(landscape.Width == 800 && landscape.Height == 300 &&
               upsideDown.Width == 800 && upsideDown.Height == 300,
            "Canvas 0/180 orientation must retain configured native Width x Height.");
        Assert(clockwise.Width == 300 && clockwise.Height == 800 &&
               counterClockwise.Width == 300 && counterClockwise.Height == 800,
            "Canvas 90/270 orientation must swap configured native Width/Height.");

        var invalidWidthRejected = false;
        try
        {
            _ = FrameGeometry.GetLogicalSize(0, 300, 0);
        }
        catch (ArgumentOutOfRangeException)
        {
            invalidWidthRejected = true;
        }
        Assert(invalidWidthRejected, "Canvas Width must reject non-positive values.");

        var invalidHeightRejected = false;
        try
        {
            _ = FrameGeometry.GetLogicalSize(800, -1, 0);
        }
        catch (ArgumentOutOfRangeException)
        {
            invalidHeightRejected = true;
        }
        Assert(invalidHeightRejected, "Canvas Height must reject non-positive values.");
    }

    private static void ConfigurableCanvasRenderSurfaces()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-canvas-geometry-regression");
        try
        {
            var dashboardPath = Path.Combine(tempRoot, "dashboards", "canvas-geometry.json");
            var definition = DashboardDefinition.Parse(
                $$"""
                {
                  "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                  "canvas": {
                    "width": 320,
                    "height": 120,
                    "orientation": 90,
                    "backgroundColor": "#FFFF0000"
                  },
                  "widgets": []
                }
                """,
                dashboardPath);

            using (var preview = new DashboardPreviewRenderer(PropertyModelNormalizationMigration.MapLegacyDefinition(definition)))
            {
                Assert(preview.Width == 120 && preview.Height == 320,
                    $"Editor preview must use logical Canvas geometry; got {preview.Width}x{preview.Height}.");

                using var previewBitmap = SkiaSharp.SKBitmap.Decode(
                    preview.RenderToPng(new Dictionary<string, object?>()));
                Assert(previewBitmap.Width == 120 && previewBitmap.Height == 320,
                    "Editor snapshot dimensions must follow the configured logical Canvas size.");
                var previewCenter = previewBitmap.GetPixel(previewBitmap.Width / 2, previewBitmap.Height / 2);
                Assert(previewCenter.Red > 240 && previewCenter.Green < 16 && previewCenter.Blue < 16,
                    "Editor preview did not preserve Canvas content through configurable orientation geometry.");
            }

            using (var renderer = new DashboardRenderer(CurrentDashboard(definition)))
            {
                using var frame = renderer.RenderJpeg(new Dictionary<string, object?>(), wireRotationDegrees: 0, jpegQuality: 92);
                using var wireBitmap = SkiaSharp.SKBitmap.Decode(frame.EncodedData);
                Assert(wireBitmap.Width == 320 && wireBitmap.Height == 120,
                    $"Runtime wire frame must use configured native Canvas geometry; got {wireBitmap.Width}x{wireBitmap.Height}.");
                var wireCenter = wireBitmap.GetPixel(wireBitmap.Width / 2, wireBitmap.Height / 2);
                Assert(wireCenter.Red > 220 && wireCenter.Green < 32 && wireCenter.Blue < 32,
                    "Runtime logical-to-wire transform did not preserve Canvas content for configurable geometry.");
            }
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void AllWidgetPropertiesAssignedToExplicitTabs()
    {
        var endpoints = new[]
        {
            new WindowsMediaTelemetrySource.MediaEndpointInfo("coverage-endpoint", "Coverage Endpoint")
        };
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);

        void Check(
            C.WidgetDefinition widget,
            IReadOnlyList<WindowsMediaTelemetrySource.MediaEndpointInfo>? mediaEndpoints = null,
            IDictionary<string, string>? endpointTypeOverrides = null)
        {
            if (widget is C.StateVisualWidgetDefinition stateWidget)
                TextOverflowStateContract.EnsureProfiles(stateWidget);

            var all = new EditorPropertyView(
                widget,
                mediaEndpoints: mediaEndpoints,
                endpointTypeOverrides: endpointTypeOverrides,
                section: EditorPropertySection.All).GetProperties();

            var expected = new HashSet<string>(StringComparer.Ordinal);
            CollectLeafSemanticKeys(all, expected);

            var assigned = new HashSet<string>(StringComparer.Ordinal);
            foreach (var section in new[]
                     {
                         EditorPropertySection.General,
                         EditorPropertySection.Appearance,
                         EditorPropertySection.Data,
                         EditorPropertySection.Gauge,
                         EditorPropertySection.States,
                         EditorPropertySection.Image,
                         EditorPropertySection.Text
                     })
            {
                var properties = new EditorPropertyView(
                    widget,
                    mediaEndpoints: mediaEndpoints,
                    endpointTypeOverrides: endpointTypeOverrides,
                    section: section).GetProperties();
                CollectLeafSemanticKeys(properties, assigned);
            }

            var missing = expected.Except(assigned, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
            Assert(missing.Length == 0,
                $"{widget.Type} has canonical property semantics not assigned to an explicit tab: {string.Join(", ", missing)}.");
        }

        Check(new C.ValueWidgetDefinition
        {
            Id = "coverage-value-metric",
            SourceKind = C.ValueSourceKind.Metric,
            Metric = SystemMetricContract.Uptime,
            Width = 120f,
            TextPresentation = new C.TextPresentationDefinition { OverflowMode = ValueOverflowContract.Bump }
        });

        Check(new C.ValueWidgetDefinition
        {
            Id = "coverage-value-text",
            SourceKind = C.ValueSourceKind.Text,
            Text = "123",
            SourceUnit = "seconds",
            Width = 120f,
            TextPresentation = new C.TextPresentationDefinition { OverflowMode = ValueOverflowContract.Scroll }
        });

        var barFill = new C.BarWidgetDefinition
        {
            Id = "coverage-bar-fill",
            Metric = RuntimeMetricContract.Fps,
            ContentMode = BarImageContract.ContentModeFill,
            Width = 160f,
            Height = 24f
        };
        foreach (var threshold in barFill.Thresholds.Items)
            threshold.Enabled = true;
        Check(barFill);

        Check(new C.BarWidgetDefinition
        {
            Id = "coverage-bar-image",
            Metric = RuntimeMetricContract.Fps,
            ContentMode = BarImageContract.ContentModeImage,
            Width = 160f,
            Height = 24f,
            Image = new C.BarImagePresentationDefinition { Source = "coverage.png" }
        });

        var gauge = new C.GaugeWidgetDefinition
        {
            Id = "coverage-gauge",
            Metric = RuntimeMetricContract.Fps,
            Width = 120f,
            Height = 120f
        };
        foreach (var threshold in gauge.Thresholds.Items)
            threshold.Enabled = true;
        Check(gauge);

        Check(new C.ImageWidgetDefinition
        {
            Id = "coverage-image-file",
            Width = 120f,
            Height = 120f,
            Asset = new C.ImageAssetPresentationDefinition { SourceType = C.ImageAssetSourceType.File, Source = "coverage.png" }
        });
        Check(new C.ImageWidgetDefinition
        {
            Id = "coverage-image-icon",
            Width = 120f,
            Height = 120f,
            Asset = new C.ImageAssetPresentationDefinition { SourceType = C.ImageAssetSourceType.Icon, Source = "activity" }
        });

        var binary = new C.BinaryWidgetDefinition
        {
            Id = "coverage-binary",
            Metric = SystemMetricContract.Uptime,
            EvaluationMode = BinarySignalContract.EvaluationModeSetpoint,
            Width = 140f,
            Height = 40f
        };
        TextOverflowStateContract.EnsureProfiles(binary);
        binary.Profiles[BinarySignalContract.TrueKey].ContentType = C.StateContentType.Value;
        binary.Profiles[BinarySignalContract.TrueKey].TextPresentation.OverflowMode = ValueOverflowContract.Bump;
        binary.Profiles[BinarySignalContract.FalseKey].ContentType = C.StateContentType.Image;
        binary.Profiles[BinarySignalContract.FalseKey].Asset.SourceType = C.ImageAssetSourceType.File;
        binary.Profiles[BinarySignalContract.FalseKey].Asset.Source = "coverage.gif";
        Check(binary);

        var binaryIcon = new C.BinaryWidgetDefinition
        {
            Id = "coverage-binary-icon",
            Metric = RuntimeMetricContract.Fps,
            Width = 140f,
            Height = 40f
        };
        TextOverflowStateContract.EnsureProfiles(binaryIcon);
        binaryIcon.Profiles[BinarySignalContract.TrueKey].ContentType = C.StateContentType.Value;
        binaryIcon.Profiles[BinarySignalContract.FalseKey].ContentType = C.StateContentType.Image;
        binaryIcon.Profiles[BinarySignalContract.FalseKey].Asset.SourceType = C.ImageAssetSourceType.Icon;
        binaryIcon.Profiles[BinarySignalContract.FalseKey].Asset.Source = "activity";
        Check(binaryIcon);

        var power = new C.PowerWidgetDefinition
        {
            Id = "coverage-power",
            PowerSource = PowerMetricContract.UpsSource,
            Width = 80f,
            Height = 40f
        };
        ConfigureCoverageStateProfiles(power);
        Check(power);

        var mediaPlayer = new C.MediaPlayerWidgetDefinition
        {
            Id = "coverage-media-player",
            Width = 80f,
            Height = 40f
        };
        ConfigureCoverageStateProfiles(mediaPlayer);
        Check(mediaPlayer);

        var mediaSystem = new C.MediaSystemWidgetDefinition
        {
            Id = "coverage-media-system",
            MediaSource = MediaMetricContract.OutputSource,
            Width = 80f,
            Height = 40f
        };
        ConfigureCoverageStateProfiles(mediaSystem);
        Check(mediaSystem, endpoints, overrides);

        var canvas = new C.CanvasDefinition();
        var canvasProperties = new EditorPropertyView(canvas, section: EditorPropertySection.All).GetProperties();
        var canvasKeys = new HashSet<string>(StringComparer.Ordinal);
        CollectLeafSemanticKeys(canvasProperties, canvasKeys);
        foreach (var required in new[]
                 {
                     "general.geometry.width", "general.geometry.height", "general.geometry.orientation", "appearance.background.color",
                     "general.background_image.asset.kind", "general.background_image.asset.source",
                     "general.foreground_image.asset.kind", "general.foreground_image.asset.source",
                     "image.background_image.asset.fit", "image.background_image.opacity",
                     "image.background_image.asset.loop", "image.background_image.color",
                     "image.foreground_image.asset.fit", "image.foreground_image.opacity",
                     "image.foreground_image.asset.loop", "image.foreground_image.color"
                 })
        {
            Assert(canvasKeys.Contains(required), $"Canvas canonical property view is missing semantic key '{required}'.");
        }

        static void ConfigureCoverageStateProfiles(C.StateVisualWidgetDefinition widget)
        {
            TextOverflowStateContract.EnsureProfiles(widget);
            var specs = StateVisualProfileContract.GetSpecs(widget.Type);
            Assert(specs.Count >= 3, $"{widget.Type} must provide at least three states for coverage.");

            var value = widget.Profiles[specs[0].Key];
            value.ContentType = C.StateContentType.Value;
            value.TextPresentation.OverflowMode = ValueOverflowContract.Bump;

            var file = widget.Profiles[specs[1].Key];
            file.ContentType = C.StateContentType.Image;
            file.Asset.SourceType = C.ImageAssetSourceType.File;
            file.Asset.Source = "coverage.gif";

            var icon = widget.Profiles[specs[2].Key];
            icon.ContentType = C.StateContentType.Image;
            icon.Asset.SourceType = C.ImageAssetSourceType.Icon;
            icon.Asset.Source = "activity";
        }
    }

    private static void PropertyExpansionStatePersistence()
    {
        var state = new EditorPropertyExpansionState();

        var general = state.Get(EditorPropertySection.General);
        Assert(general.TryGetValue("Identity", out var identityExpanded) && !identityExpanded &&
               general.TryGetValue("Source", out var sourceExpanded) && sourceExpanded &&
               general.TryGetValue("Background Image", out var backgroundImageSourceExpanded) && backgroundImageSourceExpanded &&
               general.TryGetValue("Foreground Image", out var foregroundImageSourceExpanded) && foregroundImageSourceExpanded &&
               general.TryGetValue("Geometry", out var geometryExpanded) && geometryExpanded,
            "General expansion state must initialize widget and Canvas source groups consistently.");

        var appearance = state.Get(EditorPropertySection.Appearance);
        Assert(!appearance.ContainsKey("Alignment") &&
               appearance.TryGetValue("Foreground", out var foregroundExpanded) && foregroundExpanded &&
               appearance.TryGetValue("Background", out var backgroundExpanded) && backgroundExpanded &&
               appearance.TryGetValue("Border", out var borderExpanded) && borderExpanded &&
               appearance.TryGetValue("Shadow", out var shadowExpanded) && !shadowExpanded &&
               appearance.TryGetValue("Threshold 1", out var threshold1Expanded) && !threshold1Expanded &&
               appearance.TryGetValue("Threshold 2", out var threshold2Expanded) && !threshold2Expanded &&
               appearance.TryGetValue("Threshold 3", out var threshold3Expanded) && !threshold3Expanded,
            "Appearance expansion state must start from the UI defaults with contextual groups initialized.");

        state.Remember(EditorPropertySection.Appearance, new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["Shadow"] = true,
            ["Threshold 1"] = true
        });
        state.Remember(EditorPropertySection.Appearance, new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["Background"] = false
        });

        appearance = state.Get(EditorPropertySection.Appearance);
        Assert(appearance["Shadow"] && !appearance["Background"] && appearance["Border"] &&
               appearance["Foreground"] && appearance["Threshold 1"] &&
               !appearance["Threshold 2"] && !appearance["Threshold 3"],
            "Remembering one visible Appearance group must preserve the last state of the other groups.");

        var data = state.Get(EditorPropertySection.Data);
        Assert(data["Value"] && data["Display"] && data["Range"] && data["Evaluation"],
            "Data expansion state must start with all structural data groups open.");

        var gauge = state.Get(EditorPropertySection.Gauge);
        Assert(gauge["Horseshoe"] && gauge["Needle"] && gauge["Bar"],
            "Gauge expansion state must start with all structural groups open.");
        state.Remember(EditorPropertySection.Gauge, new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["Needle"] = false
        });
        Assert(state.Get(EditorPropertySection.Gauge)["Horseshoe"] &&
               !state.Get(EditorPropertySection.Gauge)["Needle"],
            "Gauge group expansion state must use the centralized persistence model.");


        var states = state.Get(EditorPropertySection.States);
        Assert(states.Count == 0,
            "States expansion defaults must remain unspecified until a default is explicitly chosen.");
        state.Remember(EditorPropertySection.States, new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["State: True"] = true,
            ["State: False"] = false
        });
        Assert(state.Get(EditorPropertySection.States)["State: True"] &&
               !state.Get(EditorPropertySection.States)["State: False"],
            "States group expansion state must use the centralized persistence model.");

        var image = state.Get(EditorPropertySection.Image);
        Assert(image.TryGetValue("Image", out var imageExpanded) && imageExpanded &&
               image.TryGetValue("Background Image", out var backgroundImageExpanded) && backgroundImageExpanded &&
               image.TryGetValue("Foreground Image", out var foregroundImageExpanded) && foregroundImageExpanded,
            "Image expansion state must start with widget and Canvas image groups open.");

        var text = state.Get(EditorPropertySection.Text);
        Assert(text["Font"] && text["Alignment"] && !text["Outline"] && !text["Overflow"],
            "Text expansion state must start from the UI defaults.");
        state.Remember(EditorPropertySection.Text, new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["Overflow"] = true
        });
        Assert(state.Get(EditorPropertySection.Text)["Overflow"],
            "Overflow expansion state must survive a PropertyGrid rebuild.");
        Assert(state.Get(EditorPropertySection.Appearance)["Shadow"],
            "Expansion state must be isolated by property tab.");
    }

    private static void EditorInputFocusOwnership()
    {
        var state = new EditorInputFocusState();
        state.Claim(EditorInputSurface.Canvas);
        var canvasSnapshot = state.Capture();
        Assert(state.CanRestore(canvasSnapshot),
            "The current canvas focus snapshot must be restorable after a programmatic PropertyGrid refresh.");

        state.Claim(EditorInputSurface.Properties);
        Assert(!state.CanRestore(canvasSnapshot),
            "A later explicit Properties focus claim must invalidate a pending canvas focus restore.");

        var propertySnapshot = state.Capture();
        Assert(state.CanRestore(propertySnapshot),
            "The current Properties focus snapshot must be restorable.");

        state.Claim(EditorInputSurface.Layers);
        Assert(!state.CanRestore(propertySnapshot) && state.Surface == EditorInputSurface.Layers,
            "A later Layers focus claim must own keyboard input and invalidate older focus restores.");

        var transitionSnapshot = state.CaptureForPreservation(EditorInputSurface.Properties);
        Assert(transitionSnapshot.Surface == EditorInputSurface.Layers && state.Surface == EditorInputSurface.Layers,
            "Lagging native Properties focus must not overwrite an explicit Layers pointer-activation claim.");

        var unclaimed = new EditorInputFocusState();
        var seededSnapshot = unclaimed.CaptureForPreservation(EditorInputSurface.Properties);
        Assert(seededSnapshot.Surface == EditorInputSurface.Properties && unclaimed.Surface == EditorInputSurface.Properties,
            "Actual focus may seed input ownership only when no editor surface has claimed ownership yet.");
    }

    private static void LayerTreePointerFocusAndInactiveSelectionContract()
    {
        using var tree = new LayerTreeView();
        Assert(tree.HideSelection,
            "LayerTreeView must hide the native inactive-selection overlay so logical TreeNode selection colors remain visible outside Layers focus.");
        Assert(tree.FullRowSelect,
            "LayerTreeView must preserve full-row native selection while Layers owns focus.");
        Assert(LayerTreeView.IsPointerActivationMessage(0x0201) &&
               LayerTreeView.IsPointerActivationMessage(0x0204) &&
               !LayerTreeView.IsPointerActivationMessage(0x000F),
            "LayerTreeView must recognize left/right pointer activation before native selection processing without confusing paint messages for input.");
    }

    private static void EditorGeometryWholePixelNormalization()
    {
        var bar = new C.BarWidgetDefinition
        {
            Id = "whole-pixel-canonical-bar",
            X = 10.25f,
            Y = -20.6f,
            Width = 100.25f,
            Height = 38.40625f
        };
        var properties = new EditorPropertyView(bar, section: EditorPropertySection.General).GetProperties();
        var x = GetNestedProperty(properties, "Geometry", nameof(C.WidgetDefinition.X))
            ?? throw new InvalidOperationException("Bar X property is missing.");
        var y = GetNestedProperty(properties, "Geometry", nameof(C.WidgetDefinition.Y))
            ?? throw new InvalidOperationException("Bar Y property is missing.");
        var width = GetNestedProperty(properties, "Geometry", nameof(C.WidgetDefinition.Width))
            ?? throw new InvalidOperationException("Bar Width property is missing.");
        var height = GetNestedProperty(properties, "Geometry", nameof(C.WidgetDefinition.Height))
            ?? throw new InvalidOperationException("Bar Height property is missing.");

        Assert(Convert.ToSingle(x.GetValue(null)) == 10f &&
               Convert.ToSingle(y.GetValue(null)) == -21f &&
               Convert.ToSingle(width.GetValue(null)) == 100f &&
               Convert.ToSingle(height.GetValue(null)) == 38f,
            "Canonical Property Editor geometry did not preserve the whole-logical-pixel display contract.");

        x.SetValue(null, 11.5f);
        y.SetValue(null, -12.5f);
        width.SetValue(null, 121.6f);
        height.SetValue(null, 44.5f);
        Assert(bar.X == 12f && bar.Y == -13f && bar.Width == 122f && bar.Height == 45f,
            $"Canonical manual geometry edit was not normalized to whole logical pixels: X={bar.X}, Y={bar.Y}, {bar.Width} x {bar.Height}.");

        var value = new C.ValueWidgetDefinition
        {
            Id = "whole-pixel-canonical-value",
            SourceKind = C.ValueSourceKind.Text,
            Text = "TEST",
            Width = 100f,
            Height = 0f
        };
        var valueProperties = new EditorPropertyView(
            value,
            effectiveSizeAccessor: _ => (100.25f, 167.57812f),
            section: EditorPropertySection.General).GetProperties();
        var derivedHeight = GetNestedProperty(valueProperties, "Geometry", nameof(C.WidgetDefinition.Height))
            ?? throw new InvalidOperationException("Canonical Value Height property is missing.");
        Assert(derivedHeight.IsReadOnly && Convert.ToSingle(derivedHeight.GetValue(null)) == 168f,
            "Canonical derived text/value Height must remain read-only and display a whole logical pixel value.");

        Assert(WidgetResizeGeometry.NormalizeLogicalCoordinate(18.6f) == 19f &&
               WidgetResizeGeometry.NormalizeLogicalCoordinate(-18.6f) == -19f,
            "Logical coordinate normalization is inconsistent with the editor whole-pixel contract.");
        Assert(WidgetResizeGeometry.NormalizeResizedDimension(38.40625f) == 38f &&
               WidgetResizeGeometry.NormalizeResizedDimension(0.4f) == WidgetResizeGeometry.MinimumDimension,
            "Manual resize dimension normalization is inconsistent with the editor whole-pixel contract.");
    }

    private static void LayerTreeViewportAnchorPersistence()
    {
        var originalTop = new TreeNode("Original top");
        var state = LayerTreeViewportState.Capture(originalTop, _ => "w:viewport-anchor");
        Assert(state.TopNodeKey == "w:viewport-anchor",
            "Layer tree viewport capture did not retain the top visible node key.");

        var rebuiltTop = new TreeNode("Rebuilt top");
        var resolved = state.Resolve(key => key == "w:viewport-anchor" ? rebuiltTop : null);
        Assert(ReferenceEquals(resolved, rebuiltTop),
            "Layer tree viewport anchor did not resolve to the rebuilt node instance.");

        var missing = state.Resolve(_ => null);
        Assert(missing is null,
            "Layer tree viewport restore must tolerate an anchor that no longer exists.");
    }

    private static void GridSplitButtonBehavior()
    {
        using var button = new CheckableToolStripSplitButton();
        button.DropDownItems.Add(new ToolStripMenuItem("10 x 10") { Tag = 10 });
        button.DropDownItems.Add(new ToolStripMenuItem("25 x 25") { Tag = 25 });

        var mainButtonClicks = 0;
        button.ButtonClick += (_, _) => mainButtonClicks++;

        button.DropDownItems[1].PerformClick();
        Assert(mainButtonClicks == 0,
            "Grid spacing selection unexpectedly triggered the grid main-button action.");

        button.PerformButtonClick();
        Assert(mainButtonClicks == 1,
            "ToolStripSplitButton main area did not raise ButtonClick exactly once.");
        Assert(button.DropDownItems.Count == 2,
            "Grid split-button lost its independent spacing drop-down items.");

        button.Checked = true;
        Assert(button.Checked,
            "Grid split-button did not retain its persistent enabled-state presentation flag.");
        button.Checked = false;
        Assert(!button.Checked,
            "Grid split-button did not clear its enabled-state presentation flag.");
    }

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
                        { "type": "bar", "id": "bar-missing-height", "width": 20, "metric": "system.runtime.fps", "min": 0, "max": 100, "color": "#FFFFFFFF" }
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
                        { "type": "bar", "id": "bar-obsolete-diameter", "width": 20, "height": 20, "diameter": 20, "metric": "system.runtime.fps", "min": 0, "max": 100, "color": "#FFFFFFFF" }
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
                Metric = RuntimeMetricContract.Fps,
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

    private static void QuantitativeForegroundColorSchema()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-quantitative-foreground-schema-regression");
        try
        {
            var dashboardPath = Path.Combine(tempRoot, "dashboards", "quantitative-foreground-schema.json");

            var obsoleteFillColorRejected = false;
            try
            {
                _ = DashboardDefinition.Parse(
                    $$"""
                    {
                      "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                      "canvas": { "width": 1920, "height": 480, "orientation": 0, "backgroundColor": "#000000" },
                      "widgets": [
                        {
                          "type": "gauge",
                          "id": "gauge-obsolete-fill",
                          "width": 100,
                          "height": 100,
                          "metric": "{{RuntimeMetricContract.Fps}}",
                          "min": 0,
                          "max": 100,
                          "fillColor": "#FF0000FF"
                        }
                      ]
                    }
                    """,
                    dashboardPath);
            }
            catch (InvalidDataException)
            {
                obsoleteFillColorRejected = true;
            }
            Assert(obsoleteFillColorRejected,
                "Current dashboard schema must reject Gauge FillColor; Gauge base indicator color is Foreground/Color.");

            var definition = DashboardDefinition.Parse(
                $$"""
                {
                  "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                  "canvas": { "width": 1920, "height": 480, "orientation": 0, "backgroundColor": "#000000" },
                  "widgets": [
                    {
                      "type": "gauge",
                      "id": "gauge-foreground-color",
                      "width": 100,
                      "height": 100,
                      "metric": "{{RuntimeMetricContract.Fps}}",
                      "min": 0,
                      "max": 100,
                      "color": "#FF123456"
                    }
                  ]
                }
                """,
                dashboardPath);
            Assert(definition.Widgets.Count == 1 &&
                   string.Equals(definition.Widgets[0].Color, "#FF123456", StringComparison.Ordinal),
                "Current Gauge schema must accept universal Color as the base indicator color.");

            var obsoleteBarFillColorRejected = false;
            try
            {
                _ = DashboardDefinition.Parse(
                    $$"""
                    {
                      "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                      "canvas": { "width": 1920, "height": 480, "orientation": 0, "backgroundColor": "#000000" },
                      "widgets": [
                        {
                          "type": "bar",
                          "id": "bar-obsolete-fill",
                          "width": 100,
                          "height": 20,
                          "metric": "{{RuntimeMetricContract.Fps}}",
                          "min": 0,
                          "max": 100,
                          "fillColor": "#FF00FF00"
                        }
                      ]
                    }
                    """,
                    dashboardPath);
            }
            catch (InvalidDataException)
            {
                obsoleteBarFillColorRejected = true;
            }
            Assert(obsoleteBarFillColorRejected,
                "Current dashboard schema must reject Bar FillColor; Fill-mode Bar base indicator color is Foreground/Color.");

            var barDefinition = DashboardDefinition.Parse(
                $$"""
                {
                  "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                  "canvas": { "width": 1920, "height": 480, "orientation": 0, "backgroundColor": "#000000" },
                  "widgets": [
                    {
                      "type": "bar",
                      "id": "bar-foreground-color",
                      "width": 100,
                      "height": 20,
                      "metric": "{{RuntimeMetricContract.Fps}}",
                      "min": 0,
                      "max": 100,
                      "color": "#FF654321"
                    }
                  ]
                }
                """,
                dashboardPath);
            Assert(barDefinition.Widgets.Count == 1 &&
                   string.Equals(barDefinition.Widgets[0].Color, "#FF654321", StringComparison.Ordinal),
                "Current Fill-mode Bar schema must accept universal Color as the base indicator color.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void BarForegroundColorRendering()
    {
        const string metric = RuntimeMetricContract.Fps;
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-bar-foreground-rendering-regression");
        try
        {
            var widget = new WidgetDefinition
            {
                Id = "bar-foreground-rendering",
                Type = WidgetTypeContract.Bar,
                Width = 40,
                Height = 10,
                Metric = metric,
                Min = 0,
                Max = 100,
                BarContentMode = BarImageContract.ContentModeFill,
                Color = "#FF123456"
            };

            using var images = new ImageAssetCache(tempRoot);
            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, new[] { CurrentWidget(widget) });
            using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = 50d },
                antialias: false,
                typefaces);
            using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(40, 10))
                ?? throw new InvalidOperationException("Could not create Bar foreground rendering regression surface.");
            using var renderer = new PinkieSysMon.Widgets.BarWidgetRenderer(images);

            surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
            PinkieSysMon.Widgets.WidgetTransform.Render(surface.Canvas, CurrentWidget(widget), context, renderer);
            surface.Canvas.Flush();

            using var pixels = surface.PeekPixels()
                ?? throw new InvalidOperationException("Could not inspect Bar foreground rendering regression pixels.");
            var sample = pixels.GetPixelColor(5, 5);
            Assert(sample.Red == 0x12 && sample.Green == 0x34 && sample.Blue == 0x56 && sample.Alpha == 0xFF,
                "Fill-mode Bar must render its base indicator from universal Color.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void RemovedGraphWidgetType()
    {
        Assert(!WidgetTypeContract.KnownTypes.Contains("graph", StringComparer.OrdinalIgnoreCase),
            "Removed Graph widget type must not remain in the runtime type contract.");
        Assert(!WidgetTypeContract.EditorAddableTypes.Contains("graph", StringComparer.OrdinalIgnoreCase),
            "Removed Graph widget type must not remain addable in the editor.");

        var widgetProperties = TypeDescriptor.GetProperties(typeof(WidgetDefinition));
        Assert(widgetProperties["History"] is null &&
               widgetProperties["SampleIntervalMs"] is null &&
               widgetProperties["StrokeWidth"] is null,
            "Graph-only WidgetDefinition properties must be removed from the model.");
    }

    private static void BarImagePropertyVisibility()
    {
        var widget = new C.BarWidgetDefinition
        {
            Id = "bar-image-properties",
            Width = 100f,
            Height = 20f,
            Metric = RuntimeMetricContract.Fps,
            Min = 0,
            Max = 100,
            ContentMode = BarImageContract.ContentModeFill,
            Image = new C.BarImagePresentationDefinition
            {
                Source = "bar.png",
                Fit = BarImageContract.ImageFitContain,
                ProgressMode = BarImageContract.ProgressModeReveal,
                Loop = true
            }
        };

        var fillAppearance = new EditorPropertyView(widget, section: EditorPropertySection.Appearance).GetProperties();
        var fillImage = new EditorPropertyView(widget, section: EditorPropertySection.Image).GetProperties();
        Assert(GetNestedProperty(fillAppearance, "Foreground", nameof(C.WidgetDefinition.Color)) is not null,
            "Fill-mode Bar must expose universal Color.");
        Assert(fillAppearance["ThresholdMode"] is not null &&
               fillAppearance["Threshold 1"] is not null,
            "Fill-mode Bar must expose threshold controls.");
        Assert(fillImage.Count == 0,
            "Fill-mode Bar must hide image-specific properties while retaining dormant image configuration.");

        widget.ContentMode = BarImageContract.ContentModeImage;
        var imageAppearance = new EditorPropertyView(widget, section: EditorPropertySection.Appearance).GetProperties();
        var imageProperties = new EditorPropertyView(widget, section: EditorPropertySection.Image).GetProperties();
        Assert(GetNestedProperty(imageProperties, "Image", "BarImageSource") is not null,
            "Image-mode Bar must expose Image Source.");
        Assert(GetNestedProperty(imageProperties, "Image", "BarImageFit") is not null,
            "Image-mode Bar must expose Image Fit.");
        Assert(GetNestedProperty(imageProperties, "Image", "BarProgressMode") is not null,
            "Image-mode Bar must expose Progress Mode.");
        Assert(GetNestedProperty(imageProperties, "Image", "Loop") is not null,
            "Image-mode Bar must expose Loop because animated image playback uses the canonical image Loop setting.");
        Assert(imageAppearance["Foreground"] is null,
            "Image-mode Bar must hide universal foreground Color.");
        Assert(imageAppearance["ThresholdMode"] is null &&
               imageAppearance["Threshold 1"] is null &&
               imageAppearance["Threshold 2"] is null &&
               imageAppearance["Threshold 3"] is null,
            "Image-mode Bar must hide inactive threshold controls.");
        Assert(widget.Image.Source == "bar.png" &&
               widget.Image.Fit == BarImageContract.ImageFitContain &&
               widget.Image.ProgressMode == BarImageContract.ProgressModeReveal &&
               widget.Image.Loop,
            "Switching Bar content mode must preserve dormant canonical image configuration.");
    }

    private static void BarImageProgressModes()
    {
        const string metric = RuntimeMetricContract.Fps;
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-bar-progress-regression");
        try
        {
            var source = "bar-progress.png";
            WriteHorizontalQuarterTestImage(Path.Combine(tempRoot, source));

            SkiaSharp.SKColor RenderSample(string progressMode, bool reverse, int x)
            {
                var widget = new WidgetDefinition
                {
                    Id = $"bar-{progressMode}-{reverse}",
                    Type = WidgetTypeContract.Bar,
                    X = 0,
                    Y = 0,
                    Width = 40,
                    Height = 10,
                    Metric = metric,
                    Min = 0,
                    Max = 100,
                    BarContentMode = BarImageContract.ContentModeImage,
                    BarImageSource = source,
                    BarImageFit = BarImageContract.ImageFitStretch,
                    BarProgressMode = progressMode,
                    Reverse = reverse
                };

                using var images = new ImageAssetCache(tempRoot);
                images.Preload(new[] { CurrentWidget(widget) });
                using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, new[] { CurrentWidget(widget) });
                using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = 25d },
                    antialias: false,
                    typefaces);
                using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(40, 10))
                    ?? throw new InvalidOperationException("Could not create Bar image progress regression surface.");
                using var renderer = new PinkieSysMon.Widgets.BarWidgetRenderer(images);

                surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
                PinkieSysMon.Widgets.WidgetTransform.Render(surface.Canvas, CurrentWidget(widget), context, renderer);
                surface.Canvas.Flush();

                using var pixels = surface.PeekPixels()
                    ?? throw new InvalidOperationException("Could not inspect Bar image progress regression pixels.");
                return pixels.GetPixelColor(x, 5);
            }

            var reveal = RenderSample(BarImageContract.ProgressModeReveal, reverse: false, x: 5);
            Assert(reveal.Red > 240 && reveal.Green < 20 && reveal.Blue < 20,
                "Reveal mode must expose the leading quarter of the prepared image.");

            var slide = RenderSample(BarImageContract.ProgressModeSlide, reverse: false, x: 5);
            Assert(slide.Red > 240 && slide.Green > 240 && slide.Blue < 20,
                "Slide mode must bring the trailing quarter in from the start edge.");

            var scaleStart = RenderSample(BarImageContract.ProgressModeScale, reverse: false, x: 1);
            var scaleEnd = RenderSample(BarImageContract.ProgressModeScale, reverse: false, x: 8);
            Assert(scaleStart.Red > 200 && scaleStart.Green < 80 && scaleStart.Blue < 80,
                "Scale mode did not preserve the start of the full prepared image.");
            Assert(scaleEnd.Red > 180 && scaleEnd.Green > 180 && scaleEnd.Blue < 100,
                "Scale mode did not compress the end of the full prepared image into the active range.");

            var reverseReveal = RenderSample(BarImageContract.ProgressModeReveal, reverse: true, x: 35);
            Assert(reverseReveal.Red > 240 && reverseReveal.Green > 240 && reverseReveal.Blue < 20,
                "Reverse Reveal must expose the right-hand quarter of the prepared image.");

            var reverseSlide = RenderSample(BarImageContract.ProgressModeSlide, reverse: true, x: 35);
            Assert(reverseSlide.Red > 240 && reverseSlide.Green < 20 && reverseSlide.Blue < 20,
                "Reverse Slide must bring the left-hand quarter in from the right edge.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void BarImageFitModes()
    {
        const string metric = RuntimeMetricContract.Fps;
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-bar-fit-regression");
        try
        {
            var source = "bar-fit.png";
            WriteVerticalBandTestImage(Path.Combine(tempRoot, source));

            SkiaSharp.SKColor RenderSample(string fit, int x, int y)
            {
                var widget = new WidgetDefinition
                {
                    Id = $"bar-fit-{fit}",
                    Type = WidgetTypeContract.Bar,
                    X = 0,
                    Y = 0,
                    Width = 40,
                    Height = 20,
                    Metric = metric,
                    Min = 0,
                    Max = 100,
                    BarContentMode = BarImageContract.ContentModeImage,
                    BarImageSource = source,
                    BarImageFit = fit,
                    BarProgressMode = BarImageContract.ProgressModeReveal
                };

                using var images = new ImageAssetCache(tempRoot);
                images.Preload(new[] { CurrentWidget(widget) });
                using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, new[] { CurrentWidget(widget) });
                using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = 100d },
                    antialias: false,
                    typefaces);
                using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(40, 20))
                    ?? throw new InvalidOperationException("Could not create Bar image fit regression surface.");
                using var renderer = new PinkieSysMon.Widgets.BarWidgetRenderer(images);

                surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
                PinkieSysMon.Widgets.WidgetTransform.Render(surface.Canvas, CurrentWidget(widget), context, renderer);
                surface.Canvas.Flush();

                using var pixels = surface.PeekPixels()
                    ?? throw new InvalidOperationException("Could not inspect Bar image fit regression pixels.");
                return pixels.GetPixelColor(x, y);
            }

            var clipOutside = RenderSample(BarImageContract.ImageFitClip, 5, 10);
            var clipCenter = RenderSample(BarImageContract.ImageFitClip, 20, 10);
            Assert(clipOutside.Alpha == 0 && clipCenter.Alpha == 255,
                "Clip fit must center the natural-size image without scaling.");

            var containOutside = RenderSample(BarImageContract.ImageFitContain, 5, 10);
            var containInside = RenderSample(BarImageContract.ImageFitContain, 12, 10);
            Assert(containOutside.Alpha == 0 && containInside.Alpha == 255,
                "Contain fit must preserve aspect ratio and leave horizontal letterboxing.");

            var coverTop = RenderSample(BarImageContract.ImageFitCover, 20, 2);
            Assert(coverTop.Green > 200 && coverTop.Red < 80 && coverTop.Blue < 80,
                "Cover fit did not crop the vertically oversized scaled image as expected.");

            var stretchTop = RenderSample(BarImageContract.ImageFitStretch, 20, 2);
            Assert(stretchTop.Red > 200 && stretchTop.Green < 80 && stretchTop.Blue < 80,
                "Stretch fit did not map the full source height into the Bar height.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void GaugeUsesUniversalContainerEffects()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-gauge-container-regression");
        try
        {
            var widget = new WidgetDefinition
            {
                Id = "gauge-container",
                Type = WidgetTypeContract.Gauge,
                X = 10,
                Y = 10,
                Width = 40,
                Height = 40,
                Metric = RuntimeMetricContract.Fps,
                Min = 0,
                Max = 100,
                TrackEnabled = false,
                NeedleEnabled = false,
                BackgroundColor = "#FF102030",
                BorderColor = "#FFFFFFFF",
                BorderWidth = 2f
            };

            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, new[] { CurrentWidget(widget) });
            using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
                antialias: false,
                typefaces);
            using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(80, 80))
                ?? throw new InvalidOperationException("Could not create gauge container regression surface.");
            using var renderer = new PinkieSysMon.Widgets.GaugeWidgetRenderer();

            surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
            PinkieSysMon.Widgets.WidgetTransform.Render(surface.Canvas, CurrentWidget(widget), context, renderer);
            surface.Canvas.Flush();

            using var pixels = surface.PeekPixels()
                ?? throw new InvalidOperationException("Could not inspect gauge container regression pixels.");
            var center = pixels.GetPixelColor(30, 30);
            var border = pixels.GetPixelColor(10, 30);

            Assert(center.Alpha == 255 && center.Red == 0x10 && center.Green == 0x20 && center.Blue == 0x30,
                "Gauge did not use the universal widget background.");
            Assert(border.Alpha == 255 && border.Red == 255 && border.Green == 255 && border.Blue == 255,
                "Gauge did not use the universal widget border.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void GaugeNeedleOffsetsAndPointer()
    {
        const string metric = RuntimeMetricContract.Fps;
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-gauge-needle-regression");
        try
        {
            var widget = new WidgetDefinition
            {
                Id = "gauge-needle",
                Type = WidgetTypeContract.Gauge,
                X = 0,
                Y = 0,
                Width = 100,
                Height = 100,
                Metric = metric,
                Min = 0,
                Max = 100,
                StartAngle = 0,
                EndAngle = 180,
                TrackEnabled = false,
                NeedleEnabled = true,
                NeedleThickness = 4f,
                NeedleColor = "#FF00FF00",
                NeedleStartOffset = 10f,
                NeedleEndOffset = 10f,
                NeedlePointerLength = 10f,
                NeedlePointerThickness = 8f,
                NeedlePointerColor = "#FFFF0000"
            };

            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, new[] { CurrentWidget(widget) });
            using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = 50d },
                antialias: false,
                typefaces);
            using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(100, 100))
                ?? throw new InvalidOperationException("Could not create gauge needle regression surface.");
            using var renderer = new PinkieSysMon.Widgets.GaugeWidgetRenderer();

            surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
            PinkieSysMon.Widgets.WidgetTransform.Render(surface.Canvas, CurrentWidget(widget), context, renderer);
            surface.Canvas.Flush();

            using var pixels = surface.PeekPixels()
                ?? throw new InvalidOperationException("Could not inspect gauge needle regression pixels.");
            var center = pixels.GetPixelColor(50, 50);
            var body = pixels.GetPixelColor(30, 50);
            var pointer = pixels.GetPixelColor(15, 50);
            var beyondEnd = pixels.GetPixelColor(5, 50);

            Assert(center.Alpha == 0, "NeedleStartOffset did not suppress the center segment.");
            Assert(body.Alpha == 255 && body.Green == 255 && body.Red == 0,
                "Gauge needle body was not drawn on the metric axis.");
            Assert(pointer.Alpha == 255 && pointer.Red == 255 && pointer.Green == 0,
                "Gauge pointer did not replace the distal needle segment.");
            Assert(beyondEnd.Alpha == 0, "NeedleEndOffset did not suppress the radial end segment.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void GaugeNeedleRendersAboveTrack()
    {
        const string metric = RuntimeMetricContract.Fps;
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-gauge-layer-regression");
        try
        {
            var widget = new WidgetDefinition
            {
                Id = "gauge-layering",
                Type = WidgetTypeContract.Gauge,
                X = 0,
                Y = 0,
                Width = 100,
                Height = 100,
                Metric = metric,
                Min = 0,
                Max = 100,
                StartAngle = 0,
                EndAngle = 180,
                TrackEnabled = true,
                Thickness = 20f,
                TrackBackgroundColor = "#00000000",
                Color = "#FF0000FF",
                ThresholdMode = "State",
                NeedleEnabled = true,
                NeedleThickness = 4f,
                NeedleColor = "#FFFFFF00",
                NeedleStartOffset = 0f,
                NeedleEndOffset = 0f,
                NeedlePointerLength = 0f
            };

            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, new[] { CurrentWidget(widget) });
            using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = 50d },
                antialias: false,
                typefaces);
            using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(100, 100))
                ?? throw new InvalidOperationException("Could not create gauge layering regression surface.");
            using var renderer = new PinkieSysMon.Widgets.GaugeWidgetRenderer();

            surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
            PinkieSysMon.Widgets.WidgetTransform.Render(surface.Canvas, CurrentWidget(widget), context, renderer);
            surface.Canvas.Flush();

            using var pixels = surface.PeekPixels()
                ?? throw new InvalidOperationException("Could not inspect gauge layering regression pixels.");
            var needle = pixels.GetPixelColor(10, 50);
            var track = pixels.GetPixelColor(10, 54);

            Assert(needle.Alpha == 255 && needle.Red == 255 && needle.Green == 255 && needle.Blue == 0,
                "Gauge needle did not render above the track layer.");
            Assert(track.Alpha == 255 && track.Blue == 255 && track.Red == 0 && track.Green == 0,
                "Gauge track was not present beneath the needle layer.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void StaticStateFileAnimationClassification()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-state-animation-classification");
        try
        {
            const string staticSource = "static-state.png";
            const string animatedSource = "animated-state.gif";
            File.WriteAllBytes(
                Path.Combine(tempRoot, staticSource),
                Convert.FromBase64String(
                    "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAFElEQVR4nGP8z8Dwn4GBgYGJAQoAHxcCAk+Uzr4AAAAASUVORK5CYII="));
            File.WriteAllBytes(
                Path.Combine(tempRoot, animatedSource),
                Convert.FromBase64String(
                    "R0lGODlhAgACAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQICgAAACwAAAAAAgACAAAIBgABCAQQEAAh+QQICgAAACwAAAAAAgACAIEAAP8AAAAAAAAAAAAIBgABCAQQEAA7"));

            using var images = new ImageAssetCache(tempRoot);
            using var icons = new IconAssetCache(tempRoot);
            using var visuals = new PinkieSysMon.Widgets.StateIconVisualCache(images, icons);

            var staticProfile = new StateVisualProfile(
                StateVisualProfileContract.SourceFile,
                staticSource,
                null,
                1f,
                StateVisualProfileContract.FitContain,
                true,
                null);
            var animatedProfile = staticProfile with { Source = animatedSource };

            Assert(!visuals.UsesActivationRelativePlayback(staticProfile),
                "A single-frame state image must not enter activation-relative animation playback.");
            Assert(visuals.UsesActivationRelativePlayback(animatedProfile),
                "A multi-frame state image must retain activation-relative animation playback.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void StatefulWidgetShadowCacheAnimationContract()
    {
        const string metric = "test.stateful.shadow";
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-state-shadow-cache");
        try
        {
            const string staticSource = "static-state.png";
            const string animatedSource = "animated-state.gif";
            File.WriteAllBytes(
                Path.Combine(tempRoot, staticSource),
                Convert.FromBase64String(
                    "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAFElEQVR4nGP8z8Dwn4GBgYGJAQoAHxcCAk+Uzr4AAAAASUVORK5CYII="));
            File.WriteAllBytes(
                Path.Combine(tempRoot, animatedSource),
                Convert.FromBase64String(
                    "R0lGODlhAgACAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQICgAAACwAAAAAAgACAAAIBgABCAQQEAAh+QQICgAAACwAAAAAAgACAIEAAP8AAAAAAAAAAAAIBgABCAQQEAA7"));

            var widget = new WidgetDefinition
            {
                Id = "stateful-shadow-cache",
                Type = WidgetTypeContract.Binary,
                Metric = metric,
                X = 4,
                Y = 4,
                Width = 16,
                Height = 16,
                ShadowEnabled = true,
                ShadowOpacity = 0.8f,
                ShadowBlur = 3f,
                ShadowOffsetX = 1f,
                ShadowOffsetY = 1f
            };
            widget.EnsureStateVisualProfiles();
            var falseProfile = widget.Profiles![BinarySignalContract.FalseKey];
            falseProfile.SourceType = StateVisualProfileContract.SourceFile;
            falseProfile.Source = staticSource;
            var trueProfile = widget.Profiles[BinarySignalContract.TrueKey];
            trueProfile.SourceType = StateVisualProfileContract.SourceFile;
            trueProfile.Source = animatedSource;

            var currentWidget = (C.BinaryWidgetDefinition)CurrentWidget(widget);
            var currentTrueProfile = currentWidget.Profiles[BinarySignalContract.TrueKey];

            using var images = new ImageAssetCache(tempRoot);
            images.Preload(new[] { currentWidget });
            using var icons = new IconAssetCache(tempRoot);
            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, new[] { currentWidget });
            using var renderer = new PinkieSysMon.Widgets.BinaryWidgetRenderer(images, icons);
            using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = false },
                antialias: true,
                typefaces);

            Assert(context.TryGetShadowContentKey(currentWidget, renderer, out var falseKey),
                "A static state profile must be eligible for shadow-composite caching.");

            context.UpdateMetrics(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = true });
            Assert(!context.TryGetShadowContentKey(currentWidget, renderer, out _),
                "An animated active state must remain live instead of freezing inside the shadow cache.");

            currentTrueProfile.ContentType = C.StateContentType.Image;
            currentTrueProfile.Asset.SourceType = C.ImageAssetSourceType.File;
            currentTrueProfile.Asset.Source = staticSource;
            Assert(context.TryGetShadowContentKey(currentWidget, renderer, out var trueKey),
                "A static replacement state must become eligible for shadow-composite caching.");
            Assert(!falseKey.Equals(trueKey),
                "Changing Binary state must invalidate a cached static state composite even when both states reuse one asset.");

            currentTrueProfile.ContentType = C.StateContentType.Value;
            currentTrueProfile.TextPresentation ??= new C.TextPresentationDefinition();
            context.UpdateMetrics(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = 1 });
            Assert(context.TryGetShadowContentKey(currentWidget, renderer, out var firstValueKey),
                "A value-backed state must be shadow-cacheable while its rendered text is stable.");
            context.UpdateMetrics(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = 2 });
            Assert(context.TryGetShadowContentKey(currentWidget, renderer, out var secondValueKey) &&
                   !firstValueKey.Equals(secondValueKey),
                "A value-backed Binary shadow key must invalidate when visible metric text changes within the same state.");

            currentTrueProfile.TextPresentation.OverflowMode = ValueOverflowContract.Scroll;
            Assert(!context.TryGetShadowContentKey(currentWidget, renderer, out _),
                "A scrolling value-backed state must remain live instead of freezing inside the shadow cache.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void Pass9FStatefulRendererUnificationContract()
    {
        var sharedBase = typeof(PinkieSysMon.Widgets.StateProfileWidgetRenderer);
        foreach (var rendererType in new[]
                 {
                     typeof(PinkieSysMon.Widgets.BinaryWidgetRenderer),
                     typeof(PinkieSysMon.Widgets.PowerWidgetRenderer),
                     typeof(PinkieSysMon.Widgets.MediaSystemWidgetRenderer),
                     typeof(PinkieSysMon.Widgets.MediaPlayerWidgetRenderer)
                 })
        {
            Assert(rendererType.BaseType == sharedBase,
                $"{rendererType.Name} must use the shared state-profile presentation core.");
        }

        var legacyCompiledVisual = typeof(PinkieSysMon.Widgets.StateIconVisualCache).GetNestedType(
            "CompiledVisual",
            System.Reflection.BindingFlags.NonPublic);
        Assert(legacyCompiledVisual is null,
            "StateIconVisualCache must not reintroduce its former duplicate image rendering implementation.");

        var createState = typeof(ImageVisual).GetMethod(
            nameof(ImageVisual.CreateState),
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        var drawAtElapsed = typeof(ImageVisual).GetMethod(
            nameof(ImageVisual.DrawAtElapsed),
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        Assert(createState is not null && drawAtElapsed is not null,
            "Shared ImageVisual must own state-profile compilation and activation-relative drawing.");
    }

    private static void RuntimeJpegZeroCopyTransportContract()
    {
        var sendJpeg = typeof(TrofeoTransport).GetMethod(
            nameof(TrofeoTransport.SendJpeg),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        Assert(sendJpeg is not null,
            "Trofeo transport must expose the JPEG send entry point.");

        var parameters = sendJpeg!.GetParameters();
        Assert(parameters.Length == 1 && parameters[0].ParameterType == typeof(ReadOnlySpan<byte>),
            "Trofeo JPEG transport must consume a ReadOnlySpan<byte> so encoded SKData does not require a per-frame managed byte[] copy.");

        var renderedType = typeof(DashboardRenderer.RenderedJpeg);
        Assert(typeof(IDisposable).IsAssignableFrom(renderedType),
            "Rendered JPEG ownership must be explicit so the native SKData buffer is disposed after USB transfer.");
        Assert(renderedType.GetProperty(nameof(DashboardRenderer.RenderedJpeg.EncodedData))?.PropertyType == typeof(SkiaSharp.SKData),
            "Rendered JPEG must retain the encoder-owned SKData buffer instead of materializing a managed byte array.");
        Assert(renderedType.GetProperty(nameof(DashboardRenderer.RenderedJpeg.Bytes))?.PropertyType == typeof(ReadOnlySpan<byte>),
            "Rendered JPEG must expose encoded bytes as a zero-copy span over SKData.");
    }

    private static void StableWidgetShadowIsReused()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-shadow-regression");
        try
        {
            var widget = new WidgetDefinition
            {
                Id = "gauge-shadow",
                Type = WidgetTypeContract.Gauge,
                Metric = RuntimeMetricContract.Fps,
                X = 10,
                Y = 10,
                Width = 30,
                Height = 30,
                Min = 0,
                Max = 100,
                ShadowEnabled = true,
                ShadowOpacity = 0.8f,
                ShadowBlur = 4f,
                ShadowOffsetX = 2f,
                ShadowOffsetY = 2f
            };

            var currentWidget = CurrentWidget(widget);

            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, Array.Empty<C.WidgetDefinition>());
            using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
                antialias: true,
                typefaces);
            using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(100, 100))
                ?? throw new InvalidOperationException("Could not create Skia regression surface.");
            var renderer = new CountingWidgetRenderer(WidgetTypeContract.Gauge);

            PinkieSysMon.Widgets.WidgetTransform.Render(surface.Canvas, currentWidget, context, renderer);
            PinkieSysMon.Widgets.WidgetTransform.Render(surface.Canvas, currentWidget, context, renderer);

            Assert(renderer.RenderCount == 1,
                $"Stable shadowed widget rendered {renderer.RenderCount} times; expected one cached render.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void NumericWidgetShadowInvalidatesOnValueChange()
    {
        const string metric = RuntimeMetricContract.Fps;
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-shadow-regression");
        try
        {
            var widget = new WidgetDefinition
            {
                Id = "bar-shadow",
                Type = WidgetTypeContract.Bar,
                Metric = metric,
                X = 10,
                Y = 10,
                Width = 50,
                Height = 10,
                Min = 0,
                Max = 100,
                ShadowEnabled = true,
                ShadowOpacity = 0.8f,
                ShadowBlur = 4f,
                ShadowOffsetX = 2f,
                ShadowOffsetY = 2f
            };

            var currentWidget = CurrentWidget(widget);

            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, Array.Empty<C.WidgetDefinition>());
            using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = 25d },
                antialias: true,
                typefaces);
            using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(100, 100))
                ?? throw new InvalidOperationException("Could not create Skia regression surface.");
            var renderer = new CountingWidgetRenderer(WidgetTypeContract.Bar);

            PinkieSysMon.Widgets.WidgetTransform.Render(surface.Canvas, currentWidget, context, renderer);
            PinkieSysMon.Widgets.WidgetTransform.Render(surface.Canvas, currentWidget, context, renderer);
            Assert(renderer.RenderCount == 1, "Unchanged numeric shadow state was not reused.");

            context.UpdateMetrics(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metric] = 50d });
            PinkieSysMon.Widgets.WidgetTransform.Render(surface.Canvas, currentWidget, context, renderer);
            Assert(renderer.RenderCount == 2, "Changed numeric shadow state did not invalidate the cache.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }


    private static void CachedWidgetShadowFollowsTranslation()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-shadow-regression");
        try
        {
            var widget = new WidgetDefinition
            {
                Id = "translated-shadow",
                Type = WidgetTypeContract.Gauge,
                Metric = RuntimeMetricContract.Fps,
                Min = 0,
                Max = 100,
                X = 10,
                Y = 10,
                Width = 20,
                Height = 12,
                ShadowEnabled = true,
                ShadowOpacity = 1f,
                ShadowBlur = 2f,
                ShadowOffsetX = 2f,
                ShadowOffsetY = 2f
            };

            var currentWidget = CurrentWidget(widget);

            using var typefaces = new PinkieSysMon.Widgets.TypefaceCache(tempRoot, Array.Empty<C.WidgetDefinition>());
            using var context = new PinkieSysMon.Widgets.WidgetRenderContext(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
                antialias: true,
                typefaces);
            using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(
                120, 80, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul))
                ?? throw new InvalidOperationException("Could not create Skia regression surface.");
            using var renderer = new SolidBoxCountingWidgetRenderer(WidgetTypeContract.Gauge);

            surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
            PinkieSysMon.Widgets.WidgetTransform.Render(surface.Canvas, currentWidget, context, renderer);
            Assert(renderer.RenderCount == 1, "Initial translated-shadow render did not populate the cache.");

            var oldCenterX = (int)(currentWidget.X + currentWidget.Width / 2f);
            var oldCenterY = (int)(currentWidget.Y + currentWidget.Height / 2f);
            currentWidget.X = 70;
            currentWidget.Y = 45;

            surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
            PinkieSysMon.Widgets.WidgetTransform.Render(surface.Canvas, currentWidget, context, renderer);
            Assert(renderer.RenderCount == 1, "Pure widget translation rebuilt a reusable shadow cache entry.");
            surface.Canvas.Flush();

            using var pixels = surface.PeekPixels()
                ?? throw new InvalidOperationException("Could not inspect translated shadow regression surface pixels.");
            var newCenterX = (int)(currentWidget.X + currentWidget.Width / 2f);
            var newCenterY = (int)(currentWidget.Y + currentWidget.Height / 2f);
            Assert(pixels.GetPixelColor(newCenterX, newCenterY).Alpha > 0,
                "Cached shadow composite did not move with the widget.");
            Assert(pixels.GetPixelColor(oldCenterX, oldCenterY).Alpha == 0,
                "Cached shadow composite remained at the widget's previous position.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static string WriteGlobalIconTestAssets(string applicationRoot, bool includeBroken)
    {
        var libraryRoot = Path.Combine(applicationRoot, "assets", "icons", "Mixed");
        Directory.CreateDirectory(libraryRoot);

        File.WriteAllText(
            Path.Combine(libraryRoot, "vector.svg"),
            "<svg viewBox=\"0 0 10 10\" xmlns=\"http://www.w3.org/2000/svg\"><rect x=\"0\" y=\"0\" width=\"10\" height=\"10\" fill=\"currentColor\"/></svg>");

        static void WriteBase64(string path, string value) =>
            File.WriteAllBytes(path, Convert.FromBase64String(value));

        WriteBase64(
            Path.Combine(libraryRoot, "red.png"),
            "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAFElEQVR4nGP8z8Dwn4GBgYGJAQoAHxcCAk+Uzr4AAAAASUVORK5CYII=");
        const string jpeg =
            "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCAACAAIDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDi6KKK+ZP3E//Z";
        WriteBase64(Path.Combine(libraryRoot, "red.jpg"), jpeg);
        WriteBase64(Path.Combine(libraryRoot, "red.jpeg"), jpeg);
        WriteBase64(
            Path.Combine(libraryRoot, "red.bmp"),
            "Qk1GAAAAAAAAADYAAAAoAAAAAgAAAAIAAAABACAAAAAAABAAAADEDgAAxA4AAAAAAAAAAAAAAAD//wAA//8AAP//AAD//w==");
        WriteBase64(
            Path.Combine(libraryRoot, "red.gif"),
            "R0lGODdhAgACAIEAAP8AAAAAAAAAAAAAACwAAAAAAgACAAAIBgABCAQQEAA7");
        WriteBase64(
            Path.Combine(libraryRoot, "red.ico"),
            "AAABAAEAAgIAAAAAIABNAAAAFgAAAIlQTkcNChoKAAAADUlIRFIAAAACAAAAAggGAAAAcrYNJAAAABRJREFUeJxj/M/A8J+BgYGBiQEKAB8XAgJPlM6+AAAAAElFTkSuQmCC");
        WriteBase64(
            Path.Combine(libraryRoot, "red.webp"),
            "UklGRjwAAABXRUJQVlA4IDAAAADQAQCdASoCAAIAAUAmJaACdLoB+AADsAD+8ut//NgVzXPv9//S4P0uD9Lg/9KQAAA=");
        WriteBase64(
            Path.Combine(libraryRoot, "animated.gif"),
            "R0lGODlhAgACAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQICgAAACwAAAAAAgACAAAIBgABCAQQEAAh+QQICgAAACwAAAAAAgACAIEAAP8AAAAAAAAAAAAIBgABCAQQEAA7");

        if (includeBroken)
            File.WriteAllText(Path.Combine(libraryRoot, "broken.png"), "not an image");

        return libraryRoot;
    }

    private static void WriteSolidTestImage(
        string path,
        SkiaSharp.SKColor color,
        int width = 10,
        int height = 10)
    {
        using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(width, height))
            ?? throw new InvalidOperationException("Could not create state image test asset.");
        surface.Canvas.Clear(color);
        surface.Canvas.Flush();
        using var image = surface.Snapshot();
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("Could not encode state image test asset.");
        File.WriteAllBytes(path, data.ToArray());
    }

    private static void WriteHorizontalQuarterTestImage(string path)
    {
        using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(40, 10))
            ?? throw new InvalidOperationException("Could not create Bar progress test image.");
        using var paint = new SkiaSharp.SKPaint { IsAntialias = false, Style = SkiaSharp.SKPaintStyle.Fill };
        var colors = new[]
        {
            SkiaSharp.SKColors.Red,
            SkiaSharp.SKColors.Lime,
            SkiaSharp.SKColors.Blue,
            SkiaSharp.SKColors.Yellow
        };
        for (var i = 0; i < colors.Length; i++)
        {
            paint.Color = colors[i];
            surface.Canvas.DrawRect(new SkiaSharp.SKRect(i * 10, 0, (i + 1) * 10, 10), paint);
        }
        surface.Canvas.Flush();
        using var image = surface.Snapshot();
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("Could not encode Bar progress test image.");
        File.WriteAllBytes(path, data.ToArray());
    }

    private static void WriteVerticalBandTestImage(string path)
    {
        using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(10, 10))
            ?? throw new InvalidOperationException("Could not create Bar fit test image.");
        using var paint = new SkiaSharp.SKPaint { IsAntialias = false, Style = SkiaSharp.SKPaintStyle.Fill };

        paint.Color = SkiaSharp.SKColors.Red;
        surface.Canvas.DrawRect(new SkiaSharp.SKRect(0, 0, 10, 2), paint);
        paint.Color = SkiaSharp.SKColors.Lime;
        surface.Canvas.DrawRect(new SkiaSharp.SKRect(0, 2, 10, 8), paint);
        paint.Color = SkiaSharp.SKColors.Blue;
        surface.Canvas.DrawRect(new SkiaSharp.SKRect(0, 8, 10, 10), paint);
        surface.Canvas.Flush();

        using var image = surface.Snapshot();
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("Could not encode Bar fit test image.");
        File.WriteAllBytes(path, data.ToArray());
    }

    private static void ResizeLocalAxisProjection()
    {
        var unrotated = WidgetResizeGeometry.ProjectCanvasDeltaToLocal(new System.Drawing.PointF(12f, -7f), 0d);
        Assert(Math.Abs(unrotated.X - 12f) < 0.001f && Math.Abs(unrotated.Y + 7f) < 0.001f,
            "Unrotated resize delta was not preserved.");

        var quarterTurn = WidgetResizeGeometry.ProjectCanvasDeltaToLocal(new System.Drawing.PointF(0f, 10f), 90d);
        Assert(Math.Abs(quarterTurn.X - 10f) < 0.001f && Math.Abs(quarterTurn.Y) < 0.001f,
            "Rotated resize did not follow the widget local X axis.");
    }

    private static void GaugeResizeGeometryClamp()
    {
        var widget = new PinkieSysMon.DashboardModel.GaugeWidgetDefinition
        {
            Width = 200f,
            Height = 200f,
            Gap = 3f,
            Track = new PinkieSysMon.DashboardModel.GaugeTrackDefinition
            {
                Enabled = true,
                Thickness = 20f,
                BorderWidth = 2f
            },
            Needle = new PinkieSysMon.DashboardModel.GaugeNeedleDefinition
            {
                Enabled = true,
                StartOffset = 30f,
                EndOffset = 10f
            }
        };

        var trackMinimum = 2f * (2f * widget.Track.BorderWidth + 2f * widget.Gap + widget.Track.Thickness);
        var needleMinimum = MathF.BitIncrement(2f * (widget.Needle.StartOffset + widget.Needle.EndOffset));
        var expectedMinimum = Math.Max(WidgetResizeGeometry.MinimumDimension, Math.Max(trackMinimum, needleMinimum));
        var actualMinimum = WidgetResizeGeometry.GetMinimumGaugeSize(widget);
        Assert(actualMinimum == expectedMinimum, "Gauge minimum size does not preserve valid track/needle geometry.");

        var expanded = WidgetResizeGeometry.ResolveGaugeSize(widget, 200f, 20f, 20f);
        Assert(Math.Abs(expanded - 220f) < 0.001f, "Gauge diagonal resize did not preserve 1:1 geometry.");

        var clamped = WidgetResizeGeometry.ResolveGaugeSize(widget, 200f, -1000f, -1000f);
        Assert(clamped == expectedMinimum, "Gauge resize did not clamp to its valid minimum size.");
    }

    private static void PreviewResizeGeometryInvalidation()
    {
        var tempRoot = CreateTemporaryApplicationRoot("PinkieSysMon-preview-resize-regression");
        try
        {
            var dashboardPath = Path.Combine(tempRoot, "dashboards", "resize-preview.json");
            var definition = DashboardDefinition.Parse(
                $$"""
                {
                  "schemaVersion": {{DashboardSchemaMigration.CanvasImageLayerSchemaVersion}},
                  "canvas": {
                    "width": 1920,
                    "height": 480,
                    "orientation": 0,
                    "backgroundColor": "#000000"
                  },
                  "widgets": [
                    {
                      "type": "bar",
                      "id": "resize-bar",
                      "x": 10,
                      "y": 10,
                      "width": 20,
                      "height": 20,
                      "metric": "system.runtime.fps",
                      "min": 0,
                      "max": 100,
                      "color": "#FFFFFFFF"
                    }
                  ]
                }
                """,
                dashboardPath);
            var canonicalDefinition = PropertyModelNormalizationMigration.MapLegacyDefinition(definition);
            var widget = canonicalDefinition.Widgets.Single();

            using var preview = new DashboardPreviewRenderer(canonicalDefinition);
            var metrics = new Dictionary<string, object?> { [RuntimeMetricContract.Fps] = 100d };
            using var before = SkiaSharp.SKBitmap.Decode(preview.RenderToPng(metrics));
            Assert(before.GetPixel(35, 15) == SkiaSharp.SKColors.Black,
                "Bar unexpectedly occupied the post-resize area before geometry changed.");

            widget.Width = 40f;
            preview.InvalidateWidgetGeometry(widget);
            using var after = SkiaSharp.SKBitmap.Decode(preview.RenderToPng(metrics));
            Assert(after.GetPixel(35, 15) == SkiaSharp.SKColors.White,
                "Preview renderer kept stale compiled widget geometry after resize invalidation.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static TextPresentationDefinition GetBinaryText(WidgetDefinition widget, string stateKey)
    {
        widget.EnsureStateVisualProfiles();
        return widget.Profiles![stateKey].Text
            ?? throw new InvalidOperationException($"Binary state '{stateKey}' has no text presentation.");
    }

    private static string CreateTemporaryApplicationRoot(string prefix)
    {
        var root = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "config"));
        Directory.CreateDirectory(Path.Combine(root, "dashboards"));

        // WidgetDefinition defaults Binary state visuals to lucide:check. Tests that
        // intentionally exercise mixed value/icon geometry therefore need a minimally
        // valid global icon library just like a real application root has.
        var lucideRoot = Path.Combine(root, "assets", "icons", "lucide");
        Directory.CreateDirectory(lucideRoot);
        File.WriteAllText(
            Path.Combine(lucideRoot, "check.svg"),
            "<svg viewBox=\"0 0 24 24\" xmlns=\"http://www.w3.org/2000/svg\"><path d=\"M4 12l5 5L20 6\"/></svg>");

        CopyBundledFonts(root);

        File.WriteAllText(Path.Combine(root, "config", "app.json"), "{}");
        return root;
    }

    private static void CopyBundledFonts(string temporaryRoot)
    {
        var applicationRoot = ApplicationRootLocator.Find(AppContext.BaseDirectory);
        var sourceFonts = GlobalAssetResolver.GetFontsRoot(applicationRoot);
        if (!Directory.Exists(sourceFonts))
            throw new DirectoryNotFoundException($"Bundled fonts directory was not found: '{sourceFonts}'.");

        var destinationFonts = Path.Combine(temporaryRoot, "assets", "fonts");
        foreach (var sourcePath in Directory
                     .EnumerateFiles(sourceFonts, "*.*", SearchOption.AllDirectories)
                     .Where(path =>
                         Path.GetExtension(path).Equals(".ttf", StringComparison.OrdinalIgnoreCase) ||
                         Path.GetExtension(path).Equals(".otf", StringComparison.OrdinalIgnoreCase)))
        {
            var relativePath = Path.GetRelativePath(sourceFonts, sourcePath);
            var destinationPath = Path.Combine(destinationFonts, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }
    }

    private static PropertyDescriptor? FindLeafProperty(PropertyDescriptorCollection properties, string name) =>
        properties
            .Cast<PropertyDescriptor>()
            .SelectMany(FlattenProperties)
            .FirstOrDefault(property => string.Equals(property.Name, name, StringComparison.Ordinal));

    private static IEnumerable<PropertyDescriptor> FlattenProperties(PropertyDescriptor property)
    {
        if (property is not PropertyGroupPropertyDescriptor)
        {
            yield return property;
            yield break;
        }

        var owner = property.GetValue(null);
        if (owner is null)
            yield break;

        foreach (PropertyDescriptor child in TypeDescriptor.GetProperties(owner))
        {
            foreach (var nested in FlattenProperties(child))
                yield return nested;
        }
    }

    private static void CollectLeafPropertyNames(
        PropertyDescriptorCollection properties,
        ISet<string> names)
    {
        foreach (PropertyDescriptor property in properties)
        {
            if (property is PropertyGroupPropertyDescriptor)
            {
                var owner = property.GetValue(null);
                if (owner is not null)
                    CollectLeafPropertyNames(TypeDescriptor.GetProperties(owner), names);
                continue;
            }

            names.Add(property.Name);
        }
    }

    private static void CollectLeafSemanticKeys(
        PropertyDescriptorCollection properties,
        ISet<string> keys)
    {
        foreach (PropertyDescriptor property in properties)
        {
            if (property is PropertyGroupPropertyDescriptor)
            {
                var owner = property.GetValue(null);
                if (owner is not null)
                    CollectLeafSemanticKeys(TypeDescriptor.GetProperties(owner), keys);
                continue;
            }

            keys.Add(property is IPropertySemanticKeyDescriptor semantic ? semantic.SemanticKey : property.Name);
        }
    }

    private static PropertyDescriptor? GetNestedProperty(
        PropertyDescriptorCollection properties,
        params string[] path)
    {
        if (path.Length == 0)
            return null;

        PropertyDescriptor? descriptor = properties[path[0]];
        object? owner = null;
        for (var i = 1; i < path.Length; i++)
        {
            if (descriptor is null)
                return null;

            owner = descriptor.GetValue(owner);
            if (owner is null)
                return null;

            var nested = TypeDescriptor.GetProperties(owner);
            descriptor = nested[path[i]];
        }

        return descriptor;
    }

    private static void PropertyModelSchema18To19Migration()
    {
        var root = new JsonObject
        {
            ["schemaVersion"] = DashboardSchemaMigration.CanvasImageLayerSchemaVersion,
            ["canvas"] = new JsonObject
            {
                ["width"] = 1920,
                ["height"] = 480,
                ["orientation"] = 0,
                ["backgroundColor"] = "#000000",
                ["backgroundImage"] = new JsonObject
                {
                    ["sourceType"] = "file",
                    ["source"] = null,
                    ["fit"] = "stretch",
                    ["opacity"] = 1f,
                    ["loop"] = true,
                    ["color"] = "#FFFFFFFF"
                },
                ["foregroundImage"] = new JsonObject
                {
                    ["sourceType"] = "icon",
                    ["source"] = null,
                    ["fit"] = "contain",
                    ["opacity"] = 0.5f,
                    ["loop"] = false,
                    ["color"] = "#80FFFFFF"
                }
            },
            ["widgets"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = WidgetTypeContract.Value,
                    ["id"] = "value-1",
                    ["width"] = 0,
                    ["height"] = 0,
                    ["valueSource"] = "text",
                    ["text"] = "hello",
                    ["fontSize"] = 20,
                    ["overflowMode"] = "none",
                    ["barImageSource"] = "obsolete-stale.png"
                },
                new JsonObject
                {
                    ["type"] = WidgetTypeContract.Image,
                    ["id"] = "image-1",
                    ["width"] = 100,
                    ["height"] = 50,
                    ["sourceType"] = "ICON",
                    ["source"] = "lucide:star",
                    ["fit"] = "COVER",
                    ["loop"] = false,
                    ["opacity"] = 0.5f
                },
                new JsonObject
                {
                    ["type"] = WidgetTypeContract.Binary,
                    ["id"] = "binary-1",
                    ["width"] = 0,
                    ["height"] = 0,
                    ["metric"] = "system.test.binary",
                    ["profiles"] = new JsonObject
                    {
                        [BinarySignalContract.TrueKey] = new JsonObject
                        {
                            ["sourceType"] = "value",
                            ["source"] = "stale-image-source",
                            ["fit"] = "contain",
                            ["loop"] = true,
                            ["opacity"] = 1f,
                            ["text"] = new JsonObject { ["overflowMode"] = "none" }
                        }
                    }
                },
                new JsonObject
                {
                    ["type"] = WidgetTypeContract.Gauge,
                    ["id"] = "gauge-1",
                    ["width"] = 100,
                    ["height"] = 100,
                    ["metric"] = "system.test.gauge",
                    ["min"] = 0,
                    ["max"] = 100,
                    ["thresholdMode"] = "segmenttransition",
                    ["threshold1Enabled"] = true,
                    ["threshold1Value"] = 50,
                    ["threshold1Color"] = "#FFFF0000",
                    ["thickness"] = 12,
                    ["trackEnabled"] = true,
                    ["trackBackgroundColor"] = "#10000000",
                    ["needleEnabled"] = true,
                    ["needleThickness"] = 3,
                    ["needlePointerLength"] = 7,
                    ["needlePointerThickness"] = 2
                },
                new JsonObject
                {
                    ["type"] = WidgetTypeContract.Bar,
                    ["id"] = "bar-1",
                    ["width"] = 200,
                    ["height"] = 20,
                    ["metric"] = "system.test.bar",
                    ["min"] = 0,
                    ["max"] = 100,
                    ["barContentMode"] = "Fill",
                    ["barImageSource"] = "images\\dormant.png",
                    ["barImageFit"] = "Cover",
                    ["barProgressMode"] = "Reveal",
                    ["loop"] = false
                },
                new JsonObject
                {
                    ["type"] = WidgetTypeContract.Power,
                    ["id"] = "power-1",
                    ["width"] = 32,
                    ["height"] = 32,
                    ["source"] = PowerMetricContract.BatterySource
                },
                new JsonObject
                {
                    ["type"] = WidgetTypeContract.MediaSystem,
                    ["id"] = "media.system-1",
                    ["width"] = 32,
                    ["height"] = 32,
                    ["source"] = MediaMetricContract.InputSource
                },
                new JsonObject
                {
                    ["type"] = WidgetTypeContract.MediaPlayer,
                    ["id"] = "media.player-1",
                    ["width"] = 32,
                    ["height"] = 32,
                    ["source"] = "media.playback"
                }
            }
        };

        var changed = DashboardSchemaMigration.UpgradeToPropertyModelNormalization(root);
        Assert(changed, "Schema 18 property-model migration must report a change.");
        Assert(root["schemaVersion"]!.GetValue<int>() == DashboardSchemaMigration.PropertyModelNormalizationSchemaVersion,
            "Property-model migration must produce schemaVersion 19.");

        var canvas = root["canvas"]!.AsObject();
        Assert(canvas["foregroundImage"]!["asset"]!["sourceType"]!.GetValue<string>() == "icon",
            "Canvas image-layer source identity must move under Asset.");
        Assert(!canvas["foregroundImage"]!.AsObject().ContainsKey("sourceType"),
            "Schema 19 Canvas image layer must not retain top-level SourceType.");

        var widgets = root["widgets"]!.AsArray().Select(node => node!.AsObject()).ToArray();
        JsonObject Find(string id) => widgets.Single(widget => widget["id"]!.GetValue<string>() == id);

        var value = Find("value-1");
        Assert(value["sourceKind"]!.GetValue<string>() == "Text",
            "Value valueSource must migrate to canonical sourceKind casing.");
        Assert(value["textPresentation"]!["fontSize"]!.GetValue<float>() == 20f &&
               value["textPresentation"]!["overflowMode"]!.GetValue<string>() == ValueOverflowContract.None,
            "Value flat text properties must migrate into canonical TextPresentation.");
        Assert(!value.ContainsKey("valueSource") && !value.ContainsKey("fontSize") && !value.ContainsKey("barImageSource"),
            "Schema 19 Value output must drop old and unrelated schema-18 union fields.");

        var image = Find("image-1");
        Assert(image["asset"]!["sourceType"]!.GetValue<string>() == "icon" &&
               image["asset"]!["fit"]!.GetValue<string>() == "cover" &&
               image["asset"]!["source"]!.GetValue<string>() == "lucide:star",
            "Image source fields must migrate into canonical Asset presentation.");
        Assert(!image.ContainsKey("sourceType") && !image.ContainsKey("fit") && !image.ContainsKey("loop"),
            "Schema 19 Image output must not retain top-level asset fields.");

        var binary = Find("binary-1");
        var trueProfile = binary["profiles"]![BinarySignalContract.TrueKey]!.AsObject();
        Assert(trueProfile["contentType"]!.GetValue<string>() == "value",
            "Schema-18 state sourceType=value must migrate to ContentType=value.");
        Assert(trueProfile["asset"]!["source"]!.GetValue<string>() != "stale-image-source" &&
               trueProfile["textPresentation"]!["overflowMode"]!.GetValue<string>() == ValueOverflowContract.None,
            "Value state migration must use a valid dormant default asset and canonicalize text overflow casing.");

        var gauge = Find("gauge-1");
        Assert(gauge["thresholds"]!["mode"]!.GetValue<string>() == "SegmentTransition" &&
               gauge["thresholds"]!["items"]![0]!["value"]!.GetValue<double>() == 50d,
            "Gauge numbered threshold fields must migrate into ThresholdSetDefinition.");
        Assert(gauge["track"]!["thickness"]!.GetValue<float>() == 12f &&
               gauge["needle"]!["pointer"]!["length"]!.GetValue<float>() == 7f,
            "Gauge track and needle/pointer fields must migrate into canonical subobjects.");
        Assert(!gauge.ContainsKey("threshold1Value") && !gauge.ContainsKey("trackEnabled") && !gauge.ContainsKey("needlePointerLength"),
            "Schema 19 Gauge output must drop flat threshold/track/needle fields.");

        var bar = Find("bar-1");
        Assert(bar["contentMode"]!.GetValue<string>() == BarImageContract.ContentModeFill &&
               bar["image"]!["source"]!.GetValue<string>() == "images\\dormant.png" &&
               !bar["image"]!["loop"]!.GetValue<bool>(),
            "Bar migration must preserve dormant image configuration inside the canonical Image subobject.");

        Assert(Find("power-1")["powerSource"]!.GetValue<string>() == PowerMetricContract.BatterySource,
            "Power root Source must migrate to PowerSource.");
        Assert(Find("media.system-1")["mediaSource"]!.GetValue<string>() == MediaMetricContract.InputSource,
            "Media System root Source must migrate to MediaSource.");
        Assert(!Find("media.player-1").ContainsKey("source"),
            "Media Player inert schema-18 root Source must be removed.");
    }

    private static void PropertyModelFullMigrationChain()
    {
        var root = new JsonObject
        {
            ["schemaVersion"] = 10,
            ["canvas"] = new JsonObject
            {
                ["orientation"] = 0,
                ["backgroundColor"] = "#000000",
                ["background"] = "images\\background.png",
                ["foreground"] = null
            },
            ["widgets"] = new JsonArray()
        };

        var changed = DashboardSchemaMigration.UpgradeToPropertyModelNormalization(root);
        Assert(changed && root["schemaVersion"]!.GetValue<int>() == 19,
            "The complete legacy migration chain must terminate at schemaVersion 19.");
        Assert(root["canvas"]!["width"]!.GetValue<int>() == FrameGeometry.DefaultNativeWidth &&
               root["canvas"]!["height"]!.GetValue<int>() == FrameGeometry.DefaultNativeHeight,
            "Full migration must preserve the schema-16 native Canvas geometry upgrade.");
        Assert(root["canvas"]!["backgroundImage"]!["asset"]!["source"]!.GetValue<string>() == "images\\background.png",
            "Full migration must carry legacy Canvas image paths through schema 18 into the schema-19 Asset contract.");
    }

    private static void CanonicalDashboardJsonStrictUnknownFieldRejection()
    {
        var definition = CreateMinimalCanonicalDashboard();
        var root = JsonNode.Parse(DashboardJson.Serialize(definition, indented: false))!.AsObject();
        root["widgets"]![0]!.AsObject()["obsoleteSyntheticProperty"] = "must fail";

        AssertThrows<InvalidDataException>(
            () => DashboardJson.ParseCurrent(root.ToJsonString(), Path.Combine(Path.GetTempPath(), "dashboard.json")),
            "Schema 19 current-schema deserialization must reject unknown widget fields.");
    }

    private static void CanonicalDashboardJsonRoundTrip()
    {
        var definition = CreateMinimalCanonicalDashboard();
        var json = DashboardJson.Serialize(definition);
        var parsed = DashboardJson.ParseCurrent(json, Path.Combine(Path.GetTempPath(), "dashboard.json"));
        var savedAgain = DashboardJson.Serialize(parsed);

        Assert(parsed.SchemaVersion == DashboardSchemaMigration.PropertyModelNormalizationSchemaVersion &&
               parsed.Widgets.Count == 1 &&
               parsed.Widgets[0] is PinkieSysMon.DashboardModel.ValueWidgetDefinition value &&
               value.SourceKind == PinkieSysMon.DashboardModel.ValueSourceKind.Text &&
               value.Text == "round-trip",
            "Canonical dashboard JSON must deserialize the polymorphic widget into its concrete schema-19 type.");

        var first = JsonNode.Parse(json)!.ToJsonString();
        var second = JsonNode.Parse(savedAgain)!.ToJsonString();
        Assert(first == second,
            "Canonical schema-19 save -> parse -> save must be structurally idempotent for an unchanged dashboard.");
    }

    private static void PropertyModelSchema19MigrationIdempotence()
    {
        var root = JsonNode.Parse(DashboardJson.Serialize(CreateMinimalCanonicalDashboard(), indented: false))!.AsObject();
        var before = root.ToJsonString();
        var changed = DashboardSchemaMigration.UpgradeToPropertyModelNormalization(root);
        Assert(!changed && root.ToJsonString() == before,
            "A schema-19 dashboard must be a no-op for the property-model migration entry point.");
    }

    private static void RuntimeDashboardLoadUsesCanonicalModel()
    {
        const string metric = "system.test.runtime-canonical";
        var root = new JsonObject
        {
            ["schemaVersion"] = DashboardSchemaMigration.CanvasImageLayerSchemaVersion,
            ["canvas"] = new JsonObject
            {
                ["width"] = 1920,
                ["height"] = 480,
                ["orientation"] = 0,
                ["backgroundColor"] = "#000000"
            },
            ["widgets"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = WidgetTypeContract.Value,
                    ["id"] = "runtime-value",
                    ["width"] = 0,
                    ["height"] = 0,
                    ["valueSource"] = "Metric",
                    ["metric"] = metric,
                    ["overflowMode"] = ValueOverflowContract.None
                }
            }
        };

        var path = Path.Combine(Path.GetTempPath(), "runtime-canonical-dashboard.json");
        var definition = PinkieSysMon.DashboardModel.DashboardDefinition.Parse(root.ToJsonString(), path);

        Assert(definition.SchemaVersion == DashboardSchemaMigration.PropertyModelNormalizationSchemaVersion &&
               definition.Widgets.Single() is PinkieSysMon.DashboardModel.ValueWidgetDefinition value &&
               value.Metric == metric,
            "Runtime dashboard loading must migrate schema 18 into the canonical schema-19 typed model before use.");
        Assert(DashboardMetricUsage.Collect(definition).SetEquals([metric]),
            "Runtime metric discovery must consume the canonical schema-19 widget model.");
    }

    private static void CanonicalDashboardMetricUsageSemantics()
    {
        const string valueMetric = "system.test.value";
        const string binaryMetric = "system.test.binary";
        const string gaugeMetric = "system.test.gauge";
        var dashboard = new PinkieSysMon.DashboardModel.DashboardDefinition
        {
            Widgets =
            [
                new PinkieSysMon.DashboardModel.ValueWidgetDefinition
                {
                    Id = "literal",
                    SourceKind = PinkieSysMon.DashboardModel.ValueSourceKind.Text,
                    Text = valueMetric
                },
                new PinkieSysMon.DashboardModel.ValueWidgetDefinition
                {
                    Id = "metric",
                    SourceKind = PinkieSysMon.DashboardModel.ValueSourceKind.Metric,
                    Metric = valueMetric
                },
                new PinkieSysMon.DashboardModel.BinaryWidgetDefinition
                {
                    Id = "binary",
                    Metric = binaryMetric
                },
                new PinkieSysMon.DashboardModel.GaugeWidgetDefinition
                {
                    Id = "gauge",
                    Metric = gaugeMetric
                },
                new PinkieSysMon.DashboardModel.PowerWidgetDefinition
                {
                    Id = "power",
                    PowerSource = PowerMetricContract.BatterySource
                },
                new PinkieSysMon.DashboardModel.MediaSystemWidgetDefinition
                {
                    Id = "media-system",
                    MediaSource = MediaMetricContract.InputSource
                },
                new PinkieSysMon.DashboardModel.MediaPlayerWidgetDefinition
                {
                    Id = "media-player"
                }
            ]
        };

        var metrics = DashboardMetricUsage.Collect(dashboard);
        Assert(metrics.Contains(valueMetric) && metrics.Contains(binaryMetric) && metrics.Contains(gaugeMetric),
            "Canonical metric-backed Value/Binary/Gauge widgets must contribute their telemetry metrics.");
        Assert(metrics.Contains(PowerMetricContract.StateMetric(PowerMetricContract.BatterySource)),
            "Canonical PowerSource must determine the required power state metric.");
        Assert(metrics.Contains(MediaMetricContract.InputAvailable) && metrics.Contains(MediaMetricContract.InputType),
            "Canonical MediaSource=input must require the input availability/type metrics.");
        Assert(metrics.Contains(MediaMetricContract.PlaybackStatus),
            "Canonical Media Player must contribute its fixed playback-status metric without a persisted source property.");
        Assert(!metrics.Contains("system.test.not-a-metric") && metrics.Count == 7,
            "Canonical metric usage must not infer telemetry dependencies from literal text or unrelated values.");
    }

    private static void CanonicalCurrentModelOwnership()
    {
        Assert(MetricContract.DashboardSchemaVersion == DashboardSchemaMigration.PropertyModelNormalizationSchemaVersion &&
               MetricContract.DashboardSchemaVersion == 19,
            "The executable current dashboard contract must be schema 19.");

        AssertThrows<ArgumentException>(
            () => _ = new EditorPropertyView(new WidgetDefinition
            {
                Type = WidgetTypeContract.Value,
                Id = "legacy-editor-rejection"
            }),
            "The production Property Editor must reject the legacy schema-18 WidgetDefinition DTO.");

        var firstPropertyView = new EditorPropertyView(
            new C.ValueWidgetDefinition { Id = "property-grid-owner-first" },
            section: EditorPropertySection.General);
        var secondPropertyView = new EditorPropertyView(
            new C.ValueWidgetDefinition { Id = "property-grid-owner-second" },
            section: EditorPropertySection.General);
        Assert(ReferenceEquals(firstPropertyView.GetPropertyOwner(pd: null), firstPropertyView) &&
               ReferenceEquals(secondPropertyView.GetPropertyOwner(pd: null), secondPropertyView),
            "Canonical single-selection property views must expose a non-null PropertyGrid owner.");
        using (var propertyGrid = new PropertyGrid { PropertySort = PropertySort.NoSort })
        {
            propertyGrid.SelectedObject = firstPropertyView;
            propertyGrid.SelectedObject = secondPropertyView;
            Assert(ReferenceEquals(propertyGrid.SelectedObject, secondPropertyView),
                "PropertyGrid must replace canonical single-selection views without dereferencing a null custom-type owner.");
        }

        var legacyDashboardType = typeof(DashboardDefinition);
        var legacyWidgetType = typeof(WidgetDefinition);

        static IEnumerable<Type> SignatureTypes(System.Reflection.MethodBase member) =>
            member.GetParameters().Select(parameter => parameter.ParameterType);

        var rendererLegacySurface = typeof(DashboardRenderer)
            .GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            .SelectMany(SignatureTypes)
            .Any(type => type == legacyDashboardType);
        Assert(!rendererLegacySurface,
            "DashboardRenderer must not retain a schema-18 DashboardDefinition constructor bridge.");

        var outputLegacySurface = typeof(OutputSessionManager)
            .GetMembers(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            .OfType<System.Reflection.MethodBase>()
            .SelectMany(SignatureTypes)
            .Any(type => type == legacyDashboardType);
        Assert(!outputLegacySurface,
            "OutputSessionManager must not retain schema-18 dashboard constructor/reconfigure bridges.");

        var geometryLegacySurface = typeof(PinkieSysMon.Widgets.WidgetGeometry)
            .GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            .Concat(typeof(PinkieSysMon.Widgets.WidgetTransform)
                .GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
            .SelectMany(SignatureTypes)
            .Any(type => type == legacyWidgetType);
        Assert(!geometryLegacySurface,
            "Current widget geometry/render APIs must not expose schema-18 WidgetDefinition overloads.");
    }

    private static void CanonicalTypedWidgetSerialization()
    {
        var binary = new C.BinaryWidgetDefinition { Id = "typed-binary", Metric = "system.test.binary" };
        var power = new C.PowerWidgetDefinition { Id = "typed-power", PowerSource = PowerMetricContract.BatterySource };
        var mediaSystem = new C.MediaSystemWidgetDefinition { Id = "typed-media-system", MediaSource = MediaMetricContract.InputSource };
        var mediaPlayer = new C.MediaPlayerWidgetDefinition { Id = "typed-media-player" };

        var definition = new C.DashboardDefinition
        {
            Widgets =
            [
                new C.ValueWidgetDefinition
                {
                    Id = "typed-value",
                    SourceKind = C.ValueSourceKind.Text,
                    Text = "literal"
                },
                binary,
                new C.GaugeWidgetDefinition
                {
                    Id = "typed-gauge",
                    Metric = "system.test.gauge"
                },
                new C.BarWidgetDefinition
                {
                    Id = "typed-bar",
                    Metric = "system.test.bar",
                    ContentMode = BarImageContract.ContentModeFill,
                    Image = new C.BarImagePresentationDefinition
                    {
                        Source = "images\\dormant.png",
                        Fit = BarImageContract.ImageFitCover,
                        ProgressMode = BarImageContract.ProgressModeReveal,
                        Loop = false
                    }
                },
                new C.ImageWidgetDefinition
                {
                    Id = "typed-image",
                    Asset = new C.ImageAssetPresentationDefinition
                    {
                        SourceType = C.ImageAssetSourceType.Icon,
                        Source = "lucide:star",
                        Fit = StateVisualProfileContract.FitContain,
                        Loop = false
                    }
                },
                power,
                mediaSystem,
                mediaPlayer
            ]
        };

        var root = DashboardJson.SerializeToObjectUnchecked(definition);
        var widgets = root["widgets"]!.AsArray().Select(node => node!.AsObject()).ToArray();
        Assert(widgets.Length == 8,
            "Canonical serialization must persist all eight concrete widget types without a flat union placeholder.");

        JsonObject Find(string type) => widgets.Single(widget => widget["type"]!.GetValue<string>() == type);
        var value = Find(WidgetTypeContract.Value);
        var gauge = Find(WidgetTypeContract.Gauge);
        var bar = Find(WidgetTypeContract.Bar);
        var image = Find(WidgetTypeContract.Image);
        var powerJson = Find(WidgetTypeContract.Power);
        var mediaSystemJson = Find(WidgetTypeContract.MediaSystem);
        var mediaPlayerJson = Find(WidgetTypeContract.MediaPlayer);

        Assert(value.ContainsKey("sourceKind") && value.ContainsKey("textPresentation") &&
               !value.ContainsKey("valueSource") && !value.ContainsKey("fontFamily"),
            "Value serialization must use SourceKind/TextPresentation and must not emit schema-18 flat aliases.");
        Assert(gauge.ContainsKey("thresholds") && gauge.ContainsKey("track") && gauge.ContainsKey("needle") &&
               !gauge.ContainsKey("threshold1Value") && !gauge.ContainsKey("trackEnabled") && !gauge.ContainsKey("needlePointerLength"),
            "Gauge serialization must own thresholds, track and needle as canonical subobjects.");
        Assert(bar.ContainsKey("contentMode") && bar.ContainsKey("image") &&
               !bar.ContainsKey("barImageSource") && !bar.ContainsKey("barImageFit"),
            "Bar serialization must own its progress image through BarImagePresentationDefinition.");
        Assert(image.ContainsKey("asset") && !image.ContainsKey("sourceType") && !image.ContainsKey("source"),
            "Image serialization must own source identity through Asset.");
        Assert(powerJson.ContainsKey("powerSource") && !powerJson.ContainsKey("source"),
            "Power serialization must persist PowerSource instead of overloaded Source.");
        Assert(mediaSystemJson.ContainsKey("mediaSource") && !mediaSystemJson.ContainsKey("source"),
            "Media System serialization must persist MediaSource instead of overloaded Source.");
        Assert(!mediaPlayerJson.ContainsKey("source") && !mediaPlayerJson.ContainsKey("metric"),
            "Media Player must persist neither an overloaded Source nor its derived playback metric.");

        var removedUnionFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "valueSource", "sourceType", "barImageSource", "barImageFit", "barProgressMode",
            "threshold1Enabled", "threshold1Value", "threshold1Color",
            "threshold2Enabled", "threshold2Value", "threshold2Color",
            "threshold3Enabled", "threshold3Value", "threshold3Color",
            "trackEnabled", "trackBackgroundColor", "trackBorderColor", "trackBorderWidth", "trackCornerRadius",
            "needleEnabled", "needleThickness", "needleColor", "needleStartOffset", "needleEndOffset",
            "needlePointerLength", "needlePointerThickness", "needlePointerColor"
        };
        Assert(widgets.All(widget => widget.Select(pair => pair.Key).All(key => !removedUnionFields.Contains(key))),
            "Canonical typed serialization must not resurrect removed schema-18 flat-union fields.");
        Assert(widgets.All(widget => !widget.ContainsKey("Type")),
            "Canonical typed serialization must persist only the lowercase polymorphic 'type' discriminator, not the computed runtime Type property.");
    }

    private static void CanonicalSharedTextPresentationParity()
    {
        static C.TextPresentationDefinition Presentation() => new()
        {
            FontFamily = "Roboto",
            FontSize = 23f,
            FontWeight = 600,
            Italic = true,
            Align = "center",
            VerticalAlign = "middle",
            OutlineWidth = 2f,
            OutlineColor = "#FF112233",
            OverflowMode = ValueOverflowContract.Bump,
            ScrollSpeed = 37f,
            BumpPauseMs = 777
        };

        var value = new C.ValueWidgetDefinition
        {
            Id = "text-parity-value",
            SourceKind = C.ValueSourceKind.Text,
            Text = "value",
            TextPresentation = Presentation()
        };
        var state = new C.StateVisualProfileDefinition
        {
            ContentType = C.StateContentType.Value,
            TextPresentation = Presentation()
        };

        var direct = value.GetTextPresentation();
        var stateRender = state.ToRenderProfile().Text;
        Assert(direct == stateRender,
            "Value and state-value rendering must consume the same canonical TextPresentationDefinition semantics.");

        var dashboard = new C.DashboardDefinition
        {
            Widgets =
            [
                value,
                new C.BinaryWidgetDefinition
                {
                    Id = "text-parity-binary",
                    Metric = RuntimeMetricContract.Fps,
                    Profiles = new Dictionary<string, C.StateVisualProfileDefinition>(StringComparer.OrdinalIgnoreCase)
                    {
                        [BinarySignalContract.TrueKey] = state
                    }
                }
            ]
        };
        var root = DashboardJson.SerializeToObjectUnchecked(dashboard);
        var widgets = root["widgets"]!.AsArray();
        var valueText = widgets[0]!["textPresentation"]!.AsObject();
        var stateText = widgets[1]!["profiles"]![BinarySignalContract.TrueKey]!["textPresentation"]!.AsObject();
        Assert(valueText.Select(pair => pair.Key).OrderBy(key => key).SequenceEqual(
                   stateText.Select(pair => pair.Key).OrderBy(key => key), StringComparer.Ordinal),
            "Value and state-value JSON must expose the same canonical text-presentation field set.");
    }

    private static void CanonicalImageSourceKindTransitionClearsSource()
    {
        static void AssertBothDirections(C.ImageAssetPresentationDefinition asset, string context)
        {
            asset.SourceType = C.ImageAssetSourceType.File;
            asset.Source = "images\\example.png";
            asset.ChangeSourceType(C.ImageAssetSourceType.Icon);
            Assert(C.ImageAssetSourceType.IsIcon(asset.SourceType) && asset.Source is null,
                $"{context} file -> icon transition must clear Source.");

            asset.Source = "lucide:star";
            asset.ChangeSourceType(C.ImageAssetSourceType.File);
            Assert(C.ImageAssetSourceType.IsFile(asset.SourceType) && asset.Source is null,
                $"{context} icon -> file transition must clear Source.");
        }

        var image = new C.ImageWidgetDefinition { Id = "transition-image" };
        AssertBothDirections(image.Asset, "Image widget");

        var canvas = new C.CanvasDefinition();
        AssertBothDirections(canvas.BackgroundImage.Asset, "Canvas image layer");

        var profile = new C.StateVisualProfileDefinition { ContentType = C.StateContentType.Image };
        AssertBothDirections(profile.Asset, "State image profile");
    }

    private static PinkieSysMon.DashboardModel.DashboardDefinition CreateMinimalCanonicalDashboard() => new()
    {
        SchemaVersion = DashboardSchemaMigration.PropertyModelNormalizationSchemaVersion,
        Widgets =
        [
            new PinkieSysMon.DashboardModel.ValueWidgetDefinition
            {
                Id = "value-round-trip",
                Width = 0,
                Height = 0,
                SourceKind = PinkieSysMon.DashboardModel.ValueSourceKind.Text,
                Text = "round-trip"
            }
        ]
    };

    private static void Assert([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertThrows<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private sealed class BlockingTrofeoTestTransport : ITrofeoFrameTransport
    {
        private readonly ManualResetEventSlim? _sendStarted;
        private readonly ManualResetEventSlim? _allowSend;
        private int _sendCount;
        private int _disposeCount;

        public BlockingTrofeoTestTransport(
            ManualResetEventSlim? sendStarted = null,
            ManualResetEventSlim? allowSend = null)
        {
            _sendStarted = sendStarted;
            _allowSend = allowSend;
        }

        public int WireRotationDegrees => 0;
        public int SendCount => Volatile.Read(ref _sendCount);
        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public void SendJpegWithDiagnostics(
            ReadOnlySpan<byte> jpeg,
            TrofeoFrameRenderDiagnostics diagnostics)
        {
            if (DisposeCount != 0)
                throw new InvalidOperationException("Test transport was disposed before sending.");
            Interlocked.Increment(ref _sendCount);
            _sendStarted?.Set();
            if (_allowSend is not null && !_allowSend.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("Test send was not released.");
            if (DisposeCount != 0)
                throw new InvalidOperationException("Test transport was disposed during an active send.");
        }

        public void RecordLifecycle(string state, string reason) { }
        public void Dispose() => Interlocked.Increment(ref _disposeCount);
    }

    private sealed class FakeOutputSession : IOutputSession
    {
        public FakeOutputSession(string targetId, string targetName)
        {
            TargetId = targetId;
            TargetName = targetName;
        }

        public string TargetId { get; }
        public string TargetName { get; private set; }
        public bool IsConnected { get; set; }
        public long SentFrames { get; set; }
        public double LastFrameMs { get; set; }
        public int LastJpegBytes { get; set; }
        public string UsbState { get; set; } = "WAITING";
        public bool PublishesRuntimeMetrics { get; private set; }
        public bool ThrowOnStart { get; set; }
        public bool ThrowOnPrepare { get; set; }
        public bool ThrowOnNextCommit { get; set; }
        public bool ThrowOnSuspend { get; set; }
        public bool ThrowOnResume { get; set; }
        public bool ThrowOnDispose { get; set; }
        public int StartCount { get; private set; }
        public int PrepareCount { get; private set; }
        public int CommitCount { get; private set; }
        public int SuspendCount { get; private set; }
        public int ResumeCount { get; private set; }
        public int DisposeCount { get; private set; }

        public void Start()
        {
            StartCount++;
            if (ThrowOnStart)
                throw new InvalidOperationException("Synthetic output-session startup failure.");
        }

        public IOutputSessionReconfiguration PrepareReconfiguration(
            AppConfig config,
            OutputTargetConfig target,
            PinkieSysMon.DashboardModel.DashboardDefinition dashboard)
        {
            PrepareCount++;
            if (ThrowOnPrepare)
                throw new InvalidOperationException("Synthetic output-session preparation failure.");
            return new FakeOutputSessionReconfiguration(target.Name);
        }

        public void CommitReconfiguration(IOutputSessionReconfiguration reconfiguration)
        {
            var prepared = reconfiguration as FakeOutputSessionReconfiguration
                ?? throw new InvalidOperationException("Unexpected fake reconfiguration object.");
            TargetName = prepared.TargetName;
            CommitCount++;

            if (ThrowOnNextCommit)
            {
                ThrowOnNextCommit = false;
                throw new InvalidOperationException("Synthetic output-session commit failure after mutation.");
            }
        }

        public void SetRuntimeMetricsPublisher(bool enabled) => PublishesRuntimeMetrics = enabled;

        public void Suspend()
        {
            SuspendCount++;
            if (ThrowOnSuspend)
                throw new InvalidOperationException("Synthetic output-session suspend failure.");
        }

        public void Resume()
        {
            ResumeCount++;
            if (ThrowOnResume)
                throw new InvalidOperationException("Synthetic output-session resume failure.");
        }

        public void Dispose()
        {
            DisposeCount++;
            if (ThrowOnDispose)
                throw new InvalidOperationException("Synthetic output-session disposal failure.");
        }
    }

    private sealed class FakeOutputSessionReconfiguration(string targetName) : IOutputSessionReconfiguration
    {
        public string TargetName { get; } = targetName;
        public void Dispose() { }
    }

    private sealed class MultiEditTestBox
    {
        public int Value { get; set; }
    }

    private sealed class MultiEditTestPropertyDescriptor : PropertyDescriptor
    {
        private readonly MultiEditTestBox _target;
        private readonly bool _throwsOnSet;

        public MultiEditTestPropertyDescriptor(MultiEditTestBox target, bool throwsOnSet)
            : base("Value", null)
        {
            _target = target;
            _throwsOnSet = throwsOnSet;
        }

        public override Type ComponentType => typeof(MultiEditTestBox);
        public override bool IsReadOnly => false;
        public override Type PropertyType => typeof(int);
        public override bool CanResetValue(object component) => false;
        public override object GetValue(object? component) => _target.Value;
        public override void ResetValue(object component) { }
        public override void SetValue(object? component, object? value)
        {
            var converted = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
            _target.Value = converted;
            if (_throwsOnSet && converted == 9)
                throw new InvalidOperationException("Synthetic multi-edit failure after mutation.");
        }
        public override bool ShouldSerializeValue(object component) => false;
    }

    private sealed class CountingWidgetRenderer(string type) : PinkieSysMon.Widgets.IWidgetRenderer
    {
        public string Type { get; } = type;
        public int RenderCount { get; private set; }

        public void Render(
            SkiaSharp.SKCanvas canvas,
            PinkieSysMon.DashboardModel.WidgetDefinition widget,
            PinkieSysMon.Widgets.WidgetRenderContext context)
        {
            RenderCount++;
        }
    }

    private sealed class SolidBoxCountingWidgetRenderer(string type) : PinkieSysMon.Widgets.IWidgetRenderer, IDisposable
    {
        private readonly SkiaSharp.SKPaint _paint = new()
        {
            Color = SkiaSharp.SKColors.White,
            IsAntialias = false,
            Style = SkiaSharp.SKPaintStyle.Fill
        };

        public string Type { get; } = type;
        public int RenderCount { get; private set; }

        public void Render(
            SkiaSharp.SKCanvas canvas,
            PinkieSysMon.DashboardModel.WidgetDefinition widget,
            PinkieSysMon.Widgets.WidgetRenderContext context)
        {
            RenderCount++;
            canvas.DrawRect(
                new SkiaSharp.SKRect(widget.X, widget.Y, widget.X + widget.Width, widget.Y + widget.Height),
                _paint);
        }

        public void Dispose() => _paint.Dispose();
    }

    private sealed class ThrowingMetricSource(string providerId, string name, string metricName) : IMetricSource
    {
        public string ProviderId { get; } = providerId;
        public string Name { get; } = name;
        public IReadOnlyCollection<string> MetricNames { get; } = new[] { metricName };
        public IReadOnlyDictionary<string, object?> Capture() => throw new InvalidOperationException("Synthetic provider failure.");
        public IReadOnlyDictionary<string, object?> Capture(IReadOnlyCollection<string> requestedMetrics) => Capture();
    }

    private sealed class ConstantMetricSource(string providerId, string name, string metricName, object? value) : IMetricSource
    {
        public string ProviderId { get; } = providerId;
        public string Name { get; } = name;
        public IReadOnlyCollection<string> MetricNames { get; } = new[] { metricName };
        public IReadOnlyDictionary<string, object?> Capture() =>
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [metricName] = value };
        public IReadOnlyDictionary<string, object?> Capture(IReadOnlyCollection<string> requestedMetrics) => Capture();
    }
}
