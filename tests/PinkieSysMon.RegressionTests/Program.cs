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
    private const string FixtureOutputFps = "system.output.desktop-1.fps";

    [STAThread]
    private static int Main(string[] args)
    {
        if (Issue105LoggingTests.TryRunChild(args) is { } loggingChildExit)
            return loggingChildExit;
        if (TurzxRuntimeDiagnostic.TryRun(args, Console.WriteLine) is { } runtimeCheckExit)
            return runtimeCheckExit;
        if (TurzxTransportDiagnostic.TryRun(args, Console.WriteLine) is { } transportExit)
            return transportExit;
        if (args.Length == 1 && args[0] == "--issue-106-allocation-probe")
        {
            try
            {
                GraphHistoryTests.BudgetValidationAndAllocationBounds();
                Console.WriteLine("Issue #106 allocation probe: PASS");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Issue #106 allocation probe: FAIL: {ex}");
                return 1;
            }
        }
        if (TurzxPassiveDiagnostic.TryRun(args, Console.WriteLine) is { } diagnosticExit)
            return diagnosticExit;
        var testGroups = new (string Group, (string Name, Action Run)[] Cases)[]
        {
            ("Dashboard / Defaults", [
                ("new dashboard creation and save/reopen", Issue93NewDashboardTests.CreateSaveAndReopen),
                ("adaptive dashboard geometry and semantics", Issue94AdaptiveDashboardTests.GeneratorGeometryAndSemantics),
                ("infeasible canvas rejection", Issue94AdaptiveDashboardTests.RejectInfeasibleGeometry),
                ("Desktop default staged assignment", Issue94DesktopDefaultAssignmentTests.StagingAndReuse),
                ("Desktop default rollback and collisions", Issue94DesktopDefaultAssignmentTests.RollbackAndCollisions),
                ("user-defined physical board naming", Issue95PhysicalDashboardNamesTests.UserGeometryNaming),
                ("integrated physical native default assignment", Issue94PhysicalDefaultAssignmentTests.IntegratedProfilesAndReuse),
                ("physical default rollback and unprofiled adapter", Issue94PhysicalDefaultAssignmentTests.FailureAndUnsupportedProfiles),
                ("first Desktop bootstrap and missing-file protection", Issue94FreshBootstrapTests.FirstDesktopAndNoRepair),
                ("first physical bootstrap and safe rollback", Issue94FreshBootstrapTests.PhysicalFirstAndCancel),
            ]),
            ("Dashboard / Geometry", [
                ("Dashboard universal size schema", DashboardUniversalSizeSchema),
            ]),
            ("Dashboard / Model & Canvas", [
                ("Shadow 31→32 migration, defaults and idempotence", Issue116ShadowMigrationTests.SchemaMigrationAndRoundTrip),
                ("Shadow Panel properties and Desktop RGBA alpha", Issue116ShadowMigrationTests.PropertyContractAndDesktopAlpha),
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
            ]),
            ("Widgets / Calculator", [
                ("arithmetic, entry and error handling", CalculatorWidgetTests.ArithmeticAndErrorRecovery),
                ("clipboard, invalid input and state isolation", CalculatorWidgetTests.ClipboardAndIsolation),
                ("Desktop key routing, rotation and gesture cancellation", CalculatorWidgetTests.InteractionAndGeometry),
                ("rounded Calculator fills without borders", CalculatorWidgetTests.BorderlessCornerRadiusRendering),
                ("schema migration, persistence and Editor property tabs", CalculatorWidgetTests.PersistenceAndEditorProperties),
            ]),
            ("Widgets / Bar", [
                ("Bar image property visibility", BarImagePropertyVisibility),
                ("Bar image progress modes", BarImageProgressModes),
                ("Bar image fit modes", BarImageFitModes),
                ("Bar track color, migration and insets", BarTrackColorAndInsets),
                ("Bar foreground color rendering", BarForegroundColorRendering),
            ]),
            ("Widgets / Binary", [
                ("Binary numeric truth semantics", BinaryNumericTruthSemantics),
                ("Binary setpoint semantics", BinarySetpointSemantics),
                ("numeric lists, intervals and all operators", Issue89BinarySetpointTests.NumericPatterns),
                ("invalid numeric expressions and unavailable values", Issue89BinarySetpointTests.InvalidPatterns),
                ("Editor field and schema round-trip", Issue89BinarySetpointTests.EditorAndSerialization),
                ("numeric units, DateTime and Auto states", Issue89BinarySetpointTests.RenderContextAndLegacyTypes),
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
                ("Binary state image schema", BinaryStateImageSchema),
                ("Binary state animation playback", BinaryStateAnimationPlayback),
                ("Binary state text schema", BinaryStateTextSchema),
                ("Binary state text independence", BinaryStateTextIndependence),
                ("Binary state text rendering", BinaryStateTextRendering),
                ("Binary schema 10 migration", BinarySchema10Migration),
            ]),
            ("Widgets / Gauge", [
                ("Gauge universal container effects", GaugeUsesUniversalContainerEffects),
                ("Gauge needle offsets and pointer", GaugeNeedleOffsetsAndPointer),
                ("Gauge needle above track", GaugeNeedleRendersAboveTrack),
            ]),
            ("Widgets / Graph", [
                ("Graph canonical type and obsolete global fields", GraphCanonicalTypeAndObsoleteFields),
                ("Graph canonical schema and legacy compatibility", GraphWidgetTests.SchemaAndLegacyCompatibility),
                ("Graph ranges and absolute threshold colors", GraphWidgetTests.RangesAndAbsoluteThresholdColors),
                ("Graph Line Columns Auto Fixed threshold render matrix", GraphWidgetTests.RenderingModeScaleThresholdMatrix),
                ("Graph gaps expiry clipping and dense spikes", GraphWidgetTests.GapsExpiryClippingAndDenseSpikes),
                ("Graph fill alpha and common image composition", GraphWidgetTests.FillAlphaAndCommonImageComposition),
                ("Graph geometry reuse and Runtime Editor parity", GraphWidgetTests.GeometryReuseAndRuntimeEditorParity),
                ("Graph reverse time direction and background edits", GraphWidgetTests.ReverseTimeDirectionAndBackgroundEdits),
                ("Graph interpolation and filter schema", GraphSignalTests.SchemaAndDefaults),
                ("Graph causal filter values and finite arithmetic", GraphSignalTests.FiltersAndFiniteArithmetic),
                ("Graph filter publication cache and lifecycle", GraphSignalTests.PublicationCacheLifecycle),
                ("Graph monotone Bezier geometry and clipping", GraphSignalTests.BezierGeometry),
                ("Graph Bezier and filtering raster integration", GraphWidgetTests.SignalRendering),
                ("Graph Editor common properties and applicability", GraphEditorTests.CommonPropertiesAndGraphApplicability),
                ("Graph Editor clipboard image and undo snapshots", GraphEditorTests.ClipboardImageImportAndCanonicalUndoSnapshots),
                ("Graph Editor shared background image bindings", GraphEditorTests.BackgroundImageSharedPropertyBindings),
            ]),
            ("Widgets / Media Player", [
                ("Media Player state property hierarchy", MediaPlayerStatePropertyHierarchy),
                ("Media Player state presentation schema", MediaPlayerStatePresentationSchema),
                ("Media Player state text rendering", MediaPlayerStateTextRendering),
                ("Media Player schema 14 migration", MediaPlayerSchema14Migration),
            ]),
            ("Widgets / Media System", [
                ("Media System state property hierarchy", MediaSystemStatePropertyHierarchy),
                ("Media System state presentation schema", MediaSystemStatePresentationSchema),
                ("Media System state text rendering", MediaSystemStateTextRendering),
                ("Media System schema 15 migration", MediaSystemSchema15Migration),
            ]),
            ("Widgets / Power", [
                ("Power state property hierarchy", PowerStatePropertyHierarchy),
                ("Power state presentation schema", PowerStatePresentationSchema),
                ("Power state text rendering", PowerStateTextRendering),
                ("Power schema 13 migration", PowerSchema13Migration),
            ]),
            ("Widgets / Shared DateTime", [
                ("DateTime component projection, editor and cache", DateTimeComponentTests.ProjectionEditorAndCache),
                ("schema 19 migration and quantitative compatibility", DateTimeComponentTests.SchemaMigrationAndCompatibility),
                ("12-cycle numeric projection and boundaries", Issue107HourCycleTests.ProjectionAndBoundaries),
                ("12-cycle Editor and widget consumers", Issue107HourCycleTests.EditorAndConsumers),
                ("12-cycle persisted schema compatibility", Issue107HourCycleTests.SchemaCompatibility),
                ("Binary DateTime evaluation and Editor component", Issue81BinaryDateTimeTests.EvaluationAndEditor),
                ("Binary DateTime schema 20 migration", Issue81BinaryDateTimeTests.SchemaMigration),
            ]),
            ("Widgets / Shared Images", [
                ("Editor missing and corrupt artwork recovery", Issue105EditorLoggingTests.MissingAndCorruptArtworkRecovery),
                ("empty image source validation and render", Issue70EmptyImageSourceTests.CanonicalAndRendering),
            ]),
            ("Widgets / Shared Properties", [
                ("Widget source property hierarchy", WidgetSourcePropertyHierarchy),
                ("common widget property order and bindings", CommonWidgetPropertyTests.OrderAndBindings),
                ("Issue #117 Image content source placement and switching", Issue117ImageSourcePropertyTests.PlacementAndSwitching),
                ("Issue #117 Image mixed selection and persisted source", Issue117ImageSourcePropertyTests.MultiSelectionAndSerialization),
                ("common padding parsing and atomic edits", CommonWidgetPropertyTests.PaddingEdits),
                ("Issue #112 common panel and contextual image controls", Issue112PanelTests.CommonLayerContractAndContext),
                ("Issue #112 migration through schema 30 and idempotence", Issue112PanelTests.MigrationAndRoundTrip),
                ("Issue #113 Canvas schema 29→30 migration and idempotence", Issue113CanvasPanelTests.SchemaMigrationAndIdempotence),
                ("Issue #113 Canvas Editor common Panel contract", Issue113CanvasPanelTests.EditorPanelProperties),
                ("Issue #113 Canvas color/border/alpha and physical shadow isolation", Issue113CanvasPanelTests.DrawOrderAlphaAndPhysicalIsolation),
                ("Issue #112 three-layer alpha rendering", Issue112PanelTests.ThreeLayerRenderingAndFileAlpha),
                ("Universal widget property hierarchy", UniversalWidgetPropertyHierarchy),
                ("Appearance property hierarchy", AppearancePropertyHierarchy),
                ("Data property hierarchy", DataPropertyHierarchy),
                ("Gauge property hierarchy", GaugePropertyHierarchy),
                ("States property hierarchy", StatesPropertyHierarchy),
                ("Image property hierarchy", ImagePropertyHierarchy),
            ]),
            ("Widgets / Shared Quantitative", [
                ("Quantitative foreground color schema", QuantitativeForegroundColorSchema),
            ]),
            ("Widgets / Shared Rendering", [
                ("Static state-file animation classification", StaticStateFileAnimationClassification),
                ("Stateful widget shadow-cache animation contract", StatefulWidgetShadowCacheAnimationContract),
                ("PASS 9F stateful renderer unification contract", Pass9FStatefulRendererUnificationContract),
            ]),
            ("Widgets / Shared Schema", [
                ("Removed legacy widget types", RemovedLegacyWidgetTypes),
                ("Text property hierarchy", TextPropertyHierarchy),
            ]),
            ("Widgets / Action", [
                ("universal model, migration and strict legacy rejection", WidgetActionTests.ModelMigrationAndLegacyRejection),
                ("Action tab descriptors and cross-widget multi-edit", WidgetActionTests.EditorPropertiesAndMultiEdit),
                ("inactive, overlapping and rotated Action hotspot geometry", WidgetActionTests.InactiveHotspotsAndGeometry),
                ("derived Text/Value native hotspot refresh", WidgetActionTests.DerivedValueGeometryAndRefresh),
                ("region rasterization, clipping and immutable snapshots", WidgetActionInteractionTests.RasterAndSnapshot),
                ("native gesture rules and Shell boundary", WidgetActionInteractionTests.GesturesAndShellBoundary),
                ("native hand cursor only inside Action regions", WidgetActionInteractionTests.DesktopHoverCursorRouting),
                ("common geometry migration and history", CommonWidgetGeometryTests.MigrationAndHistory),
                ("common border masks and padded text baseline", CommonWidgetGeometryTests.RenderBordersAndPadding),
                ("padded Gauge fit and resize limits", CommonWidgetGeometryTests.GaugeFit),
            ]),
            ("Widgets / Value", [
                ("Text/value source semantics", TextValueSourceSemantics),
                ("Text/value property visibility", TextValuePropertyVisibility),
                ("Literal numeric data semantics", LiteralNumericDataSemantics),
                ("Literal numeric unit options follow source unit", LiteralNumericUnitOptionsFollowSourceUnit),
                ("Literal numeric dashboard normalization", LiteralNumericDashboardNormalization),
                ("Text overflow state transitions", TextOverflowStateTransitions),
                ("Text overflow dashboard normalization", TextOverflowDashboardNormalization),
            ]),
            ("Editor / Devices", [
                ("connected TURZX Settings controls", Issue71SettingsTests.ConnectedSettingsControls),
                ("default physical factory and sibling isolation", Issue71SettingsTests.DefaultPhysicalFactoryAndSiblingIsolation),
                ("absent TURZX Settings and tray", Issue71SettingsTests.AbsentSettingsAndTrayRemainReadOnly),
                ("default factory rejects mismatched adapter", Issue71SettingsTests.DefaultFactoryRejectsMismatchedTransport),
                ("read-only tabs and independent discovery failures", Issue77DevicesUiTests.ReadOnlyTabsAndIndependentFailures),
                ("Device Settings discovery and explicit adoption", DeviceSettingsStage23Tests.DiscoveryAndAdoption),
                ("Device Settings consent and isolated persistence", DeviceSettingsStage23Tests.ConsentAndPersistence),
                ("Device Settings disconnected and ambiguous UI", DeviceSettingsStage23Tests.LiveStatusControls),
                ("Settings apply rollback and stale-file safety", DeviceSettingsStage23Tests.SettingsApplyRecovery),
            ]),
            ("Editor / Diagnostics", [
                ("Editor edit identities and property privacy", Issue105EditorLoggingTests.EditIdentitiesAndPrivacy),
                ("Editor gesture checkpoint and no-op", Issue105EditorLoggingTests.GestureCheckpointAndNoOp),
                ("Editor hierarchy and paste correlation", Issue105EditorLoggingTests.HierarchyAndPasteCorrelation),
                ("Editor edit evidence bounds", Issue105EditorLoggingTests.EditEvidenceBounds),
                ("operation outcomes and parent correlation", Issue105EditorLoggingTests.OperationOutcomesAndCorrelation),
                ("per-incident recovery flush", Issue105EditorLoggingTests.RecoveryFlushPreservesOtherIncidents),
                ("Editor load save and history evidence", Issue105EditorLoggingTests.LoadSaveAndHistory),
                ("Editor locked JSON save evidence", Issue105EditorLoggingTests.LockedJsonSaveFailure),
                ("Editor metadata partial save evidence", Issue105EditorLoggingTests.MetadataPartialSave),
                ("Editor failed load preserves document", Issue105EditorLoggingTests.FailedLoadPreservesDocument),
                ("Editor invalid metadata backup evidence", Issue105EditorLoggingTests.InvalidMetadataLoadBackup),
            ]),
            ("Editor / Documents", [
                ("shared Editor settings concurrent merge", Issue70ConcurrentEditorSettingsTests.MergesDashboardViews),
                ("Editor save with an open settings snapshot", Issue70ConcurrentEditorSettingsTests.SaveWithOpenSnapshot),
                ("widget clipboard package version and payload", Issue70ClipboardTests.PackageRoundTrip),
                ("nested widget paste plan and Save/reopen", Issue70PastePlannerTests.TransfersNestedGroups),
                ("malformed group paste isolation", Issue70PastePlannerTests.RejectsMalformedMappingsWithoutMutation),
                ("decline image import preserves properties", Issue70ImageImportTests.DeclinePreservesWidgetConfigurations),
                ("collision and identical image reuse", Issue70ImageImportTests.ImportAndCollisionChoices),
                ("cancel and invalid image path safety", Issue70ImageImportTests.CancelAndInvalidPaths),
                ("startup selection and theme-restart arguments", Issue84EditorStartupTests.SelectionAndRestartArguments),
                ("document-free Editor commands", Issue84EditorStartupTests.EmptyWorkspaceControls),
                ("current-schema load and clean switching", Issue102EditorDirtyTests.CleanLoadAndSwitch),
                ("migration remains dirty until saved", Issue102EditorDirtyTests.MigrationAndSave),
                ("normalization remains dirty through undo", Issue102EditorDirtyTests.NormalizationAndUndo),
                ("user edits undo redo and Save", Issue102EditorDirtyTests.EditUndoRedoAndSave),
                ("middle-mouse canvas pan delta and scroll bounds", Issue98CanvasPanTests.DeltaAndBounds),
            ]),
            ("Editor / Graph Preview", [
                ("Graph preview coalescing and terminal source ownership", GraphPreviewLifecycleTests.DiscoveryCoalescingAndTerminalShutdown),
                ("Graph preview close fence and Runtime isolation", GraphPreviewLifecycleTests.BlockedCaptureCloseFenceAndIsolation),
                ("Graph preview stale discovery and illustrative Output history", GraphPreviewLifecycleTests.StaleDiscoveryAndIllustrativeOutputHistory),
            ]),
            ("Editor / Output Control", [
                ("dirty document Save, Don't Save, Cancel and failed Save", EditorOutputStartStage25Tests.DirtyDocumentDecisions),
                ("selected Output dashboard choice and isolation", EditorOutputStartStage25Tests.SelectedDashboardChoices),
                ("absent Runtime and normal launch policy", EditorOutputStartStage25Tests.AbsentRuntimePolicy),
                ("failed apply and observed completion", EditorOutputStartStage25Tests.FailuresAndVerification),
                ("All choice preflight and partial results", EditorOutputStartStage25Tests.AllPlanningAndPartialResults),
                ("legacy status, assignment drift and menu ownership", EditorOutputStartStage25Tests.LegacyDriftAndMenu),
                ("Editor Outputs grouping and refresh", EditorOutputStartStage25Tests.GroupedOutputMenu),
            ]),
            ("Editor / Platform & Properties", [
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
            ]),
            ("Editor / Properties", [
                ("bundled font selection for all text presentations", FontFamilySelectionTests.BundledFontOptions),
                ("mixed multi-selection dropdown labels", MultiSelectionDropdownTests.MixedValueOptionLabels),
                ("single-widget and canvas property history checkpoint", Issue80PropertyUndoTests.SingleWidgetAndCanvasCapture),
                ("multi-widget property history checkpoint", Issue80PropertyUndoTests.MultiWidgetCapture),
            ]),
            ("Editor / Properties & Layout", [
                ("All widget properties assigned to explicit tabs", AllWidgetPropertiesAssignedToExplicitTabs),
                ("Property expansion state persistence", PropertyExpansionStatePersistence),
                ("Editor input focus ownership", EditorInputFocusOwnership),
                ("Layer tree pointer focus and inactive selection contract", LayerTreePointerFocusAndInactiveSelectionContract),
                ("Editor geometry whole-pixel normalization", EditorGeometryWholePixelNormalization),
                ("Layer tree viewport anchor persistence", LayerTreeViewportAnchorPersistence),
                ("Grid split-button behavior", GridSplitButtonBehavior),
            ]),
            ("Editor / Property Controls", [
                ("Property applicability contract", PropertyApplicabilityContract),
                ("Context-disabled custom editors", ContextDisabledCustomEditors),
                ("Date/time format editor", DateTimeFormatEditorContract),
                ("Global icon format discovery and capabilities", GlobalIconFormatDiscoveryAndCapabilities),
                ("Global icon Windows-safe logical names", GlobalIconWindowsSafeLogicalNames),
                ("Windows path-component validation contract", WindowsPathComponentValidationContract),
                ("Global icon rendering and applicability", GlobalIconRenderingAndApplicability),
                ("Multi-selection common property editing", MultiSelectionCommonPropertyEditing),
                ("Multi-selection applicability and context", MultiSelectionApplicabilityAndContext),
            ]),
            ("Editor / Settings", [
                ("Settings persistence milestones and conflict", Issue105SettingsLoggingTests.PersistenceMilestonesAndConflict),
                ("Settings recovery and concurrent-writer evidence", Issue105SettingsLoggingTests.RecoveryAndConcurrentWriterEvidence),
                ("Settings physical card identity and privacy", Issue105SettingsLoggingTests.PhysicalCardIdentityAndPrivacy),
                ("Settings Remove partial and absent evidence", Issue105SettingsLoggingTests.RemovalPartialAndAbsentEvidence),
            ]),
            ("Editor / Shell", [
                ("Editor main-menu Group cleanup", EditorMainMenuGroupCleanup),
                ("Editor Add Widget menu grouping and dispatch (#120)", Issue120AddWidgetMenuContract),
                ("Editor Outputs toolbar right inset", EditorOutputsToolbarRightInset),
                ("Editor native title-bar layout contract", EditorWindowChromeHitTestContract),
                ("Editor title-bar native ownership contract", EditorTitleBarCaptionVisualExclusion),
                ("Editor title-bar menu integration", EditorTitleBarMenuIntegration),
                ("Editor title-bar vertical alignment", EditorTitleBarVerticalAlignment),
                ("Editor title-bar menu theme contract", EditorTitleBarMenuThemeContract),
                ("Editor theme settings contract", EditorThemeSettingsContract),
                ("General Settings app-wide isolation", EditorGeneralSettingsContract),
                ("Desktop Settings master-detail and lifecycle policy", EditorDesktopSettingsContract),
            ]),
            ("Telemetry / Connectivity", [
                ("Windows internet connectivity metric", WindowsInternetConnectivityMetric),
            ]),
            ("Telemetry / Diagnostics", [
                ("telemetry capture availability and recovery", Issue105TelemetryLoggingTests.CaptureUnavailableRecoveryAndFirstPublication),
                ("telemetry pending worker and queue totals", Issue105TelemetryLoggingTests.PendingWorkerAndQueueCounts),
                ("iCUE freshness file and catalog generations", Issue105TelemetryLoggingTests.IcueFreshnessFileAndCatalogGenerations),
                ("network offline versus query failure", Issue105TelemetryLoggingTests.NetworkOfflineFailureAndRecovery),
            ]),
            ("Telemetry / Graph Publication", [
                ("Graph telemetry publication and serialized discovery", GraphTelemetryTests.PublicationAndSerializedDiscovery),
                ("Graph telemetry pause and generation fences", GraphTelemetryTests.PausedCaptureAndSemanticGenerationFences),
                ("Graph catalog meaning and direct Output publication", GraphTelemetryTests.CatalogMeaningAndDynamicPublication),
            ]),
            ("Telemetry / History", [
                ("Graph history publication and immutable snapshots", GraphHistoryTests.PublicationsAndImmutableSnapshots),
                ("Graph history demand prepare commit and release", GraphHistoryTests.DemandPrepareCommitAndRelease),
                ("Graph history gaps cadence and pause boundaries", GraphHistoryTests.GapsCadenceAndPauseBoundaries),
                ("Graph history window predecessor and capacity", GraphHistoryTests.WindowPredecessorAndCapacity),
                ("Graph history raw temporal values and semantic epochs", GraphHistoryTests.RawTemporalValuesAndSemanticEpochs),
                ("Graph history budget validation and allocation bounds", GraphHistoryTests.BudgetValidationAndAllocationBounds),
                ("Graph history concurrent readers writers and retirement", GraphHistoryTests.ConcurrentWritersReadersAndRetirement),
            ]),
            ("Telemetry / Output Metrics", [
                ("Output telemetry demand active union", OutputTelemetryDemandTests.ActiveUnion),
                ("Output telemetry demand activation and clearing", OutputTelemetryDemandTests.Transitions),
                ("Output telemetry demand independent Desktop switches", OutputTelemetryDemandTests.DesktopPolicy),
                ("Output metric identity and physical catalog", OutputPerformanceMetricTests.IdentityAndCatalog),
                ("Output metric multi-session isolation", OutputPerformanceMetricTests.PublisherIsolation),
                ("Output metric clearing stop and restart", OutputPerformanceMetricTests.StopAndRestart),
            ]),
            ("Telemetry / Output Providers", [
                ("Dynamic output provider registration", OutputProviderStage07Tests.ProviderCatalog),
                ("Dynamic output selector branches and switches", OutputProviderStage07Tests.Selector),
                ("Output provider passive status and removal", OutputProviderStage07Tests.PassiveLifecycle),
                ("Physical USB inactive-session fallback and Binary", OutputProviderStage07Tests.PhysicalUsbFallbackLifecycle),
                ("Output provider active isolation without System", OutputProviderStage07Tests.LiveIsolation),
            ]),
            ("Telemetry / Providers", [
                ("Telemetry provider failure isolation", TelemetryProviderFailureIsIsolated),
                ("PC catalog, types and selector", Issue92PcTelemetryTests.CatalogAndSelector),
                ("PC installed memory and architecture values", Issue92PcTelemetryTests.ValueNormalization),
                ("PC static snapshot demand and unavailable isolation", Issue92PcTelemetryTests.StaticSnapshotAndIsolation),
                ("Telemetry reconfiguration clears deactivated metrics", TelemetryReconfigurationClearsDeactivatedMetrics),
                ("initial telemetry capture notification and wake", OutputTelemetryDemandTests.InitialCaptureNotifiesOnce),
                ("Telemetry reconfiguration prepare/commit isolation", TelemetryReconfigurationPrepareCommitIsolation),
            ]),
            ("Runtime / Configuration & Reload", [
                ("Settings Runtime phases and recovery", SavedConfigurationReloadTests.LoggingPhasesAndRecovery),
                ("application root without dashboard folder", RuntimeConfigurationStartupTests.RootWithoutDashboardDirectory),
                ("Runtime config clean desktop-only bootstrap", RuntimeConfigurationStartupTests.DesktopOnly),
                ("Runtime config mixed enablement and reload", RuntimeConfigurationStartupTests.MixedTargets),
                ("Runtime config invalid dashboard and malformed input", RuntimeConfigurationStartupTests.InvalidInput),
                ("selected saved settings and shared-board isolation", SavedOutputReloadTests.SavedSettingsIsolation),
                ("physical inherited settings and reload ownership", SavedOutputReloadTests.PhysicalDefaultsAndOwnership),
                ("selected reload validation and read-only failures", SavedOutputReloadTests.ValidationAndReadOnlyFailures),
                ("selected reload rollback and concurrent writes", SavedOutputReloadTests.RollbackAndConcurrentWrite),
                ("selected reload global lifetime and feedback", SavedOutputReloadTests.LifecycleAndFeedback),
                ("All common settings and enabled dashboard isolation", SavedConfigurationReloadTests.CommonSettingsAndEnabledDocuments),
                ("All empty enabled set and optional telemetry defaults", SavedConfigurationReloadTests.EmptyEnabledSetAndMissingTelemetry),
                ("All full-record validation before mutation", SavedConfigurationReloadTests.ValidationBeforeMutation),
                ("disabled startup documents and explicit enable", SavedConfigurationReloadTests.DisabledStartupAndExplicitEnable),
                ("All common-subsystem rollback and recovery failures", SavedConfigurationReloadTests.RollbackAcrossSubsystems),
                ("All intervening app and telemetry writes", SavedConfigurationReloadTests.InterveningConfigurationWrites),
                ("All global lifetime, suspension and wire command", SavedConfigurationReloadTests.GlobalLifetimeSuspensionAndWire),
                ("target configuration IPC payloads", RuntimeConfigurationStage24Tests.ConfigurationProtocol),
                ("dashboard assignment and passive hardware isolation", RuntimeConfigurationStage24Tests.AssignmentIsolation),
                ("independent Desktop Board Enabled and Lock commands", RuntimeConfigurationStage24Tests.DesktopSwitches),
                ("target configuration transaction rollback", RuntimeConfigurationStage24Tests.ConfigurationRollback),
                ("concurrent persistence and stale binding guards", RuntimeConfigurationStage24Tests.ConcurrentPersistence),
                ("global lifetime and suspended configuration commands", RuntimeConfigurationStage24Tests.GlobalLifetimeAndSuspendedState),
            ]),
            ("Runtime / Idle", [
                ("idle timeout policy and config persistence", RuntimeIdleTests.PolicyAndConfig),
                ("Windows power policy refresh", RuntimeIdleTests.PowerPolicyRefresh),
                ("telemetry pause and no catch-up", RuntimeIdleTests.TelemetryDoesNotCatchUp),
                ("physical initial telemetry frame with long interval", RuntimeIdleTests.InitialTelemetryWakesLongIntervalPhysicalFrame),
                ("Desktop and physical idle lifecycle", RuntimeIdleTests.OutputsPreserveStateAndTransport),
                ("in-flight USB safe idle pause", RuntimeIdleTests.InFlightUsbPause),
            ]),
            ("Runtime / IPC", [
                ("IPC shared request and original error identity", Issue105IpcLoggingTests.SharedRequestAndOriginalError),
                ("IPC legacy compatibility and strict envelope", Issue105IpcLoggingTests.LegacyCompatibilityAndEnvelopeAdmission),
                ("IPC lost response never repeats command", Issue105IpcLoggingTests.LostResponseNeverRepeatsCommand),
                ("IPC context lifetime and deferred feedback", Issue105IpcLoggingTests.ContextIsolationAndDeferredFeedback),
                ("IPC counted status and observer isolation", Issue105IpcLoggingTests.CountedStatusAndObserverIsolation),
                ("Runtime IPC output control isolation", RuntimeIpcOutputControlIsolation),
                ("Runtime IPC shutdown cancellation contract", RuntimeIpcShutdownCancellationContract),
                ("Runtime normal startup and unsupported arguments", RuntimeOutputStartupPolicy),
                ("Output menu device command model", ScreenMenuDeviceCommandModel),
            ]),
            ("Runtime / Notifications", [
                ("shared command result and target identity", RuntimeNotificationStage26Tests.SharedCommandResults),
                ("pending readiness and one terminal notification", RuntimeNotificationStage26Tests.PendingAndTerminalOutcomes),
                ("configuration observation and sibling isolation", RuntimeNotificationStage26Tests.ConfigurationAndIsolation),
                ("failed, partial and unknown command outcomes", RuntimeNotificationStage26Tests.FailuresAndUnknownResults),
                ("aggregate All command outcomes", RuntimeNotificationStage26Tests.AggregateAllOutcomes),
                ("passive silence and superseded completion", RuntimeNotificationStage26Tests.QuietAndSupersededRequests),
                ("notification failure and accepted shutdown", RuntimeNotificationStage26Tests.NotificationFailureAndShutdown),
                ("concise banners and detailed command diagnostics", RuntimeNotificationStage26Tests.ConciseBannerAndFullDiagnostics),
            ]),
            ("Runtime / Output Control", [
                ("independent Output status and disabled hardware", RuntimeStatusStage24Tests.IndependentOutputStatus),
                ("additive IPC status and legacy payload compatibility", RuntimeStatusStage24Tests.StatusWireCompatibility),
                ("tray Output actions and Desktop startup", RuntimeStatusStage24Tests.TrayAndStartupPolicy),
                ("native tray root order and command dispatch", RuntimeTrayMenuTests.RootOrderAndDispatch),
                ("Desktop tray Enabled gating and provider-off frames", RuntimeTrayMenuTests.DesktopActionsAndProviderOffFrames),
                ("tray refresh ownership and unavailable measurements", RuntimeTrayMenuTests.RefreshOwnershipAndUnavailableFrames),
                ("physical root placement and consent callback", RuntimeTrayMenuTests.PhysicalPlacementAndConsentBridge),
            ]),
            ("Outputs / Desktop", [
                ("Desktop Canvas shadow viewport geometry and hit region", DesktopCanvasShadowStage05Tests.ViewportGeometryAndInputMapping),
                ("Desktop Canvas contour shadow BGRA composition", DesktopCanvasShadowStage05Tests.ContourAlphaAndCachedComposition),
                ("Desktop Canvas shadow session reload and renderer rollback", DesktopCanvasShadowStage05Tests.SessionShadowReloadAndRollback),
                ("Desktop buffered transitions and native ownership", Issue105DesktopLoggingTests.BufferedTransitionsAndOwnership),
                ("Shell failure cancellation and payload privacy", Issue105DesktopLoggingTests.ShellFailureCancellationAndPrivacy),
                ("unavailable native Desktop window isolation and recovery", DesktopSessionStage09Tests.UnavailableExplorerHost),
                ("Desktop Z-order policy and persistence", DesktopZOrderStage14Tests.PolicyAndPersistence),
                ("Desktop Z-order lifecycle reconciliation", DesktopZOrderStage14Tests.SessionLifecycle),
                ("Multiple Desktop session dashboard and removal isolation", DesktopMultiSessionStage15Tests.SharedBoardReloadAndRemoval),
                ("Multiple Desktop session failed-start recovery", DesktopMultiSessionStage15Tests.FailedPanelCreationIsolation),
                ("Desktop session lifecycle and no USB", DesktopSessionStage09Tests.DesktopLifecycle),
                ("Desktop initial telemetry frame", DesktopSessionStage09Tests.RefreshOnFirstTelemetry),
                ("Desktop session reconfigure and orientation", DesktopSessionStage09Tests.ReconfigureGeometry),
                ("Desktop Board Enabled versus Provider Enabled", DesktopSessionStage09Tests.IndependentSwitches),
                ("Desktop hidden/suspended/disabled suppresses frames", DesktopSessionStage09Tests.HiddenAndDisabledRenderSuppression),
                ("Desktop layered BGRA stride, alpha and blending", DesktopTransparencyStage10Tests.PremultipliedTransfer),
                ("Desktop transparent/translucent/opaque session frames", DesktopTransparencyStage10Tests.SessionCanvasAlpha),
                ("Desktop placement multi-monitor and recovery", DesktopPositionStage11Tests.MonitorRecovery),
                ("Desktop cross-monitor visibility and detached recovery", DesktopPositionStage11Tests.MultiMonitorCoverage),
                ("Desktop per-target placement JSON persistence", DesktopPositionStage11Tests.ScopedPersistence),
                ("Desktop panel taskbar exclusion", DesktopLockStage12Tests.TaskbarExclusion),
                ("Desktop locked layered style and unlocked hit-testing", DesktopLockStage12Tests.NativeStylePolicy),
                ("Desktop Lock changes on live output reconfiguration", DesktopLockStage12Tests.LiveLockToggle),
            ]),
            ("Outputs / Physical", [
                ("exact unscaled pixel mapping", Issue95FrameGeometryTests.ExactPixelMapping),
                ("ordinary worker ignores panel dimensions", Issue95FrameGeometryTests.OrdinaryWorkerIgnoresPanelSize),
                ("raster and wire resource bounds", Issue95FrameGeometryTests.ResourceAndWireBounds),
                ("LY endpoint admission", Issue95GenericOutputTests.LyEndpointAdmission),
                ("generic Output round trip and ownership", Issue95GenericOutputTests.NewFormatRoundTripAndOwnership),
                ("required geometry protocol and binding", Issue95GenericOutputTests.RequiredGeometryProtocolAndBinding),
                ("candidate confirmation and copy isolation", Issue95GenericOutputTests.CandidateCannotBypassConfirmation),
                ("Trofeo wire framing contract", TrofeoWireFramingContract),
                ("Trofeo USB transfer segmentation contract", TrofeoUsbTransferSegmentationContract),
                ("Trofeo overlapped wait completed", TrofeoOverlappedWaitCompleted),
                ("Trofeo overlapped wait canceled and drained", TrofeoOverlappedWaitCanceledAndDrained),
                ("Trofeo overlapped wait cancellation failure drains", TrofeoOverlappedWaitCancellationFailureDrains),
                ("Trofeo overlapped x64 ABI contract", TrofeoOverlappedX64AbiContract),
                ("Trofeo overlapped cancellation waits for completion", TrofeoOverlappedCancelAwaitsCompletion),
                ("Frame pump suspend cancels blocked open", FramePumpSuspendCancelsBlockedOpen),
                ("Frame pump dispose cancels blocked send", FramePumpDisposeCancelsBlockedSend),
                ("Final physical Output disable drains frame and ACK before re-enable", FramePumpShutdownTests.FinalOutputFrameCompletion),
                ("Frame pump dispose completes pending handshake without frame admission", FramePumpShutdownTests.HandshakeCompletion),
                ("Frame pump dispose cancels stalled handshake after grace", FramePumpShutdownTests.StalledHandshakeCancellation),
                ("selected physical Remove native release and sibling isolation", FramePumpShutdownTests.SelectedPhysicalRemovalRelease),
                ("acquisition cleanup uncertainty blocks Remove", FramePumpShutdownTests.AcquisitionCleanupFailureBlocksRemoval),
                ("Frame pump suspend during blocked USB open", FramePumpSuspendDuringBlockedOpen),
                ("Frame pump suspend during blocked USB send", FramePumpSuspendDuringBlockedSend),
                ("Frame pump reconfigure during blocked USB send", FramePumpReconfigureDuringBlockedSend),
            ]),
            ("Outputs / Physical Enablement", [
                ("physical Enabled consent and exact identity", PhysicalOutputEnablementTests.ConsentAndExactIdentity),
                ("physical Enabled selected persistence and isolation", PhysicalOutputEnablementTests.SelectedPersistenceAndIsolation),
                ("physical Enabled concurrent input guards", PhysicalOutputEnablementTests.ConcurrentInputGuards),
                ("physical Enabled apply and recovery failures", PhysicalOutputEnablementTests.ApplyAndRecoveryFailures),
                ("physical Enabled lifetime, feedback and IPC boundary", PhysicalOutputEnablementTests.LifetimeFeedbackAndIpcBoundary),
            ]),
            ("Outputs / Shared Configuration", [
                ("Output config legacy default and persistence", OutputConfigLegacyDefaultAndPersistence),
                ("Output config multi-target safety", OutputConfigMultiTargetSafety),
                ("Output per-instance configuration persistence", OutputConfigurationModelTests.Persistence),
                ("compatible Enabled persistence and consumer policy", OutputEnabledContractTests.PersistenceAndConsumers),
                ("Output desktop-only configuration", OutputConfigurationModelTests.DesktopOnly),
                ("Output per-instance invalid references and identity", OutputConfigurationModelTests.InvalidReferences),
                ("Per-output dashboard assignment isolation", OutputDashboardOwnershipTests.IndependentAssignments),
                ("Per-output shared board and targeted reload", OutputDashboardOwnershipTests.SharedBoardTargetReload),
                ("Per-output dashboard transaction rollback", OutputDashboardOwnershipTests.Rollback),
            ]),
            ("Outputs / Shared Lifecycle", [
                ("per-device USB handshake settings and reconfigure isolation", FramePumpPerDeviceUsbSettings),
                ("Output session manager lifecycle", OutputSessionManagerLifecycle),
                ("Output session manager reconfiguration", OutputSessionManagerReconfiguration),
                ("Output session manager transactional preparation", OutputSessionManagerTransactionalPreparation),
                ("Output session commit rollback", OutputSessionCommitRollback),
                ("Output session startup rollback", OutputSessionStartupRollback),
                ("Output session global restart rollback", OutputSessionGlobalRestartRollback),
                ("Output session live reconfigure startup rollback", OutputSessionLiveReconfigureStartupRollback),
                ("Output session lifecycle failure isolation", OutputSessionLifecycleFailureIsolation),
                ("Runtime IPC output command protocol", RuntimeIpcOutputCommandProtocol),
                ("Runtime IPC output status payload", RuntimeIpcOutputStatusPayload),
            ]),
            ("Devices / Adoption", [
                ("adoption MissingBindingStillAllowsPassiveRetry", Issue71AdoptionTests.MissingBindingStillAllowsPassiveRetry),
                ("adoption PartialDiscoveryIsolation", Issue71AdoptionTests.PartialDiscoveryIsolation),
                ("adoption ConsentPersistenceAndCancel", Issue71AdoptionTests.ConsentPersistenceAndCancel),
                ("adoption StaleConsentAndBrightnessBounds", Issue71AdoptionTests.StaleConsentAndBrightnessBounds),
                ("adoption ReconciliationUsesExactAdapter", Issue71AdoptionTests.ReconciliationUsesExactAdapter),
                ("adoption FaultedTurzxDoesNotAutomaticallyReopen", Issue71AdoptionTests.FaultedTurzxDoesNotAutomaticallyReopen),
                ("adoption FailedTurzxOpenAndTrofeoRetryIsolation", Issue71AdoptionTests.FailedTurzxOpenAndTrofeoRetryIsolation),
            ]),
            ("Devices / Candidate Configuration", [
                ("candidate live action states", Issue95ProtocolChoiceTests.LiveActionStates),
                ("catalog protocol choices and draft admission", Issue95ProtocolChoiceTests.CatalogChoicesAndDraftAdmission),
                ("candidate explicit choice survives refresh", Issue95ProtocolChoiceTests.CandidateRefreshPreservesExplicitChoice),
                ("protocol radio availability and reconnect", Issue95ProtocolChoiceTests.RadioAvailabilityAndReconnect),
                ("candidate draft exact trial and invalidation", Issue95CandidateDraftTests.EditableParametersAndRuntimeIntent),
                ("candidate draft invalid and stale binding", Issue95CandidateDraftTests.InvalidAndUnprovenCandidatesFailClosed),
                ("clean runtime plus visual approval gates Save", Issue95CandidateDraftTests.OnlyCleanRuntimeAndPositiveVisualAllowSave),
                ("failed USB result and native cleanup block Save", Issue95CandidateDraftTests.TechnicalFailureAndNativeCleanupFailClosed),
            ]),
            ("Devices / Candidate Save", [
                ("candidate per-card Save merge and working board", Issue95CandidateSaveTests.PerCardMergeAndWorkingBoard),
                ("candidate Save stale binding and apply rollback", Issue95CandidateSaveTests.StaleBindingAndApplyFailure),
                ("configured card isolated Save and stale assignment", Issue95CandidateSaveTests.ConfiguredCardIsolationAndStaleWrite),
                ("Configured device Save: Runtime start decision outcomes", Issue95ConfiguredRuntimePromptTests.SaveStartDecisions),
                ("Forget exact node, rollback and manual recovery", Issue95CandidateSaveTests.ForgetExactNodeAndRollback),
                ("Remove selected card and partial state", Issue95CandidateSaveTests.RemoveCardIsolationAndPartialState),
            ]),
            ("Devices / Configuration", [
                ("mixed configuration compatibility", Issue71ModelTests.CompatibleConfiguration),
                ("unsafe ownership rejection", Issue71ModelTests.ConfigurationRejectsUnsafeOwnership),
                ("brightness application policy", Issue71ModelTests.BrightnessPolicy),
                ("exact passive WinUSB interfaces", Issue71ModelTests.ExactPassiveInterfaces),
                ("missing and removed metadata", Issue71ModelTests.MissingAndRemovedMetadata),
                ("binding and inventory isolation", Issue71ModelTests.BindingAndInventory),
                ("detached drafts and unsupported adapter options", Issue71ModelTests.DetachedDraftAndAdapterOptions),
                ("registered interface class decoding", Issue71ModelTests.RegisteredClassDecoding),
            ]),
            ("Devices / Diagnostics", [
                ("USB observer failure preserves completion", Issue105UsbLoggingTests.ObserverFailurePreservesNativeCompletion),
                ("USB bounded evidence and unknown counts", Issue105UsbLoggingTests.BoundedEvidenceAndUnknownCounts),
                ("USB healthy sparse stream and exact I/O", Issue105UsbLoggingTests.HealthyStreamIsSparseAndExact),
                ("TURZX observed brightness forwarded to live JPEG stream", Issue105UsbLoggingTests.ObservedBrightnessForwardsToTurzx),
                ("TURZX observed brightness reply failure blocks next frame", Issue105UsbLoggingTests.ObservedBrightnessFailureStopsBeforeFrame),
                ("USB malformed frame evidence and no retry", Issue105UsbLoggingTests.MalformedFrameBoundariesRetained),
                ("USB pending cancellation and native ownership", Issue105UsbLoggingTests.PendingCancellationKeepsNativeOwnership),
                ("USB native failure during cancellation", Issue105UsbLoggingTests.NativeFailureDuringCancellationIsRetained),
                ("USB slow completion and failed acquisition", Issue105UsbLoggingTests.SlowCompletionAndOpenFailure),
                ("Physical handshake context and bounded evidence", PhysicalHandshakeDiagnosticTests.CommonContextAndBounds),
                ("Physical handshake quiet paths and original error identity", PhysicalHandshakeDiagnosticTests.ObserverPreservesErrorsAndQuietPaths),
                ("Physical handshake Trofeo actual handshake replies", PhysicalHandshakeDiagnosticTests.TrofeoHandshakeReplies),
                ("Physical handshake Trofeo I/O failure ordering", PhysicalHandshakeDiagnosticTests.TrofeoHandshakeIoErrors),
                ("Physical handshake TURZX short primary replies", PhysicalHandshakeDiagnosticTests.TurzxShortReplies),
                ("Physical handshake TURZX follow-up and unknown counts", PhysicalHandshakeDiagnosticTests.TurzxFollowUpAndUnknownCounts),
                ("Physical handshake healthy sync and frame exclusion", PhysicalHandshakeDiagnosticTests.HealthySyncDoesNotCaptureFrames),
                ("Physical handshake shared factory admission and ordinary log", PhysicalHandshakeDiagnosticTests.FactoryLogsBeforeHardwareAdmission),
                ("sync capture exact bytes and single exchange", TurzxTransportDiagnosticTests.SyncCapturePreservesExactReplyAndSingleExchange),
                ("sync capture admission and failure lifetime", TurzxTransportDiagnosticTests.SyncCaptureAdmissionAndFailureLifetime),
                ("TURZX passive diagnostic explicit admission", TurzxPassiveDiagnosticTests.ExplicitAdmission),
                ("TURZX passive diagnostic exact metadata and privacy", TurzxPassiveDiagnosticTests.ExactMetadataAndPrivateOutput),
                ("TURZX passive diagnostic missing and ambiguous interfaces", TurzxPassiveDiagnosticTests.MissingAmbiguousAndRemoved),
                ("TURZX passive diagnostic sanitized errors", TurzxPassiveDiagnosticTests.ErrorsDoNotLeakPrivateMessages),
                ("TURZX transport diagnostic explicit consent", TurzxTransportDiagnosticTests.ExplicitConsentAndArguments),
                ("TURZX transport diagnostic one command and empty brightness", TurzxTransportDiagnosticTests.OneCommandAndEmptyBrightness),
                ("TURZX transport diagnostic failure isolation", TurzxTransportDiagnosticTests.FailuresStopBeforeFurtherIo),
                ("TURZX two JPEG frames on one connection", TurzxTransportDiagnosticTests.JpegPairSequenceAndFailures),
                ("TURZX bounded post-image reply reads", TurzxTransportDiagnosticTests.JpegPairReadBoundaries),
                ("TURZX diagnostic uses stream transport owner", TurzxTransportDiagnosticTests.JpegStreamUsesTransport),
                ("TURZX synthetic codecs, geometry and corners", TurzxTransportDiagnosticTests.SyntheticCodecGeometryAndCorners),
            ]),
            ("Devices / Discovery", [
                ("candidate invalidation and adopted-card evidence", Issue105DiscoveryLoggingTests.CandidateInvalidationAndAdoption),
                ("discovery failure recovery and privacy", Issue105DiscoveryLoggingTests.DiscoveryFailureRecoveryAndPrivacy),
                ("counted state bounds and close fence", Issue105DiscoveryLoggingTests.CountedStatesBoundsAndClose),
                ("partial discovery failure isolation and strict global errors", Issue95CatalogCandidateTests.PartialFailureIsolation),
                ("composite USB function-parent correlation and ambiguity", Issue95CatalogCandidateTests.CompositeFunctionCorrelation),
                ("composite snapshot completeness", Issue95CatalogCandidateTests.CompositeSnapshotCompleteness),
                ("catalog VID/PID candidate admission", Issue95CatalogCandidateTests.CatalogBoundedPassiveCandidates),
                ("exact passive interface correlation", Issue95CatalogCandidateTests.ExactCorrelationAndAmbiguity),
                ("exact candidate selection and per-instance Forget", Issue95CatalogCandidateTests.ExactSelectionAndPerInstanceIgnore),
                ("multi-interface candidate ambiguity", Issue95CatalogCandidateTests.MultiInterfaceSelectionFailsClosed),
                ("strict catalog path and driver rejection", Issue95CatalogCandidateTests.StrictPathAndMissingDriver),
            ]),
            ("Devices / Identity & Inventory", [
                ("catalog and exact USB devnode parsing", Issue77UsbDiscoveryTests.CatalogAndExactParsing),
                ("passive catalog capture without unrelated reads", Issue77UsbDiscoveryTests.PassiveCatalogCapture),
                ("composite grouping and distinct units", Issue77UsbDiscoveryTests.CompositeAndDistinctUnits),
                ("conflicting and missing metadata", Issue77UsbDiscoveryTests.ConflictingAndMissingMetadata),
                ("bounded CM buffer races and errors", Issue77UsbDiscoveryTests.BufferRacesAndErrors),
                ("removal races and private observation identity", Issue77UsbDiscoveryTests.RemovalAndPrivateIdentity),
                ("exact devnode metadata and identity namespaces", Issue77DeviceIdentityTests.ExactDevnodeMetadata),
                ("supported and informational reconciliation", Issue77DeviceIdentityTests.ReconcileDistinctAndCompositeDevices),
                ("exact bindings and devnode ambiguity", Issue77DeviceIdentityTests.BindingAndAmbiguity),
                ("recognition versus transport dispatch", Issue77DeviceIdentityTests.RecognitionAndDispatchContract),
                ("informational draft identity and status lifecycle", Issue77DeviceIdentityTests.InformationalDraftLifecycle),
                ("compatible persistence and stale consent", Issue77DeviceIdentityTests.CompatiblePersistenceAndConsent),
                ("Output target selection", OutputTargetSelection),
                ("passive discovery and reconnect identity", DeviceDiscoveryStage16Tests.PassiveReconnect),
                ("duplicate configured identity fails closed", DeviceDiscoveryStage16Tests.DuplicateIdentity),
                ("verified USB instance fingerprint parsing", DeviceDiscoveryStage16Tests.PassiveSerialFingerprint),
                ("duplicate physical serial fingerprint fails closed", DeviceDiscoveryStage16Tests.DuplicateSerialFingerprint),
                ("physical inventory known and new snapshots", DeviceDiscoveryStage16Tests.InventoryPreservesKnownAndNew),
                ("physical inventory ambiguity and no rebind", DeviceDiscoveryStage16Tests.InventoryNeverRebindsAmbiguousHardware),
                ("native inventory capture isolation", DeviceDiscoveryStage16Tests.InventoryCaptureIsolation),
                ("physical device enable consent policy", DeviceDiscoveryStage16Tests.DeviceEnabledConsentPolicy),
                ("Settings physical Output removal and disconnect policy", DeviceDiscoveryStage16Tests.DeviceRemovalPolicy),
                ("Output device discovery identity", OutputDeviceDiscoveryIdentity),
            ]),
            ("Devices / Recovery", [
                ("recovery complete metadata before removal", Issue71ModelTests.RecoveryMetadataCannotInventRemoval),
                ("recovery exact reattachment after send/open failure", Issue71RecoveryTests.ReattachmentRecoversSendAndOpenFailure),
                ("recovery trustworthy absence and exact return", Issue71RecoveryTests.RecoveryRequiresTrustworthyAbsenceAndExactReturn),
                ("recovery stale discovery and lifecycle isolation", Issue71RecoveryTests.StaleRecoveryScanCannotSurviveLifecycleChange),
                ("recovery bounded checkpoint stays single-attempt", Issue71RecoveryTests.BoundedCheckpointNeverRecoversAutomatically),
                ("recovery cleanup failure blocks acquisition", Issue71RecoveryTests.FailedDisposalCannotAuthorizeRecovery),
            ]),
            ("Devices / Runtime", [
                ("Runtime GeometryAndLegacyParity", Issue71RuntimeTests.GeometryAndLegacyParity),
                ("Runtime CanonicalRendererGeometryAndIsolation", Issue71RuntimeTests.CanonicalRendererGeometryAndIsolation),
                ("Runtime EncodedPayloadBound", Issue71RuntimeTests.EncodedPayloadBound),
                ("Runtime AdapterAdmission", Issue71RuntimeTests.AdapterAdmission),
                ("Runtime CheckpointAdmissionAndPreflight", Issue71RuntimeTests.CheckpointAdmissionAndPreflight),
                ("Runtime SharedWorkerLiveFrames", Issue71RuntimeTests.SharedWorkerLiveFrames),
                ("Runtime SharedWorkerFailureNoReopen", Issue71RuntimeTests.SharedWorkerFailureNoReopen),
                ("Runtime FrameBudgetStopsFurtherAdmission", Issue71RuntimeTests.FrameBudgetStopsFurtherAdmission),
                ("Runtime StaticPayloadIsNotAnimation", Issue71RuntimeTests.StaticPayloadIsNotAnimation),
            ]),
            ("Devices / Transport", [
                ("TURZX independent encrypted packet fixtures", Issue71TransportTests.PacketFixtures),
                ("TURZX packet bounds and codec identity", Issue71TransportTests.PacketBounds),
                ("TURZX command-specific reply classification", Issue71TransportTests.CommandReplies),
                ("TURZX sequence and empty brightness boundary", Issue71TransportTests.SequenceAndBrightnessBoundary),
                ("TURZX command admission and no automatic retry", Issue71TransportTests.AdmissionAndNoRetry),
                ("TURZX read failures and stale reply bytes", Issue71TransportTests.ReadFailuresAndStaleBytes),
                ("TURZX exact open and failed sync cleanup", Issue71TransportTests.ExactOpenAndFailureCleanup),
                ("TURZX native completion and error identity", Issue71TransportTests.NativeCompletionAndErrors),
                ("TURZX native cancellation drains before release", Issue71TransportTests.NativeCancellationLifetime),
                ("TURZX deadline and concurrent disposal", Issue71TransportTests.NativeDeadlineAndDispose),
                ("TURZX native ABI and endpoint validation", Issue71TransportTests.NativeAbiAndEndpoints),
                ("TURZX transport disposal drains command", Issue71TransportTests.TransportDisposalDrainsCommand),
                ("TURZX pending native success", Issue71TransportTests.PendingNativeSuccess),
                ("TURZX commands remain serialized", Issue71TransportTests.CommandsRemainSerialized),
                ("TURZX JPEG stream owns complete replies", Issue71TransportTests.JpegStreamOwnsReplyBoundaries),
                ("TURZX brightness-to-JPEG uncertain reply halts stream", Issue71TransportTests.JpegStreamBrightnessCannotSkipUncertainReplies),
                ("TURZX JPEG stream uncertain-boundary rejection", Issue71TransportTests.JpegStreamRejectsUncertainBoundaries),
                ("TURZX JPEG stream serializes follow-up", Issue71TransportTests.JpegStreamSerializesFollowUp),
                ("TURZX JPEG stream native follow-up lifetime", Issue71TransportTests.JpegStreamFollowUpNativeLifetime),
            ]),
            ("Devices / USB Test", [
                ("exclusive Runtime process ownership", Issue95RuntimeProcessLeaseTests.ConcurrentOwnersFailClosed),
                ("Runtime lease release and restart", Issue95RuntimeProcessLeaseTests.ReleasedOwnerAllowsNewAcquisition),
                ("USB Test typed IPC roundtrip", Issue95UsbTestIpcTests.RoundtripAndCorrelation),
                ("USB Test IPC rejects malformed and unsafe requests", Issue95UsbTestIpcTests.MalformedRequestsFailClosed),
                ("bounded Runtime IPC request admission", Issue95UsbTestIpcTests.BoundedServerRequestAdmission),
                ("transient Test exclusive session and correlation", Issue95RuntimeUsbTestSessionTests.ExclusiveAdmissionAndCorrelation),
                ("transient Test cancellation and native release", Issue95RuntimeUsbTestSessionTests.CancelAndNativeRelease),
                ("transient Test client loss and deadlines", Issue95RuntimeUsbTestSessionTests.SilenceDeadlineAndUncertainCleanup),
                ("Runtime Test exact passive preflight", Issue95RuntimeUsbPreflightTests.ExactRuntimeAdmissionAndNegativeCases),
                ("Runtime Test protocol and geometry preflight", Issue95RuntimeUsbPreflightTests.ProtocolAndGeometryFailClosed),
                ("Runtime Test coordinator enforces fresh preflight", Issue95RuntimeUsbPreflightTests.CoordinatorRequiresFreshPreflightBeforeLease),
                ("USB workflow phase and visual evidence", Issue105UsbWorkflowLoggingTests.EditorPhasesAndVisualOutcome),
                ("USB Runtime admission and worker failures", Issue105UsbWorkflowLoggingTests.RuntimeAdmissionAndWorkerFailures),
                ("USB IPC unknown reply and original exception", Issue105UsbWorkflowLoggingTests.TransportUnknownReplyAndOriginalException),
                ("USB IPC quiet polling and recovery totals", Issue105UsbWorkflowLoggingTests.TransportQuietPollingAndRecoveryTotals),
                ("live Test WorkerCompletionAndVirtualBoard", Issue95LiveTestTests.WorkerCompletionAndVirtualBoard),
                ("live Test StrictWorkerFailureAndCleanup", Issue95LiveTestTests.StrictWorkerFailureAndCleanup),
                ("live Test CancellationWaitsForNativeRelease", Issue95LiveTestTests.CancellationWaitsForNativeRelease),
                ("USB Test countdown starts after delivered frame and native release", Issue95LiveTestTests.ConnectionCountdownAndDisconnectPhases),
                ("USB Test modal follows Runtime observation phases", Issue95LiveTestTests.ModalDisplaysOnlyRuntimeReportedObservation),
                ("live Test ExistingOwnerAndResponseCorrelation", Issue95LiveTestTests.ExistingOwnerAndResponseCorrelation),
                ("live Test EditorLaunchAndStrictResults", () => Issue95LiveTestTests.EditorLaunchAndStrictResults().GetAwaiter().GetResult()),
            ]),
            ("Rendering / Alpha", [
                ("Alpha BGRA transparency and black JPEG composite", SharedAlphaStage08Tests.TransparentFrameAndJpeg),
                ("Alpha BGRA layers, orientation and Editor parity", SharedAlphaStage08Tests.LayersAndPreviewParity),
                ("Alpha BGRA frame ownership and disposal", SharedAlphaStage08Tests.OwnershipAndDisposal),
            ]),
            ("Rendering / Raster Sampling", [
                ("raster checkerboards downscale without aliasing", Issue122RasterSamplingTests.CheckerboardDownscaling),
                ("native pixels, enlargement and transformed scaling", Issue122RasterSamplingTests.NativePixelsAndTransformedScaling),
                ("raster icon/file alpha, cache lifetime and bounds", Issue122RasterSamplingTests.IconFileAlphaAndCacheLifecycle),
                ("raster sharpness control and native-size safety", Issue122RasterSamplingTests.RasterSharpnessChangesEdges),
                ("unsharp mask strength and transparent edge preservation", Issue122RasterSamplingTests.UnsharpMaskStrengthAndAlpha),
                ("unsharp mask preserves low-pass reduction and cached output", Issue122RasterSamplingTests.UnsharpMaskRetainsLowPass),
                ("raster sharpness application configuration", Issue122RasterSamplingTests.RasterSharpnessConfiguration),
                ("Binary image state shares filtered raster path", Issue122RasterSamplingTests.BinaryRasterSamplingParity),
            ]),
            ("Rendering / Cache & Geometry", [
                ("Runtime JPEG zero-copy transport contract", RuntimeJpegZeroCopyTransportContract),
                ("Stable widget shadow cache", StableWidgetShadowIsReused),
                ("Numeric widget shadow cache invalidation", NumericWidgetShadowInvalidatesOnValueChange),
                ("Cached widget shadow follows translation", CachedWidgetShadowFollowsTranslation),
                ("Resize local-axis projection", ResizeLocalAxisProjection),
                ("Gauge resize geometry clamp", GaugeResizeGeometryClamp),
                ("Preview resize geometry invalidation", PreviewResizeGeometryInvalidation),
            ]),
            ("Logging / Core", [
                ("Errors routing and exception correlation", Issue105LoggingTests.ErrorRoutingAndPropagation),
                ("throttle totals and changed native failures", Issue105LoggingTests.ThrottledFinalTotalsAndChangedNativeError),
                ("retained history and failed sink", Issue105LoggingTests.AppendHistoryPartialTailAndSinkFailure),
                ("component and overlapping Editor sessions", Issue105LoggingTests.ComponentAndOverlappingEditorSessions),
                ("concurrent writers and abandoned gate", Issue105LoggingTests.ConcurrentWritersAndAbandonedGate),
                ("crashed final Editor session", Issue105LoggingTests.CrashedLastEditorReleasesSession),
                ("72-hour rolling history and idle pruning", Issue105LoggingTests.RollingRetentionAndIdleErrors),
                ("idle session prunes component and error history", Issue105LoggingTests.IdleSessionPrunesAllThreeStreams),
                ("configurable retention and invalid policy", Issue105LoggingTests.ConfigurableRetentionAcrossSessions),
            ]),
            ("Logging / Integration", [
                ("bounded previous-session uncertainty", Issue105FinalCoverageTests.PreviousSessionBoundsAndUncertainty),
                ("LHM recovery totals and client isolation", Issue105FinalCoverageTests.LhmRecoveryAndClientIsolation),
                ("storage full sharing flush and contention", Issue105FinalCoverageTests.StorageFaultsPreserveOriginalAndLossEvidence),
            ]),
            ("Logging / Lifecycle", [
                ("pending cleanup preserves ownership", Issue105CleanupLoggingTests.PendingCleanupDoesNotReleaseOwnership),
                ("cleanup finally and original failures", Issue105CleanupLoggingTests.CleanupFailuresKeepFinallyAndOriginalErrors),
                ("cleanup observer failure isolation", Issue105CleanupLoggingTests.ObserverFaultDoesNotReplaceCleanupFailure),
                ("Desktop truthful cleanup evidence", Issue105CleanupLoggingTests.DesktopCleanupDoesNotReportFalseSuccess),
                ("preview shutdown worker fence evidence", Issue105CleanupLoggingTests.PreviewShutdownEvidenceRespectsWorkerFence),
                ("preview source cleanup aggregate evidence", Issue105CleanupLoggingTests.PreviewSourceFailuresRemainOneAggregate),
            ]),
            ("Logging / Physical Compatibility", [
                ("retired USB options ignored and omitted", Issue104LoggingTests.RetiredOptionsIgnoredAndOmitted),
                ("diagnostics row removed and provider retained", Issue104LoggingTests.RowsRemovedAndProviderRetained),
                ("shared physical events and error routing", Issue104LoggingTests.SharedPhysicalEventsAndFailures),
                ("stale logging changes do not reconnect", Issue104LoggingTests.RetiredFlagDoesNotReconnect),
            ]),
            ("Logging / Platform Events", [
                ("platform event chronology deduplication and reset", Issue105PlatformEventTests.ChronologyDeduplicationAndReset),
                ("platform event caps and cursor persistence faults", Issue105PlatformEventTests.BoundsCursorAndPersistenceFailures),
                ("platform access cancellation and query allowlist", Issue105PlatformEventTests.AccessFailureCancellationAndAllowlist),
            ]),
            ("Logging / Process", [
                ("Launcher shared Errors and failure isolation", Issue105ProcessLoggingTests.LauncherSharedErrorsAndFailureIsolation),
                ("action results session progress and denied sink", Issue105ProcessLoggingTests.ActionResultsSessionProgressAndDeniedSink),
            ]),
            ("Logging / Resources", [
                ("resource failure context without inner diagnostic I/O", Issue105ResourceLoggingTests.ResourceContextWithoutInnerIo),
            ]),
            ("Integration / DateTime", [
                ("DateTime fractional projection and boundaries", Issue96DateTimePrecisionTests.ProjectionAndBoundaries),
                ("DateTime Editor, cache and Binary equality", Issue96DateTimePrecisionTests.EditorCacheAndBinary),
                ("DateTime format schema compatibility", Issue96DateTimePrecisionTests.SchemaCompatibility),
                ("live frame clock and telemetry availability", Issue96DateTimePrecisionTests.LiveFrameClockAndAvailability),
            ]),
            ("Integration / Physical Isolation", [
                ("blocked USB leaves Desktop and shared telemetry responsive", BlockedTransportStage27Tests.DesktopAndTelemetryContinueDuringBlockedPhysicalSend),
            ]),
            ("Integration / Resource Rollback", [
                ("deleted image Desktop selected reload", DeletedImageRollbackTests.SelectedReload),
                ("deleted image multi-Output rollback", DeletedImageRollbackTests.MultiOutputRollback),
                ("physical renderer retained rollback", DeletedImageRollbackTests.PhysicalRendererRollback),
            ]),
            ("Infrastructure / Fixtures", [
                ("Fixture cleanup original failures and no retry", RegressionFixtureCleanupTests.OriginalFailuresAndNoRetry),
                ("Fixture cleanup held-file snapshot and native ABI", RegressionFixtureCleanupTests.HeldFileSnapshotAndNativeLayout),
            ]),
            ("Infrastructure / Launcher", [
                ("Launcher and About shared window and shortcut contract", LauncherPackagingContractTests.SharedWindowAndShortcuts),
                ("launcher exits only after successful start", LauncherPackagingContractTests.ClosesAfterSuccessfulLaunchOnly),
            ]),
        };
        // Protect the registry itself against orphaned runnable test methods,
        // ambiguous failure labels and transcript-incompatible group names.
        var registryContractCases = new (string Name, Action Run)[]
        {
            ("regression registry includes all runnable public tests",
                () => RegressionRegistryContractTests.AllRunnableCasesAreRegistered(testGroups)),
            ("regression registry rejects malformed metadata",
                RegressionRegistryContractTests.MalformedRegistryMetadataIsRejected),
        };
        testGroups = testGroups.Append(
            ("Infrastructure / Regression", registryContractCases)).ToArray();

        // Execute each logical subsystem contiguously for live progress.
        // Preserve the relative order of cases inside every subsystem.
        var logicalGroups = new List<(string Group, List<(string Name, Action Run)> Cases)>();
        var groupIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var segment in testGroups)
        {
            if (!groupIndices.TryGetValue(segment.Group, out var groupIndex))
            {
                groupIndex = logicalGroups.Count;
                groupIndices.Add(segment.Group, groupIndex);
                logicalGroups.Add((segment.Group, new List<(string Name, Action Run)>()));
            }
            logicalGroups[groupIndex].Cases.AddRange(segment.Cases);
        }

        var failed = 0;
        var total = 0;
        foreach (var group in logicalGroups)
        {
            var count = group.Cases.Count;
            const int progressWidth = 24;
            var markerCount = Math.Min(progressWidth, count);
            var emittedMarkers = 0;
            var currentBatchFailed = false;
            var failures = new List<(string Name, Exception Error)>();

            Console.Write($"  {group.Group.PadRight(34)} [");
            Console.Out.Flush();
            for (var caseIndex = 0; caseIndex < count; caseIndex++)
            {
                var test = group.Cases[caseIndex];
                try
                {
                    test.Run();
                }
                catch (Exception ex)
                {
                    failed++;
                    currentBatchFailed = true;
                    failures.Add((test.Name, ex));
                }

                var completed = caseIndex + 1;
                if (completed == (emittedMarkers + 1) * count / markerCount)
                {
                    Console.Write(currentBatchFailed ? '!' : '.');
                    Console.Out.Flush();
                    currentBatchFailed = false;
                    emittedMarkers++;
                }
            }

            Console.WriteLine($"{new string(' ', progressWidth - markerCount)}] {count - failures.Count,3}/{count}");
            foreach (var failure in failures)
            {
                Console.Error.WriteLine($"FAIL: {failure.Name}");
                Console.Error.WriteLine(failure.Error);
            }
            total += count;
        }

        Console.WriteLine($"Regression checks: {total - failed}/{total} passed.");
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
                Metric = FixtureOutputFps,
                Width = width,
                Height = height
            },
            WidgetTypeContract.Gauge => new C.GaugeWidgetDefinition
            {
                Id = id,
                Metric = FixtureOutputFps,
                Width = width,
                Height = width,
                Min = 0,
                Max = 100
            },
            WidgetTypeContract.Bar => new C.BarWidgetDefinition
            {
                Id = id,
                Metric = FixtureOutputFps,
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

    private static AppConfig CreateMultiOutputConfig() => new()
    {
        Outputs = new OutputConfig
        {
            Targets =
            [
                new OutputTargetConfig
                { Transport = OutputTargetContract.LyBulkTransport, Width = 1920, Height = 480,
                    Id = "screen-a",
                    Name = "Left Trofeo",
                    DeviceId = "usb-interface:" + new string('a', 64),
                    DeviceEnabled = true,
                    ProviderEnabled = true
                },
                new OutputTargetConfig
                { Transport = OutputTargetContract.LyBulkTransport, Width = 1920, Height = 480,
                    Id = "screen-b",
                    Name = "Right Trofeo",
                    DeviceId = "usb-interface:" + new string('b', 64),
                    DeviceEnabled = true,
                    ProviderEnabled = true
                }
            ]
        }
    };

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

        // All three projects inherit version metadata from Directory.Build.props.
        // Verify the published assemblies against the independently built regression assembly,
        // rather than pinning a test to one particular release number.
        var expectedVersion = typeof(Program).Assembly.GetName().Version
            ?? throw new InvalidOperationException("Regression test assembly must expose an AssemblyVersion.");
        Assert(expectedVersion.Build >= 0 && expectedVersion.Revision >= 0,
            "Regression test assembly must expose a four-component AssemblyVersion.");
        Assert(assembly.GetName().Version == expectedVersion,
            $"{assembly.GetName().Name} AssemblyVersion must match the regression build version.");
        var expectedInformationalVersion =
            $"{expectedVersion.Major}.{expectedVersion.Minor}.{expectedVersion.Build}";

        Assert(AttributeValue<System.Reflection.AssemblyCompanyAttribute>(assembly, a => a.Company) == "PinkieSysMon Project",
            $"{assembly.GetName().Name} must expose the normalized PinkieSysMon company identity.");
        Assert(AttributeValue<System.Reflection.AssemblyProductAttribute>(assembly, a => a.Product) == "PinkieSysMon",
            $"{assembly.GetName().Name} must expose the normalized PinkieSysMon product identity.");
        Assert(AttributeValue<System.Reflection.AssemblyTitleAttribute>(assembly, a => a.Title) == expectedTitle,
            $"{assembly.GetName().Name} must expose its expected Windows title metadata.");
        Assert(AttributeValue<System.Reflection.AssemblyDescriptionAttribute>(assembly, a => a.Description) == expectedDescription,
            $"{assembly.GetName().Name} must expose its expected Windows description metadata.");
        Assert(AttributeValue<System.Reflection.AssemblyInformationalVersionAttribute>(assembly, a => a.InformationalVersion) == expectedInformationalVersion,
            $"{assembly.GetName().Name} must expose the normalized informational version.");

        var fileInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location);
        Assert(fileInfo.CompanyName == "PinkieSysMon Project",
            $"{assembly.GetName().Name} Win32 version resources must contain CompanyName.");
        Assert(fileInfo.ProductName == "PinkieSysMon",
            $"{assembly.GetName().Name} Win32 version resources must contain ProductName.");
        Assert(fileInfo.FileDescription == expectedTitle,
            $"{assembly.GetName().Name} Win32 version resources must use the assembly title as FileDescription.");
        Assert(fileInfo.FileVersion == expectedVersion.ToString(4),
            $"{assembly.GetName().Name} Win32 version resources must match the regression build FileVersion {expectedVersion}.");
    }

    private static void AssertMigratedCanvasImageLayer(JsonObject canvas, string name, string? expectedSource)
    {
        var layer = canvas[name] as JsonObject
            ?? throw new InvalidOperationException($"Migrated Canvas Panel layer '{name}' is missing.");
        if (expectedSource is null)
        {
            Assert(layer["mode"]?.GetValue<string>() == C.WidgetPanelLayerMode.None,
                $"Unconfigured Canvas Panel layer '{name}' must remain None.");
            return;
        }

        Assert(layer["mode"]?.GetValue<string>() == C.WidgetPanelLayerMode.Image,
            $"Migrated Canvas Panel layer '{name}' must use an active file image.");
        var asset = layer["image"] as JsonObject
            ?? throw new InvalidOperationException($"Migrated Canvas Panel '{name}' image is missing.");
        Assert(asset["sourceType"]?.GetValue<string>() == C.ImageAssetSourceType.File &&
               asset["source"]?.GetValue<string>() == expectedSource,
            $"Migrated Canvas Panel '{name}' lost its historical file source.");
        Assert(asset["fit"]?.GetValue<string>() == StateVisualProfileContract.FitStretch &&
               asset["loop"]?.GetValue<bool>() == true,
            $"Migrated Canvas Panel '{name}' lost its historical Fit/Loop.");
        Assert(layer["iconOpacity"] is null,
            $"Legacy file opacity must not become a Canvas Panel image opacity.");
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

    private static PinkieSysMon.DashboardModel.DashboardDefinition CreateMinimalCanonicalDashboard() => new()
    {
        SchemaVersion = DashboardSchemaMigration.CurrentSchemaVersion,
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

    private sealed class CancellationAwareTrofeoTestTransport(
        CancellationToken cancellation,
        ManualResetEventSlim started,
        Action disposed) : IPhysicalFrameTransport
    {
        public int WireRotationDegrees => 0;

        public void SendJpegWithDiagnostics(
            ReadOnlySpan<byte> jpeg,
            FrameRenderDiagnostics diagnostics)
        {
            started.Set();
            if (!cancellation.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("Synthetic pending transfer was not canceled.");
            cancellation.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Synthetic transfer completed without cancellation.");
        }

        public void RecordLifecycle(string state, string reason) { }
        public void Dispose() => disposed();
    }

    private sealed class BlockingTrofeoTestTransport : IPhysicalFrameTransport
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
            FrameRenderDiagnostics diagnostics)
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

