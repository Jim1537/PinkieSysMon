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
        if (ShortcutNativeDiagnostic.TryRun(args, Console.WriteLine) is { } shortcutNativeExit)
            return shortcutNativeExit;
        if (TurzxRuntimeDiagnostic.TryRun(args, Console.WriteLine) is { } runtimeCheckExit)
            return runtimeCheckExit;
        if (TurzxTransportDiagnostic.TryRun(args, Console.WriteLine) is { } transportExit)
            return transportExit;
        if (TurzxPassiveDiagnostic.TryRun(args, Console.WriteLine) is { } diagnosticExit)
            return diagnosticExit;
        var testGroups = new (string Group, (string Name, Action Run)[] Cases)[]
        {
            ("Logging / Diagnostics", [
                ("Issue105 bounded previous-session uncertainty", Issue105FinalCoverageTests.PreviousSessionBoundsAndUncertainty),
                ("Issue105 LHM recovery totals and client isolation", Issue105FinalCoverageTests.LhmRecoveryAndClientIsolation),
                ("Issue105 storage full sharing flush and contention", Issue105FinalCoverageTests.StorageFaultsPreserveOriginalAndLossEvidence),
                ("Issue105 Launcher shared Errors and failure isolation", Issue105ProcessLoggingTests.LauncherSharedErrorsAndFailureIsolation),
                ("Issue105 action results session progress and denied sink", Issue105ProcessLoggingTests.ActionResultsSessionProgressAndDeniedSink),
                ("Issue105 resource failure context without inner diagnostic I/O", Issue105ResourceLoggingTests.ResourceContextWithoutInnerIo),
                ("Issue105 Shell resource failure cached per generation", Issue105ResourceLoggingTests.ShellCacheFailureIsOncePerGeneration),
                ("Issue105 platform event chronology deduplication and reset", Issue105PlatformEventTests.ChronologyDeduplicationAndReset),
                ("Issue105 platform event caps and cursor persistence faults", Issue105PlatformEventTests.BoundsCursorAndPersistenceFailures),
                ("Issue105 platform access cancellation and query allowlist", Issue105PlatformEventTests.AccessFailureCancellationAndAllowlist),
                ("Issue105 Desktop buffered transitions and native ownership", Issue105DesktopLoggingTests.BufferedTransitionsAndOwnership),
                ("Issue105 Shell failure cancellation and payload privacy", Issue105DesktopLoggingTests.ShellFailureCancellationAndPrivacy),
                ("Issue105 telemetry capture availability and recovery", Issue105TelemetryLoggingTests.CaptureUnavailableRecoveryAndFirstPublication),
                ("Issue105 telemetry pending worker and queue totals", Issue105TelemetryLoggingTests.PendingWorkerAndQueueCounts),
                ("Issue105 iCUE freshness file and catalog generations", Issue105TelemetryLoggingTests.IcueFreshnessFileAndCatalogGenerations),
                ("Issue105 network offline versus query failure", Issue105TelemetryLoggingTests.NetworkOfflineFailureAndRecovery),
                ("Issue105 candidate invalidation and adopted-card evidence", Issue105DiscoveryLoggingTests.CandidateInvalidationAndAdoption),
                ("Issue105 discovery failure recovery and privacy", Issue105DiscoveryLoggingTests.DiscoveryFailureRecoveryAndPrivacy),
                ("Issue105 counted state bounds and close fence", Issue105DiscoveryLoggingTests.CountedStatesBoundsAndClose),
                ("Issue105 IPC shared request and original error identity", Issue105IpcLoggingTests.SharedRequestAndOriginalError),
                ("Issue105 IPC legacy compatibility and strict envelope", Issue105IpcLoggingTests.LegacyCompatibilityAndEnvelopeAdmission),
                ("Issue105 IPC lost response never repeats command", Issue105IpcLoggingTests.LostResponseNeverRepeatsCommand),
                ("Issue105 IPC context lifetime and deferred feedback", Issue105IpcLoggingTests.ContextIsolationAndDeferredFeedback),
                ("Issue105 IPC counted status and observer isolation", Issue105IpcLoggingTests.CountedStatusAndObserverIsolation),
                ("Issue105 USB workflow phase and visual evidence", Issue105UsbWorkflowLoggingTests.EditorPhasesAndVisualOutcome),
                ("Issue105 USB Runtime admission and worker failures", Issue105UsbWorkflowLoggingTests.RuntimeAdmissionAndWorkerFailures),
                ("Issue105 USB IPC unknown reply and original exception", Issue105UsbWorkflowLoggingTests.TransportUnknownReplyAndOriginalException),
                ("Issue105 USB IPC quiet polling and recovery totals", Issue105UsbWorkflowLoggingTests.TransportQuietPollingAndRecoveryTotals),
                ("Issue105 Settings persistence milestones and conflict", Issue105SettingsLoggingTests.PersistenceMilestonesAndConflict),
                ("Issue105 Settings recovery and concurrent-writer evidence", Issue105SettingsLoggingTests.RecoveryAndConcurrentWriterEvidence),
                ("Issue105 Settings physical card identity and privacy", Issue105SettingsLoggingTests.PhysicalCardIdentityAndPrivacy),
                ("Issue105 Settings Remove partial and absent evidence", Issue105SettingsLoggingTests.RemovalPartialAndAbsentEvidence),
                ("Issue105 Settings Runtime phases and recovery", SavedConfigurationReloadTests.LoggingPhasesAndRecovery),
                ("Issue105 Editor edit identities and property privacy", Issue105EditorLoggingTests.EditIdentitiesAndPrivacy),
                ("Issue105 Editor gesture checkpoint and no-op", Issue105EditorLoggingTests.GestureCheckpointAndNoOp),
                ("Issue105 Editor hierarchy and paste correlation", Issue105EditorLoggingTests.HierarchyAndPasteCorrelation),
                ("Issue105 Editor edit evidence bounds", Issue105EditorLoggingTests.EditEvidenceBounds),
                ("Fixture cleanup original failures and no retry", RegressionFixtureCleanupTests.OriginalFailuresAndNoRetry),
                ("Fixture cleanup held-file snapshot and native ABI", RegressionFixtureCleanupTests.HeldFileSnapshotAndNativeLayout),
                ("Issue105 pending cleanup preserves ownership", Issue105CleanupLoggingTests.PendingCleanupDoesNotReleaseOwnership),
                ("Issue105 cleanup finally and original failures", Issue105CleanupLoggingTests.CleanupFailuresKeepFinallyAndOriginalErrors),
                ("Issue105 cleanup observer failure isolation", Issue105CleanupLoggingTests.ObserverFaultDoesNotReplaceCleanupFailure),
                ("Issue105 Desktop truthful cleanup evidence", Issue105CleanupLoggingTests.DesktopCleanupDoesNotReportFalseSuccess),
                ("Issue105 preview shutdown worker fence evidence", Issue105CleanupLoggingTests.PreviewShutdownEvidenceRespectsWorkerFence),
                ("Issue105 preview source cleanup aggregate evidence", Issue105CleanupLoggingTests.PreviewSourceFailuresRemainOneAggregate),
                ("Issue105 USB observer failure preserves completion", Issue105UsbLoggingTests.ObserverFailurePreservesNativeCompletion),
                ("Issue105 USB bounded evidence and unknown counts", Issue105UsbLoggingTests.BoundedEvidenceAndUnknownCounts),
                ("Issue105 USB healthy sparse stream and exact I/O", Issue105UsbLoggingTests.HealthyStreamIsSparseAndExact),
                ("Issue105 USB malformed frame evidence and no retry", Issue105UsbLoggingTests.MalformedFrameBoundariesRetained),
                ("Issue105 USB pending cancellation and native ownership", Issue105UsbLoggingTests.PendingCancellationKeepsNativeOwnership),
                ("Issue105 USB native failure during cancellation", Issue105UsbLoggingTests.NativeFailureDuringCancellationIsRetained),
                ("Issue105 USB slow completion and failed acquisition", Issue105UsbLoggingTests.SlowCompletionAndOpenFailure),
                ("Issue105 operation outcomes and parent correlation", Issue105EditorLoggingTests.OperationOutcomesAndCorrelation),
                ("Issue105 per-incident recovery flush", Issue105EditorLoggingTests.RecoveryFlushPreservesOtherIncidents),
                ("Issue105 Editor load save and history evidence", Issue105EditorLoggingTests.LoadSaveAndHistory),
                ("Issue105 Editor locked JSON save evidence", Issue105EditorLoggingTests.LockedJsonSaveFailure),
                ("Issue105 Editor metadata partial save evidence", Issue105EditorLoggingTests.MetadataPartialSave),
                ("Issue105 Editor failed load preserves document", Issue105EditorLoggingTests.FailedLoadPreservesDocument),
                ("Issue105 Editor invalid metadata backup evidence", Issue105EditorLoggingTests.InvalidMetadataLoadBackup),
                ("Issue105 Errors routing and exception correlation", Issue105LoggingTests.ErrorRoutingAndPropagation),
                ("Issue105 throttle totals and changed native failures", Issue105LoggingTests.ThrottledFinalTotalsAndChangedNativeError),
                ("Issue105 retained history and failed sink", Issue105LoggingTests.AppendHistoryPartialTailAndSinkFailure),
                ("Issue105 component and overlapping Editor sessions", Issue105LoggingTests.ComponentAndOverlappingEditorSessions),
                ("Issue105 concurrent writers and abandoned gate", Issue105LoggingTests.ConcurrentWritersAndAbandonedGate),
                ("Issue105 crashed final Editor session", Issue105LoggingTests.CrashedLastEditorReleasesSession),
                ("Issue105 72-hour rolling history and idle pruning", Issue105LoggingTests.RollingRetentionAndIdleErrors),
                ("Issue105 idle session prunes component and error history", Issue105LoggingTests.IdleSessionPrunesAllThreeStreams),
                ("Issue105 configurable retention and invalid policy", Issue105LoggingTests.ConfigurableRetentionAcrossSessions),
            ]),
            ("Widgets / Shortcut", [
                ("Issue101 prototype raster region and dispatch parity", ShortcutInputPrototypeTests.RegionAndDispatchAgree),
                ("Issue101 Shortcut model round-trip and defaults", ShortcutModelTests.RoundTripAndDefaults),
                ("Issue101 Shortcut migration and strictness", ShortcutModelTests.MigrationAndStrictness),
                ("Issue101 Shortcut validation and source ownership", ShortcutModelTests.ValidationAndSourceOwnership),
                ("Issue101 initial label suggestion and preservation", ShortcutTargetEditingTests.InitialLabelAndPreservation),
                ("Issue101 empty label persistence without suggestions", ShortcutTargetEditingTests.PersistenceNeverSuggestsLabel),
                ("Issue101 Target property label and snapshot integration", ShortcutEditorTests.TargetEditingAndSnapshots),
                ("Issue101 production input raster and snapshot", ShortcutInteractionTests.RasterAndSnapshot),
                ("Issue101 production gesture and Shell boundary", ShortcutInteractionTests.GesturesAndShellBoundary),
                ("Issue101 label composition effects and shared artwork", ShortcutRenderTests.LabelsEffectsAndSharedArtwork),
                ("Issue101 Shell artwork cache and retirement", ShortcutRenderTests.ShellArtworkCacheLifetime),
                ("Issue101 Editor properties and applicability", ShortcutEditorTests.PropertiesAndApplicability),
                ("Issue101 clipboard imports artwork without targets", ShortcutEditorTests.ClipboardCopiesOnlyArtwork),
                ("Issue101 prototype separated union and overlap order", ShortcutInputPrototypeTests.UnionAndOverlap),
                ("Issue101 prototype click modes and cancellation", ShortcutInputPrototypeTests.ClickModesAndCancellation),
                ("Issue101 prototype timing distance and generation", ShortcutInputPrototypeTests.TimeDistanceAndIdentity),
                ("Issue101 native experiment explicit admission", ShortcutInputPrototypeTests.DiagnosticAdmission),
                ("Issue101 underlay stdin preserves message-loop responsiveness", ShortcutInputPrototypeTests.LifetimeReadDoesNotBlockCaller),
            ]),
            ("Devices / USB Test & Save", [
                ("Issue95 live Test WorkerCompletionAndVirtualBoard", Issue95LiveTestTests.WorkerCompletionAndVirtualBoard),
                ("Issue95 live Test StrictWorkerFailureAndCleanup", Issue95LiveTestTests.StrictWorkerFailureAndCleanup),
                ("Issue95 live Test CancellationWaitsForNativeRelease", Issue95LiveTestTests.CancellationWaitsForNativeRelease),
                ("Issue95 live Test ExistingOwnerAndResponseCorrelation", Issue95LiveTestTests.ExistingOwnerAndResponseCorrelation),
                ("Issue95 live Test EditorLaunchAndStrictResults", () => Issue95LiveTestTests.EditorLaunchAndStrictResults().GetAwaiter().GetResult()),
                ("Issue95 candidate per-card Save merge and working board", Issue95CandidateSaveTests.PerCardMergeAndWorkingBoard),
                ("Issue95 candidate Save stale binding and apply rollback", Issue95CandidateSaveTests.StaleBindingAndApplyFailure),
                ("Issue95 configured card isolated Save and stale assignment", Issue95CandidateSaveTests.ConfiguredCardIsolationAndStaleWrite),
                ("Configured device Save: Runtime start decision outcomes", Issue95ConfiguredRuntimePromptTests.SaveStartDecisions),
                ("Issue95 Forget exact node, rollback and manual recovery", Issue95CandidateSaveTests.ForgetExactNodeAndRollback),
                ("Issue95 Remove selected card and partial state", Issue95CandidateSaveTests.RemoveCardIsolationAndPartialState),
                ("Issue95 candidate live action states", Issue95ProtocolChoiceTests.LiveActionStates),
            ]),
            ("Devices / Frame & Configuration", [
                ("Issue95 exact unscaled pixel mapping", Issue95FrameGeometryTests.ExactPixelMapping),
                ("Issue95 ordinary worker ignores panel dimensions", Issue95FrameGeometryTests.OrdinaryWorkerIgnoresPanelSize),
                ("Issue95 raster and wire resource bounds", Issue95FrameGeometryTests.ResourceAndWireBounds),
                ("Issue95 composite snapshot completeness", Issue95CatalogCandidateTests.CompositeSnapshotCompleteness),
                ("Issue95 catalog protocol choices and draft admission", Issue95ProtocolChoiceTests.CatalogChoicesAndDraftAdmission),
                ("Issue95 candidate explicit choice survives refresh", Issue95ProtocolChoiceTests.CandidateRefreshPreservesExplicitChoice),
                ("Issue95 protocol radio availability and reconnect", Issue95ProtocolChoiceTests.RadioAvailabilityAndReconnect),
                ("Issue95 LY endpoint admission", Issue95GenericOutputTests.LyEndpointAdmission),
                ("Issue95 generic Output round trip and ownership", Issue95GenericOutputTests.NewFormatRoundTripAndOwnership),
                ("Issue95 required geometry protocol and binding", Issue95GenericOutputTests.RequiredGeometryProtocolAndBinding),
                ("Issue95 candidate confirmation and copy isolation", Issue95GenericOutputTests.CandidateCannotBypassConfirmation),
            ]),
            ("Devices / Transport & Recovery", [
                ("Physical handshake context and bounded evidence", PhysicalHandshakeDiagnosticTests.CommonContextAndBounds),
                ("Physical handshake quiet paths and original error identity", PhysicalHandshakeDiagnosticTests.ObserverPreservesErrorsAndQuietPaths),
                ("Physical handshake Trofeo actual handshake replies", PhysicalHandshakeDiagnosticTests.TrofeoHandshakeReplies),
                ("Physical handshake Trofeo I/O failure ordering", PhysicalHandshakeDiagnosticTests.TrofeoHandshakeIoErrors),
                ("Physical handshake TURZX short primary replies", PhysicalHandshakeDiagnosticTests.TurzxShortReplies),
                ("Physical handshake TURZX follow-up and unknown counts", PhysicalHandshakeDiagnosticTests.TurzxFollowUpAndUnknownCounts),
                ("Physical handshake healthy sync and frame exclusion", PhysicalHandshakeDiagnosticTests.HealthySyncDoesNotCaptureFrames),
                ("Physical handshake shared factory admission and ordinary log", PhysicalHandshakeDiagnosticTests.FactoryLogsBeforeHardwareAdmission),
                ("Issue71 sync capture exact bytes and single exchange", TurzxTransportDiagnosticTests.SyncCapturePreservesExactReplyAndSingleExchange),
                ("Issue71 sync capture admission and failure lifetime", TurzxTransportDiagnosticTests.SyncCaptureAdmissionAndFailureLifetime),
                ("Issue71 recovery complete metadata before removal", Issue71ModelTests.RecoveryMetadataCannotInventRemoval),
                ("Issue71 recovery exact reattachment after send/open failure", Issue71RecoveryTests.ReattachmentRecoversSendAndOpenFailure),
                ("Issue71 recovery trustworthy absence and exact return", Issue71RecoveryTests.RecoveryRequiresTrustworthyAbsenceAndExactReturn),
                ("Issue71 recovery stale discovery and lifecycle isolation", Issue71RecoveryTests.StaleRecoveryScanCannotSurviveLifecycleChange),
                ("Issue71 recovery bounded checkpoint stays single-attempt", Issue71RecoveryTests.BoundedCheckpointNeverRecoversAutomatically),
                ("Issue71 recovery cleanup failure blocks acquisition", Issue71RecoveryTests.FailedDisposalCannotAuthorizeRecovery),
                ("Issue71 adoption MissingBindingStillAllowsPassiveRetry", Issue71AdoptionTests.MissingBindingStillAllowsPassiveRetry),
                ("Issue71 connected TURZX Settings controls", Issue71SettingsTests.ConnectedSettingsControls),
                ("Issue71 default physical factory and sibling isolation", Issue71SettingsTests.DefaultPhysicalFactoryAndSiblingIsolation),
                ("Issue71 adoption PartialDiscoveryIsolation", Issue71AdoptionTests.PartialDiscoveryIsolation),
                ("Issue71 adoption ConsentPersistenceAndCancel", Issue71AdoptionTests.ConsentPersistenceAndCancel),
                ("Issue71 adoption StaleConsentAndUnsupportedOptions", Issue71AdoptionTests.StaleConsentAndUnsupportedOptions),
                ("Issue71 adoption ReconciliationUsesExactAdapter", Issue71AdoptionTests.ReconciliationUsesExactAdapter),
                ("Issue71 adoption FaultedTurzxDoesNotAutomaticallyReopen", Issue71AdoptionTests.FaultedTurzxDoesNotAutomaticallyReopen),
                ("Issue71 adoption FailedTurzxOpenAndTrofeoRetryIsolation", Issue71AdoptionTests.FailedTurzxOpenAndTrofeoRetryIsolation),
                ("Issue71 Runtime GeometryAndLegacyParity", Issue71RuntimeTests.GeometryAndLegacyParity),
                ("Issue71 Runtime CanonicalRendererGeometryAndIsolation", Issue71RuntimeTests.CanonicalRendererGeometryAndIsolation),
                ("Issue71 Runtime EncodedPayloadBound", Issue71RuntimeTests.EncodedPayloadBound),
                ("Issue71 Runtime AdapterAdmission", Issue71RuntimeTests.AdapterAdmission),
                ("Issue71 Runtime CheckpointAdmissionAndPreflight", Issue71RuntimeTests.CheckpointAdmissionAndPreflight),
                ("Issue71 Runtime SharedWorkerLiveFrames", Issue71RuntimeTests.SharedWorkerLiveFrames),
                ("Issue71 Runtime SharedWorkerFailureNoReopen", Issue71RuntimeTests.SharedWorkerFailureNoReopen),
                ("Issue71 Runtime FrameBudgetStopsFurtherAdmission", Issue71RuntimeTests.FrameBudgetStopsFurtherAdmission),
                ("Issue71 Runtime StaticPayloadIsNotAnimation", Issue71RuntimeTests.StaticPayloadIsNotAnimation),
            ]),
            ("Editor / Documents", [
                ("Issue70 empty image source validation and render", Issue70EmptyImageSourceTests.CanonicalAndRendering),
                ("Issue70 shared Editor settings concurrent merge", Issue70ConcurrentEditorSettingsTests.MergesDashboardViews),
                ("Issue70 Editor save with an open settings snapshot", Issue70ConcurrentEditorSettingsTests.SaveWithOpenSnapshot),
                ("Issue70 widget clipboard package version and payload", Issue70ClipboardTests.PackageRoundTrip),
                ("Issue70 nested widget paste plan and Save/reopen", Issue70PastePlannerTests.TransfersNestedGroups),
                ("Issue70 malformed group paste isolation", Issue70PastePlannerTests.RejectsMalformedMappingsWithoutMutation),
                ("Issue70 decline image import preserves properties", Issue70ImageImportTests.DeclinePreservesWidgetConfigurations),
                ("Issue70 collision and identical image reuse", Issue70ImageImportTests.ImportAndCollisionChoices),
                ("Issue70 cancel and invalid image path safety", Issue70ImageImportTests.CancelAndInvalidPaths),
                ("Issue84 startup selection and theme-restart arguments", Issue84EditorStartupTests.SelectionAndRestartArguments),
                ("Issue84 document-free Editor commands", Issue84EditorStartupTests.EmptyWorkspaceControls),
                ("Issue102 current-schema load and clean switching", Issue102EditorDirtyTests.CleanLoadAndSwitch),
                ("Issue102 migration remains dirty until saved", Issue102EditorDirtyTests.MigrationAndSave),
                ("Issue102 normalization remains dirty through undo", Issue102EditorDirtyTests.NormalizationAndUndo),
                ("Issue102 user edits undo redo and Save", Issue102EditorDirtyTests.EditUndoRedoAndSave),
                ("Issue98 middle-mouse canvas pan delta and scroll bounds", Issue98CanvasPanTests.DeltaAndBounds),
                ("Issue93 new dashboard creation and save/reopen", Issue93NewDashboardTests.CreateSaveAndReopen),
            ]),
            ("Dashboard / Defaults", [
                ("Issue94 adaptive dashboard geometry and semantics", Issue94AdaptiveDashboardTests.GeneratorGeometryAndSemantics),
                ("Issue94 infeasible canvas rejection", Issue94AdaptiveDashboardTests.RejectInfeasibleGeometry),
                ("Issue94 Desktop default staged assignment", Issue94DesktopDefaultAssignmentTests.StagingAndReuse),
                ("Issue94 Desktop default rollback and collisions", Issue94DesktopDefaultAssignmentTests.RollbackAndCollisions),
                ("Issue95 user-defined physical board naming", Issue95PhysicalDashboardNamesTests.UserGeometryNaming),
                ("Issue94 integrated physical native default assignment", Issue94PhysicalDefaultAssignmentTests.IntegratedProfilesAndReuse),
                ("Issue94 physical default rollback and unprofiled adapter", Issue94PhysicalDefaultAssignmentTests.FailureAndUnsupportedProfiles),
                ("Issue94 first Desktop bootstrap and missing-file protection", Issue94FreshBootstrapTests.FirstDesktopAndNoRepair),
                ("Issue94 first physical bootstrap and safe rollback", Issue94FreshBootstrapTests.PhysicalFirstAndCancel),
            ]),
            ("Infrastructure / Launcher", [
                ("Launcher and About shared window and shortcut contract", LauncherPackagingContractTests.SharedWindowAndShortcuts),
                ("Issue65 launcher exits only after successful start", LauncherPackagingContractTests.ClosesAfterSuccessfulLaunchOnly),
            ]),
            ("Runtime / Idle", [
                ("Issue60 idle timeout policy and config persistence", RuntimeIdleTests.PolicyAndConfig),
                ("Issue60 Windows power policy refresh", RuntimeIdleTests.PowerPolicyRefresh),
                ("Issue60 telemetry pause and no catch-up", RuntimeIdleTests.TelemetryDoesNotCatchUp),
                ("Issue72 physical initial telemetry frame with long interval", RuntimeIdleTests.InitialTelemetryWakesLongIntervalPhysicalFrame),
                ("Issue60 Desktop and physical idle lifecycle", RuntimeIdleTests.OutputsPreserveStateAndTransport),
                ("Issue60 in-flight USB safe idle pause", RuntimeIdleTests.InFlightUsbPause),
            ]),
            ("Telemetry / Providers", [
                ("Telemetry provider failure isolation", TelemetryProviderFailureIsIsolated),
                ("Issue92 PC catalog, types and selector", Issue92PcTelemetryTests.CatalogAndSelector),
                ("Issue92 PC installed memory and architecture values", Issue92PcTelemetryTests.ValueNormalization),
                ("Issue92 PC static snapshot demand and unavailable isolation", Issue92PcTelemetryTests.StaticSnapshotAndIsolation),
                ("Telemetry reconfiguration clears deactivated metrics", TelemetryReconfigurationClearsDeactivatedMetrics),
                ("Issue72 initial telemetry capture notification and wake", OutputTelemetryDemandTests.InitialCaptureNotifiesOnce),
                ("Telemetry reconfiguration prepare/commit isolation", TelemetryReconfigurationPrepareCommitIsolation),
            ]),
            ("Devices / Transport & Options", [
                ("Issue71 mixed configuration compatibility", Issue71ModelTests.CompatibleConfiguration),
                ("Issue71 unsafe ownership rejection", Issue71ModelTests.ConfigurationRejectsUnsafeOwnership),
                ("Issue71 brightness application policy", Issue71ModelTests.BrightnessPolicy),
                ("Issue71 exact passive WinUSB interfaces", Issue71ModelTests.ExactPassiveInterfaces),
                ("Issue71 missing and removed metadata", Issue71ModelTests.MissingAndRemovedMetadata),
                ("Issue71 binding and inventory isolation", Issue71ModelTests.BindingAndInventory),
                ("Issue71 detached drafts and unsupported adapter options", Issue71ModelTests.DetachedDraftAndAdapterOptions),
                ("Issue71 registered interface class decoding", Issue71ModelTests.RegisteredClassDecoding),
                ("Issue71 absent TURZX Settings and tray", Issue71SettingsTests.AbsentSettingsAndTrayRemainReadOnly),
                ("Issue71 default factory rejects mismatched adapter", Issue71SettingsTests.DefaultFactoryRejectsMismatchedTransport),
                ("Issue71 TURZX independent encrypted packet fixtures", Issue71TransportTests.PacketFixtures),
                ("Issue71 TURZX packet bounds and codec identity", Issue71TransportTests.PacketBounds),
                ("Issue71 TURZX command-specific reply classification", Issue71TransportTests.CommandReplies),
                ("Issue71 TURZX sequence and empty brightness boundary", Issue71TransportTests.SequenceAndBrightnessBoundary),
                ("Issue71 TURZX command admission and no automatic retry", Issue71TransportTests.AdmissionAndNoRetry),
                ("Issue71 TURZX read failures and stale reply bytes", Issue71TransportTests.ReadFailuresAndStaleBytes),
                ("Issue71 TURZX exact open and failed sync cleanup", Issue71TransportTests.ExactOpenAndFailureCleanup),
                ("Issue71 TURZX native completion and error identity", Issue71TransportTests.NativeCompletionAndErrors),
                ("Issue71 TURZX native cancellation drains before release", Issue71TransportTests.NativeCancellationLifetime),
                ("Issue71 TURZX deadline and concurrent disposal", Issue71TransportTests.NativeDeadlineAndDispose),
                ("Issue71 TURZX native ABI and endpoint validation", Issue71TransportTests.NativeAbiAndEndpoints),
                ("Issue71 TURZX transport disposal drains command", Issue71TransportTests.TransportDisposalDrainsCommand),
                ("Issue71 TURZX pending native success", Issue71TransportTests.PendingNativeSuccess),
                ("Issue71 TURZX commands remain serialized", Issue71TransportTests.CommandsRemainSerialized),
                ("Issue71 TURZX JPEG stream owns complete replies", Issue71TransportTests.JpegStreamOwnsReplyBoundaries),
                ("Issue71 TURZX JPEG stream uncertain-boundary rejection", Issue71TransportTests.JpegStreamRejectsUncertainBoundaries),
                ("Issue71 TURZX JPEG stream serializes follow-up", Issue71TransportTests.JpegStreamSerializesFollowUp),
                ("Issue71 TURZX JPEG stream native follow-up lifetime", Issue71TransportTests.JpegStreamFollowUpNativeLifetime),
                ("Issue71 TURZX passive diagnostic explicit admission", TurzxPassiveDiagnosticTests.ExplicitAdmission),
                ("Issue71 TURZX passive diagnostic exact metadata and privacy", TurzxPassiveDiagnosticTests.ExactMetadataAndPrivateOutput),
                ("Issue71 TURZX passive diagnostic missing and ambiguous interfaces", TurzxPassiveDiagnosticTests.MissingAmbiguousAndRemoved),
                ("Issue71 TURZX passive diagnostic sanitized errors", TurzxPassiveDiagnosticTests.ErrorsDoNotLeakPrivateMessages),
                ("Issue71 TURZX transport diagnostic explicit consent", TurzxTransportDiagnosticTests.ExplicitConsentAndArguments),
                ("Issue71 TURZX transport diagnostic one command and empty brightness", TurzxTransportDiagnosticTests.OneCommandAndEmptyBrightness),
                ("Issue71 TURZX transport diagnostic failure isolation", TurzxTransportDiagnosticTests.FailuresStopBeforeFurtherIo),
                ("Issue71 TURZX two JPEG frames on one connection", TurzxTransportDiagnosticTests.JpegPairSequenceAndFailures),
                ("Issue71 TURZX bounded post-image reply reads", TurzxTransportDiagnosticTests.JpegPairReadBoundaries),
                ("Issue71 TURZX diagnostic uses stream transport owner", TurzxTransportDiagnosticTests.JpegStreamUsesTransport),
                ("Issue71 TURZX synthetic codecs, geometry and corners", TurzxTransportDiagnosticTests.SyntheticCodecGeometryAndCorners),
            ]),
            ("Devices / Test & Discovery", [
                ("Output device discovery identity", OutputDeviceDiscoveryIdentity),
                ("Issue95 exclusive Runtime process ownership", Issue95RuntimeProcessLeaseTests.ConcurrentOwnersFailClosed),
                ("Issue95 Runtime lease release and restart", Issue95RuntimeProcessLeaseTests.ReleasedOwnerAllowsNewAcquisition),
                ("Issue95 USB Test typed IPC roundtrip", Issue95UsbTestIpcTests.RoundtripAndCorrelation),
                ("Issue95 USB Test IPC rejects malformed and unsafe requests", Issue95UsbTestIpcTests.MalformedRequestsFailClosed),
                ("Issue95 bounded Runtime IPC request admission", Issue95UsbTestIpcTests.BoundedServerRequestAdmission),
                ("Issue95 transient Test exclusive session and correlation", Issue95RuntimeUsbTestSessionTests.ExclusiveAdmissionAndCorrelation),
                ("Issue95 transient Test cancellation and native release", Issue95RuntimeUsbTestSessionTests.CancelAndNativeRelease),
                ("Issue95 transient Test client loss and deadlines", Issue95RuntimeUsbTestSessionTests.SilenceDeadlineAndUncertainCleanup),
                ("Issue95 Runtime Test exact passive preflight", Issue95RuntimeUsbPreflightTests.ExactRuntimeAdmissionAndNegativeCases),
                ("Issue95 Runtime Test protocol and geometry preflight", Issue95RuntimeUsbPreflightTests.ProtocolAndGeometryFailClosed),
                ("Issue95 Runtime Test coordinator enforces fresh preflight", Issue95RuntimeUsbPreflightTests.CoordinatorRequiresFreshPreflightBeforeLease),
                ("Issue95 catalog VID/PID candidate admission", Issue95CatalogCandidateTests.CatalogBoundedPassiveCandidates),
                ("Issue95 candidate draft exact trial and invalidation", Issue95CandidateDraftTests.EditableParametersAndRuntimeIntent),
                ("Issue95 candidate draft invalid and stale binding", Issue95CandidateDraftTests.InvalidAndUnprovenCandidatesFailClosed),
                ("Issue95 clean runtime plus visual approval gates Save", Issue95CandidateDraftTests.OnlyCleanRuntimeAndPositiveVisualAllowSave),
                ("Issue95 failed USB result and native cleanup block Save", Issue95CandidateDraftTests.TechnicalFailureAndNativeCleanupFailClosed),
                ("Issue95 exact passive interface correlation", Issue95CatalogCandidateTests.ExactCorrelationAndAmbiguity),
                ("Issue95 exact candidate selection and per-instance Forget", Issue95CatalogCandidateTests.ExactSelectionAndPerInstanceIgnore),
                ("Issue95 multi-interface candidate ambiguity", Issue95CatalogCandidateTests.MultiInterfaceSelectionFailsClosed),
                ("Issue95 strict catalog path and driver rejection", Issue95CatalogCandidateTests.StrictPathAndMissingDriver),
            ]),
            ("Devices / Identity & Inventory", [
                ("Issue77 catalog and exact USB devnode parsing", Issue77UsbDiscoveryTests.CatalogAndExactParsing),
                ("Issue77 passive catalog capture without unrelated reads", Issue77UsbDiscoveryTests.PassiveCatalogCapture),
                ("Issue77 composite grouping and distinct units", Issue77UsbDiscoveryTests.CompositeAndDistinctUnits),
                ("Issue77 conflicting and missing metadata", Issue77UsbDiscoveryTests.ConflictingAndMissingMetadata),
                ("Issue77 bounded CM buffer races and errors", Issue77UsbDiscoveryTests.BufferRacesAndErrors),
                ("Issue77 removal races and private observation identity", Issue77UsbDiscoveryTests.RemovalAndPrivateIdentity),
                ("Issue77 exact devnode metadata and identity namespaces", Issue77DeviceIdentityTests.ExactDevnodeMetadata),
                ("Issue77 supported and informational reconciliation", Issue77DeviceIdentityTests.ReconcileDistinctAndCompositeDevices),
                ("Issue77 exact bindings and devnode ambiguity", Issue77DeviceIdentityTests.BindingAndAmbiguity),
                ("Issue77 recognition versus transport dispatch", Issue77DeviceIdentityTests.RecognitionAndDispatchContract),
                ("Issue77 informational draft identity and status lifecycle", Issue77DeviceIdentityTests.InformationalDraftLifecycle),
                ("Issue77 compatible persistence and stale consent", Issue77DeviceIdentityTests.CompatiblePersistenceAndConsent),
                ("Issue77 read-only tabs and independent discovery failures", Issue77DevicesUiTests.ReadOnlyTabsAndIndependentFailures),
                ("Output target selection", OutputTargetSelection),
                ("Stage16 passive discovery and reconnect identity", DeviceDiscoveryStage16Tests.PassiveReconnect),
                ("Stage16 duplicate configured identity fails closed", DeviceDiscoveryStage16Tests.DuplicateIdentity),
                ("Stage16 verified USB instance fingerprint parsing", DeviceDiscoveryStage16Tests.PassiveSerialFingerprint),
                ("Stage16 duplicate physical serial fingerprint fails closed", DeviceDiscoveryStage16Tests.DuplicateSerialFingerprint),
                ("Stage16 physical inventory known and new snapshots", DeviceDiscoveryStage16Tests.InventoryPreservesKnownAndNew),
                ("Stage16 physical inventory ambiguity and no rebind", DeviceDiscoveryStage16Tests.InventoryNeverRebindsAmbiguousHardware),
                ("Stage16 native inventory capture isolation", DeviceDiscoveryStage16Tests.InventoryCaptureIsolation),
                ("Stage17 physical device enable consent policy", DeviceDiscoveryStage16Tests.DeviceEnabledConsentPolicy),
                ("Settings physical Output removal and disconnect policy", DeviceDiscoveryStage16Tests.DeviceRemovalPolicy),
            ]),
            ("Outputs / Configuration & Reload", [
                ("Output config legacy default and persistence", OutputConfigLegacyDefaultAndPersistence),
                ("Output config multi-target safety", OutputConfigMultiTargetSafety),
                ("Output per-instance configuration persistence", OutputConfigurationModelTests.Persistence),
                ("Issue63 compatible Enabled persistence and consumer policy", OutputEnabledContractTests.PersistenceAndConsumers),
                ("Output desktop-only configuration", OutputConfigurationModelTests.DesktopOnly),
                ("Output per-instance invalid references and identity", OutputConfigurationModelTests.InvalidReferences),
                ("Issue94 application root without dashboard folder", RuntimeConfigurationStartupTests.RootWithoutDashboardDirectory),
                ("Runtime config clean desktop-only bootstrap", RuntimeConfigurationStartupTests.DesktopOnly),
                ("Runtime config mixed enablement and reload", RuntimeConfigurationStartupTests.MixedTargets),
                ("Runtime config invalid dashboard and malformed input", RuntimeConfigurationStartupTests.InvalidInput),
                ("Per-output dashboard assignment isolation", OutputDashboardOwnershipTests.IndependentAssignments),
                ("Per-output shared board and targeted reload", OutputDashboardOwnershipTests.SharedBoardTargetReload),
                ("Per-output dashboard transaction rollback", OutputDashboardOwnershipTests.Rollback),
                ("Issue32 deleted image Desktop selected reload", DeletedImageRollbackTests.SelectedReload),
                ("Issue32 deleted image multi-Output rollback", DeletedImageRollbackTests.MultiOutputRollback),
                ("Issue32 physical renderer retained rollback", DeletedImageRollbackTests.PhysicalRendererRollback),
                ("Issue63 selected saved settings and shared-board isolation", SavedOutputReloadTests.SavedSettingsIsolation),
                ("Issue63 physical inherited settings and reload ownership", SavedOutputReloadTests.PhysicalDefaultsAndOwnership),
                ("Issue63 selected reload validation and read-only failures", SavedOutputReloadTests.ValidationAndReadOnlyFailures),
                ("Issue63 selected reload rollback and concurrent writes", SavedOutputReloadTests.RollbackAndConcurrentWrite),
                ("Issue63 selected reload global lifetime and feedback", SavedOutputReloadTests.LifecycleAndFeedback),
                ("Issue63 All common settings and enabled dashboard isolation", SavedConfigurationReloadTests.CommonSettingsAndEnabledDocuments),
                ("Issue63 All empty enabled set and optional telemetry defaults", SavedConfigurationReloadTests.EmptyEnabledSetAndMissingTelemetry),
                ("Issue63 All full-record validation before mutation", SavedConfigurationReloadTests.ValidationBeforeMutation),
                ("Issue63 disabled startup documents and explicit enable", SavedConfigurationReloadTests.DisabledStartupAndExplicitEnable),
                ("Issue63 All common-subsystem rollback and recovery failures", SavedConfigurationReloadTests.RollbackAcrossSubsystems),
                ("Issue63 All intervening app and telemetry writes", SavedConfigurationReloadTests.InterveningConfigurationWrites),
                ("Issue63 All global lifetime, suspension and wire command", SavedConfigurationReloadTests.GlobalLifetimeSuspensionAndWire),
            ]),
            ("Telemetry / Output Metrics", [
                ("Output telemetry demand active union", OutputTelemetryDemandTests.ActiveUnion),
                ("Output telemetry demand activation and clearing", OutputTelemetryDemandTests.Transitions),
                ("Output telemetry demand independent Desktop switches", OutputTelemetryDemandTests.DesktopPolicy),
                ("Output metric identity and physical catalog", OutputPerformanceMetricTests.IdentityAndCatalog),
                ("Output metric multi-session isolation", OutputPerformanceMetricTests.PublisherIsolation),
                ("Output metric clearing stop and restart", OutputPerformanceMetricTests.StopAndRestart),
            ]),
            ("Outputs / Desktop", [
                ("Issue78 unavailable native Desktop window isolation and recovery", DesktopSessionStage09Tests.UnavailableExplorerHost),
                ("Desktop Z-order policy and persistence", DesktopZOrderStage14Tests.PolicyAndPersistence),
                ("Desktop Z-order lifecycle reconciliation", DesktopZOrderStage14Tests.SessionLifecycle),
                ("Multiple Desktop session dashboard and removal isolation", DesktopMultiSessionStage15Tests.SharedBoardReloadAndRemoval),
                ("Multiple Desktop session failed-start recovery", DesktopMultiSessionStage15Tests.FailedPanelCreationIsolation),
            ]),
            ("Telemetry / Output Providers", [
                ("Dynamic output provider registration", OutputProviderStage07Tests.ProviderCatalog),
                ("Dynamic output selector branches and switches", OutputProviderStage07Tests.Selector),
                ("Output provider passive status and removal", OutputProviderStage07Tests.PassiveLifecycle),
                ("Output provider active isolation without System", OutputProviderStage07Tests.LiveIsolation),
            ]),
            ("Outputs / Physical", [
                ("Trofeo wire framing contract", TrofeoWireFramingContract),
                ("Trofeo USB transfer segmentation contract", TrofeoUsbTransferSegmentationContract),
                ("Issue104 retired USB options ignored and omitted", Issue104LoggingTests.RetiredOptionsIgnoredAndOmitted),
                ("Issue104 diagnostics row removed and provider retained", Issue104LoggingTests.RowsRemovedAndProviderRetained),
                ("Issue104 shared physical events and error routing", Issue104LoggingTests.SharedPhysicalEventsAndFailures),
                ("Issue104 stale logging changes do not reconnect", Issue104LoggingTests.RetiredFlagDoesNotReconnect),
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
                ("Issue95 selected physical Remove native release and sibling isolation", FramePumpShutdownTests.SelectedPhysicalRemovalRelease),
                ("Issue95 acquisition cleanup uncertainty blocks Remove", FramePumpShutdownTests.AcquisitionCleanupFailureBlocksRemoval),
                ("Frame pump suspend during blocked USB open", FramePumpSuspendDuringBlockedOpen),
                ("Frame pump suspend during blocked USB send", FramePumpSuspendDuringBlockedSend),
                ("Frame pump reconfigure during blocked USB send", FramePumpReconfigureDuringBlockedSend),
                ("Stage27 blocked USB leaves Desktop and shared telemetry responsive", BlockedTransportStage27Tests.DesktopAndTelemetryContinueDuringBlockedPhysicalSend),
            ]),
            ("Outputs / Shared Lifecycle", [
                ("Stage20 per-device USB handshake settings and reconfigure isolation", FramePumpPerDeviceUsbSettings),
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
            ("Runtime / Output Control", [
                ("Stage24 independent Output status and disabled hardware", RuntimeStatusStage24Tests.IndependentOutputStatus),
                ("Stage24 additive IPC status and legacy payload compatibility", RuntimeStatusStage24Tests.StatusWireCompatibility),
                ("Stage24 tray Output actions and Desktop startup", RuntimeStatusStage24Tests.TrayAndStartupPolicy),
                ("Issue63 native tray root order and command dispatch", RuntimeTrayMenuTests.RootOrderAndDispatch),
                ("Issue63 Desktop tray Enabled gating and provider-off frames", RuntimeTrayMenuTests.DesktopActionsAndProviderOffFrames),
                ("Issue63 tray refresh ownership and unavailable measurements", RuntimeTrayMenuTests.RefreshOwnershipAndUnavailableFrames),
                ("Issue63 physical root placement and consent callback", RuntimeTrayMenuTests.PhysicalPlacementAndConsentBridge),
                ("Issue63 physical Enabled consent and exact identity", PhysicalOutputEnablementTests.ConsentAndExactIdentity),
                ("Issue63 physical Enabled selected persistence and isolation", PhysicalOutputEnablementTests.SelectedPersistenceAndIsolation),
                ("Issue63 physical Enabled concurrent input guards", PhysicalOutputEnablementTests.ConcurrentInputGuards),
                ("Issue63 physical Enabled apply and recovery failures", PhysicalOutputEnablementTests.ApplyAndRecoveryFailures),
                ("Issue63 physical Enabled lifetime, feedback and IPC boundary", PhysicalOutputEnablementTests.LifetimeFeedbackAndIpcBoundary),
                ("Stage24 target configuration IPC payloads", RuntimeConfigurationStage24Tests.ConfigurationProtocol),
                ("Stage24 dashboard assignment and passive hardware isolation", RuntimeConfigurationStage24Tests.AssignmentIsolation),
                ("Stage24 independent Desktop Board Enabled and Lock commands", RuntimeConfigurationStage24Tests.DesktopSwitches),
                ("Stage24 target configuration transaction rollback", RuntimeConfigurationStage24Tests.ConfigurationRollback),
                ("Stage24 concurrent persistence and stale binding guards", RuntimeConfigurationStage24Tests.ConcurrentPersistence),
                ("Stage24 global lifetime and suspended configuration commands", RuntimeConfigurationStage24Tests.GlobalLifetimeAndSuspendedState),
            ]),
            ("Editor / Output Control", [
                ("Stage25 dirty document Save, Don't Save, Cancel and failed Save", EditorOutputStartStage25Tests.DirtyDocumentDecisions),
                ("Stage25 selected Output dashboard choice and isolation", EditorOutputStartStage25Tests.SelectedDashboardChoices),
                ("Stage25 absent Runtime and normal launch policy", EditorOutputStartStage25Tests.AbsentRuntimePolicy),
                ("Stage25 failed apply and observed completion", EditorOutputStartStage25Tests.FailuresAndVerification),
                ("Stage25 All choice preflight and partial results", EditorOutputStartStage25Tests.AllPlanningAndPartialResults),
                ("Stage25 legacy status, assignment drift and menu ownership", EditorOutputStartStage25Tests.LegacyDriftAndMenu),
                ("Issue87 Editor Outputs grouping and refresh", EditorOutputStartStage25Tests.GroupedOutputMenu),
            ]),
            ("Runtime / Notifications", [
                ("Stage26 shared command result and target identity", RuntimeNotificationStage26Tests.SharedCommandResults),
                ("Stage26 pending readiness and one terminal notification", RuntimeNotificationStage26Tests.PendingAndTerminalOutcomes),
                ("Stage26 configuration observation and sibling isolation", RuntimeNotificationStage26Tests.ConfigurationAndIsolation),
                ("Stage26 failed, partial and unknown command outcomes", RuntimeNotificationStage26Tests.FailuresAndUnknownResults),
                ("Stage26 aggregate All command outcomes", RuntimeNotificationStage26Tests.AggregateAllOutcomes),
                ("Stage26 passive silence and superseded completion", RuntimeNotificationStage26Tests.QuietAndSupersededRequests),
                ("Stage26 notification failure and accepted shutdown", RuntimeNotificationStage26Tests.NotificationFailureAndShutdown),
                ("Issue86 concise banners and detailed command diagnostics", RuntimeNotificationStage26Tests.ConciseBannerAndFullDiagnostics),
            ]),
            ("Runtime / IPC", [
                ("Runtime IPC output control isolation", RuntimeIpcOutputControlIsolation),
                ("Runtime IPC shutdown cancellation contract", RuntimeIpcShutdownCancellationContract),
                ("Runtime normal startup and unsupported arguments", RuntimeOutputStartupPolicy),
                ("Output menu device command model", ScreenMenuDeviceCommandModel),
            ]),
            ("Editor / Shell", [
                ("Editor main-menu Group cleanup", EditorMainMenuGroupCleanup),
                ("Editor Outputs toolbar right inset", EditorOutputsToolbarRightInset),
                ("Editor native title-bar layout contract", EditorWindowChromeHitTestContract),
                ("Editor title-bar native ownership contract", EditorTitleBarCaptionVisualExclusion),
                ("Editor title-bar menu integration", EditorTitleBarMenuIntegration),
                ("Editor title-bar vertical alignment", EditorTitleBarVerticalAlignment),
                ("Editor title-bar menu theme contract", EditorTitleBarMenuThemeContract),
                ("Editor theme settings contract", EditorThemeSettingsContract),
                ("Stage21 General Settings app-wide isolation", EditorGeneralSettingsContract),
                ("Stage22 Desktop Settings master-detail and lifecycle policy", EditorDesktopSettingsContract),
            ]),
            ("Editor / Devices", [
                ("Stage23 Device Settings discovery and explicit adoption", DeviceSettingsStage23Tests.DiscoveryAndAdoption),
                ("Stage23 Device Settings consent and isolated persistence", DeviceSettingsStage23Tests.ConsentAndPersistence),
                ("Stage23 Device Settings disconnected and ambiguous UI", DeviceSettingsStage23Tests.LiveStatusControls),
                ("Stage23 Settings apply rollback and stale-file safety", DeviceSettingsStage23Tests.SettingsApplyRecovery),
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
            ("Widgets / DateTime", [
                ("Issue66 DateTime component projection, editor and cache", DateTimeComponentTests.ProjectionEditorAndCache),
                ("Issue66 schema 19 migration and quantitative compatibility", DateTimeComponentTests.SchemaMigrationAndCompatibility),
                ("Issue96 DateTime fractional projection and boundaries", Issue96DateTimePrecisionTests.ProjectionAndBoundaries),
                ("Issue96 DateTime Editor, cache and Binary equality", Issue96DateTimePrecisionTests.EditorCacheAndBinary),
                ("Issue96 DateTime format schema compatibility", Issue96DateTimePrecisionTests.SchemaCompatibility),
                ("Issue96 live frame clock and telemetry availability", Issue96DateTimePrecisionTests.LiveFrameClockAndAvailability),
                ("Issue81 Binary DateTime evaluation and Editor component", Issue81BinaryDateTimeTests.EvaluationAndEditor),
                ("Issue81 Binary DateTime schema 20 migration", Issue81BinaryDateTimeTests.SchemaMigration),
            ]),
            ("Telemetry / Connectivity", [
                ("Windows internet connectivity metric", WindowsInternetConnectivityMetric),
            ]),
            ("Widgets / Binary", [
                ("Binary numeric truth semantics", BinaryNumericTruthSemantics),
                ("Binary setpoint semantics", BinarySetpointSemantics),
                ("Issue89 numeric lists, intervals and all operators", Issue89BinarySetpointTests.NumericPatterns),
                ("Issue89 invalid numeric expressions and unavailable values", Issue89BinarySetpointTests.InvalidPatterns),
                ("Issue89 Editor field and schema round-trip", Issue89BinarySetpointTests.EditorAndSerialization),
                ("Issue89 numeric units, DateTime and Auto states", Issue89BinarySetpointTests.RenderContextAndLegacyTypes),
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
            ("Widgets / Shared Properties", [
                ("Widget source property hierarchy", WidgetSourcePropertyHierarchy),
                ("Universal widget property hierarchy", UniversalWidgetPropertyHierarchy),
                ("Appearance property hierarchy", AppearancePropertyHierarchy),
                ("Data property hierarchy", DataPropertyHierarchy),
                ("Gauge property hierarchy", GaugePropertyHierarchy),
                ("States property hierarchy", StatesPropertyHierarchy),
                ("Image property hierarchy", ImagePropertyHierarchy),
            ]),
            ("Widgets / Binary State & Schema", [
                ("Binary state image schema", BinaryStateImageSchema),
                ("Binary state animation playback", BinaryStateAnimationPlayback),
                ("Binary state text schema", BinaryStateTextSchema),
                ("Binary state text independence", BinaryStateTextIndependence),
                ("Binary state text rendering", BinaryStateTextRendering),
                ("Binary schema 10 migration", BinarySchema10Migration),
            ]),
            ("Widgets / Power", [
                ("Power state property hierarchy", PowerStatePropertyHierarchy),
                ("Power state presentation schema", PowerStatePresentationSchema),
                ("Power state text rendering", PowerStateTextRendering),
                ("Power schema 13 migration", PowerSchema13Migration),
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
            ("Widgets / Shared Schema", [
                ("Removed legacy widget types", RemovedLegacyWidgetTypes),
                ("Text property hierarchy", TextPropertyHierarchy),
            ]),
            ("Editor / Properties", [
                ("Issue67 bundled font selection for all text presentations", FontFamilySelectionTests.BundledFontOptions),
                ("Issue68 mixed multi-selection dropdown labels", MultiSelectionDropdownTests.MixedValueOptionLabels),
                ("Issue80 single-widget and canvas property history checkpoint", Issue80PropertyUndoTests.SingleWidgetAndCanvasCapture),
                ("Issue80 multi-widget property history checkpoint", Issue80PropertyUndoTests.MultiWidgetCapture),
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
            ("Dashboard / Model & Canvas", [
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
            ("Editor / Properties & Layout", [
                ("All widget properties assigned to explicit tabs", AllWidgetPropertiesAssignedToExplicitTabs),
                ("Property expansion state persistence", PropertyExpansionStatePersistence),
                ("Editor input focus ownership", EditorInputFocusOwnership),
                ("Layer tree pointer focus and inactive selection contract", LayerTreePointerFocusAndInactiveSelectionContract),
                ("Editor geometry whole-pixel normalization", EditorGeometryWholePixelNormalization),
                ("Layer tree viewport anchor persistence", LayerTreeViewportAnchorPersistence),
                ("Grid split-button behavior", GridSplitButtonBehavior),
            ]),
            ("Dashboard / Geometry", [
                ("Dashboard universal size schema", DashboardUniversalSizeSchema),
            ]),
            ("Widgets / Quantitative", [
                ("Quantitative foreground color schema", QuantitativeForegroundColorSchema),
                ("Bar foreground color rendering", BarForegroundColorRendering),
            ]),
            ("Widgets / Graph", [
                ("Graph canonical type and obsolete global fields", GraphCanonicalTypeAndObsoleteFields),
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
            ("Widgets / Graph", [
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
            ]),
            ("Telemetry / Graph Publication", [
                ("Graph telemetry publication and serialized discovery", GraphTelemetryTests.PublicationAndSerializedDiscovery),
                ("Graph telemetry pause and generation fences", GraphTelemetryTests.PausedCaptureAndSemanticGenerationFences),
                ("Graph catalog meaning and direct Output publication", GraphTelemetryTests.CatalogMeaningAndDynamicPublication),
            ]),
            ("Editor / Graph Preview", [
                ("Graph preview coalescing and terminal source ownership", GraphPreviewLifecycleTests.DiscoveryCoalescingAndTerminalShutdown),
                ("Graph preview close fence and Runtime isolation", GraphPreviewLifecycleTests.BlockedCaptureCloseFenceAndIsolation),
                ("Graph preview stale discovery and illustrative Output history", GraphPreviewLifecycleTests.StaleDiscoveryAndIllustrativeOutputHistory),
            ]),
            ("Widgets / Graph Editor", [
                ("Graph Editor common properties and applicability", GraphEditorTests.CommonPropertiesAndGraphApplicability),
                ("Graph Editor clipboard image and undo snapshots", GraphEditorTests.ClipboardImageImportAndCanonicalUndoSnapshots),
                ("Graph Editor shared background image bindings", GraphEditorTests.BackgroundImageSharedPropertyBindings),
            ]),
            ("Widgets / Bar, Gauge & Rendering", [
                ("Bar image property visibility", BarImagePropertyVisibility),
                ("Bar image progress modes", BarImageProgressModes),
                ("Bar image fit modes", BarImageFitModes),
                ("Gauge universal container effects", GaugeUsesUniversalContainerEffects),
                ("Gauge needle offsets and pointer", GaugeNeedleOffsetsAndPointer),
                ("Gauge needle above track", GaugeNeedleRendersAboveTrack),
                ("Static state-file animation classification", StaticStateFileAnimationClassification),
                ("Stateful widget shadow-cache animation contract", StatefulWidgetShadowCacheAnimationContract),
                ("PASS 9F stateful renderer unification contract", Pass9FStatefulRendererUnificationContract),
            ]),
            ("Rendering / Alpha", [
                ("Alpha BGRA transparency and black JPEG composite", SharedAlphaStage08Tests.TransparentFrameAndJpeg),
                ("Alpha BGRA layers, orientation and Editor parity", SharedAlphaStage08Tests.LayersAndPreviewParity),
                ("Alpha BGRA frame ownership and disposal", SharedAlphaStage08Tests.OwnershipAndDisposal),
            ]),
            ("Outputs / Desktop", [
                ("Desktop session lifecycle and no USB", DesktopSessionStage09Tests.DesktopLifecycle),
                ("Issue72 Desktop initial telemetry frame", DesktopSessionStage09Tests.RefreshOnFirstTelemetry),
                ("Desktop session reconfigure and orientation", DesktopSessionStage09Tests.ReconfigureGeometry),
                ("Desktop Board Enabled versus Provider Enabled", DesktopSessionStage09Tests.IndependentSwitches),
                ("Stage27 Desktop hidden/suspended/disabled suppresses frames", DesktopSessionStage09Tests.HiddenAndDisabledRenderSuppression),
                ("Desktop layered BGRA stride, alpha and blending", DesktopTransparencyStage10Tests.PremultipliedTransfer),
                ("Desktop transparent/translucent/opaque session frames", DesktopTransparencyStage10Tests.SessionCanvasAlpha),
                ("Desktop placement multi-monitor and recovery", DesktopPositionStage11Tests.MonitorRecovery),
                ("Issue97 Desktop cross-monitor visibility and detached recovery", DesktopPositionStage11Tests.MultiMonitorCoverage),
                ("Desktop per-target placement JSON persistence", DesktopPositionStage11Tests.ScopedPersistence),
                ("Issue69 Desktop panel taskbar exclusion", DesktopLockStage12Tests.TaskbarExclusion),
                ("Desktop locked layered style and unlocked hit-testing", DesktopLockStage12Tests.NativeStylePolicy),
                ("Desktop Lock changes on live output reconfiguration", DesktopLockStage12Tests.LiveLockToggle),
            ]),
            ("Rendering / Cache & Geometry", [
                ("Runtime JPEG zero-copy transport contract", RuntimeJpegZeroCopyTransportContract),
                ("Stable widget shadow cache", StableWidgetShadowIsReused),
                ("Numeric widget shadow cache invalidation", NumericWidgetShadowInvalidatesOnValueChange),
                ("Cached widget shadow follows translation", CachedWidgetShadowFollowsTranslation),
                ("Resize local-axis projection", ResizeLocalAxisProjection),
                ("Gauge resize geometry clamp", GaugeResizeGeometryClamp),
                ("Preview resize geometry invalidation", PreviewResizeGeometryInvalidation)
            ]),
        };
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
            Assert(target.Id == "desktop-1" &&
                   target.Kind == OutputTargetContract.DesktopKind &&
                   target.Transport == OutputTargetContract.DesktopTransport &&
                   target.DeviceId.Length == 0,
                "Configuration without Outputs must safely default to a disabled-hardware Desktop model.");

            config.Outputs.Targets =
            [
                new OutputTargetConfig { Transport = OutputTargetContract.LyBulkTransport, Width = 1920, Height = 480, Id = "physical-1", Name = "Legacy Trofeo",
                    DeviceId = "usb-interface:" + new string('a', 64) }
            ];
            config.Save(path);
            var reloaded = AppConfig.Load(path, log);
            var persistedTarget = OutputTargetContract.GetPrimary(reloaded);
            Assert(persistedTarget.DeviceId == config.Outputs.Targets[0].DeviceId &&
                   !persistedTarget.DeviceEnabled,
                "Explicit output-device binding must persist while physical hardware remains disabled by default.");
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

    private static void TrofeoOverlappedCancelAwaitsCompletion()
    {
        using var eventNotYetSignaled = new ManualResetEvent(initialState: false);
        using var cts = new CancellationTokenSource();
        using var cancellationRequested = new ManualResetEventSlim();
        using var allowTerminalCompletion = new ManualResetEventSlim();
        cts.Cancel();

        var worker = Task.Run(() => TrofeoOverlappedCompletionWait.Wait(
            eventNotYetSignaled,
            cts.Token,
            () => cancellationRequested.Set(),
            () =>
            {
                if (!allowTerminalCompletion.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("Synthetic terminal completion was not signaled.");
            }));

        try
        {
            Assert(cancellationRequested.Wait(TimeSpan.FromSeconds(2)),
                "A pending native cancellation request must be issued.");
            Assert(!worker.Wait(TimeSpan.FromMilliseconds(100)),
                "Cancellation request alone must never mark pending native I/O as completed.");

            allowTerminalCompletion.Set();
            Assert(worker.Wait(TimeSpan.FromSeconds(2)) && worker.Result,
                "Cancellation must return only after the terminal native result was drained.");
        }
        finally
        {
            allowTerminalCompletion.Set();
            try { worker.GetAwaiter().GetResult(); } catch { }
        }
    }

    private static void TrofeoOverlappedX64AbiContract()
    {
        Assert(IntPtr.Size == 8,
            "Trofeo native OVERLAPPED contract must execute in the declared win-x64 process.");

        var flags = System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static;
        var native = typeof(TrofeoTransport).GetNestedType(
            "Win32Overlapped", System.Reflection.BindingFlags.NonPublic);
        Assert(native is not null &&
               System.Runtime.InteropServices.Marshal.SizeOf(native) == 32 &&
               System.Runtime.InteropServices.Marshal.OffsetOf(native, "EventHandle").ToInt32() == 24,
            "Native Windows OVERLAPPED must match the 32-byte x64 ABI with hEvent at offset 24.");

        foreach (var name in new[] { "WinUsb_ReadPipe", "WinUsb_WritePipe" })
        {
            var nativeMethod = typeof(TrofeoTransport).GetMethod(name, flags);
            var parameters = nativeMethod?.GetParameters();
            Assert(parameters is not null &&
                   parameters.Length == 6 &&
                   parameters[2].ParameterType == typeof(IntPtr) &&
                   parameters[5].ParameterType == typeof(IntPtr),
                $"{name} must use a stable pinned byte buffer and native OVERLAPPED pointer.");
        }

        Assert(typeof(TrofeoTransport).GetMethod("WinUsb_GetOverlappedResult", flags) is not null &&
               typeof(TrofeoTransport).GetMethod("CancelIoEx", flags) is not null,
            "Cancellable native WinUSB transfers require terminal-result and targeted-cancel APIs.");
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

    private static void FramePumpSuspendCancelsBlockedOpen()
    {
        var root = CreateTemporaryApplicationRoot("PinkieSysMon-pump-cancel-open");
        using var entered = new ManualResetEventSlim();
        using var canceled = new ManualResetEventSlim();
        var pump = new FramePump(
            new AppConfig(),
            new OutputTargetConfig(),
            new C.DashboardDefinition { BaseDirectory = Path.Combine(root, "dashboards") },
            new MetricStore(),
            new FileLogger(Path.Combine(root, "cancel.log")),
            (target, timeout, log, ioToken) =>
            {
                entered.Set();
                try
                {
                    if (!ioToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
                        throw new TimeoutException("Synthetic handshake was not canceled.");
                    ioToken.ThrowIfCancellationRequested();
                    throw new InvalidOperationException("Synthetic handshake ended without cancellation.");
                }
                catch (OperationCanceledException)
                {
                    canceled.Set();
                    throw;
                }
            });

        try
        {
            pump.Start();
            Assert(entered.Wait(TimeSpan.FromSeconds(5)),
                "Worker must enter the synthetic pending handshake.");
            pump.Suspend();
            Assert(canceled.Wait(TimeSpan.FromSeconds(2)),
                "Suspending an output must cancel a pending worker-owned handshake.");
            Assert(pump.UsbState == "SUSPENDED" && !pump.IsConnected,
                "Canceled handshake must leave the output suspended and disconnected.");
        }
        finally
        {
            pump.Dispose();
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void FramePumpDisposeCancelsBlockedSend()
    {
        var root = CreateTemporaryApplicationRoot("PinkieSysMon-pump-cancel-send");
        using var sendEntered = new ManualResetEventSlim();
        var disposalCount = 0;
        var pump = new FramePump(
            new AppConfig { Usb = new UsbConfig { TransferTimeoutMs = 500 } },
            new OutputTargetConfig(),
            new C.DashboardDefinition { BaseDirectory = Path.Combine(root, "dashboards") },
            new MetricStore(),
            new FileLogger(Path.Combine(root, "cancel.log")),
            (target, timeout, log, ioToken) =>
                new CancellationAwareTrofeoTestTransport(
                    ioToken, sendEntered, () => Interlocked.Increment(ref disposalCount)));

        try
        {
            pump.Start();
            Assert(sendEntered.Wait(TimeSpan.FromSeconds(5)),
                "Worker must reach the synthetic pending frame transfer.");

            var disposeTask = Task.Run(pump.Dispose);
            Assert(disposeTask.Wait(TimeSpan.FromSeconds(3)),
                "Dispose must cancel a stalled frame transfer after its completion grace and drain the worker.");
            Assert(disposalCount == 1,
                "Worker-owned transport must be disposed exactly once after cancellation.");
        }
        finally
        {
            pump.Dispose();
            try { Directory.Delete(root, recursive: true); } catch { }
        }
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
            (target, timeout, log, ioToken) =>
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
            (target, timeout, log, ioToken) => fake);

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
        var target = new OutputTargetConfig { Transport = OutputTargetContract.LyBulkTransport };
        var pump = new FramePump(
            config, target, new C.DashboardDefinition { BaseDirectory = Path.Combine(root, "dashboards") }, new MetricStore(),
            new FileLogger(Path.Combine(root, "ownership.log")),
            (selectedTarget, timeout, log, ioToken) =>
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

    private static void FramePumpPerDeviceUsbSettings()
    {
        var root = CreateTemporaryApplicationRoot("PinkieSysMon-pump-target-usb");
        using var sendStarted = new ManualResetEventSlim();
        using var allowSend = new ManualResetEventSlim();
        var firstTransport = new BlockingTrofeoTestTransport(sendStarted, allowSend);
        var secondTransport = new BlockingTrofeoTestTransport();
        var attempts = new System.Collections.Concurrent.ConcurrentQueue<int>();
        var opens = 0;

        var config = new AppConfig();
        var target = new OutputTargetConfig
        { Transport = OutputTargetContract.LyBulkTransport, Width = 1920, Height = 480, DeviceId = "usb-interface:9db7b64ace1096dfd3006978c56ad9c802a669f230c9f3d8e240ad1deb08227a",
            Usb = new UsbConfig
            {
                RetryIntervalMs = 700,
                TransferTimeoutMs = 1100
            }
        };
        var board = new C.DashboardDefinition { BaseDirectory = Path.Combine(root, "dashboards") };
        var pump = new FramePump(config, target, board, new MetricStore(),
            new FileLogger(Path.Combine(root, "target-usb.log")),
            (selected, timeout, log, token) =>
            {
                attempts.Enqueue(timeout);
                return Interlocked.Increment(ref opens) == 1 ? firstTransport : secondTransport;
            });

        try
        {
            pump.Start();
            Assert(sendStarted.Wait(TimeSpan.FromSeconds(5)),
                "Initial physical session must reach its injected transport without USB hardware.");
            Assert(attempts.TryPeek(out var initial) &&
                   initial == 1100,
                "Transport handshake must use the physical target's USB settings, not global defaults.");

            var newConfig = new AppConfig();
            var changedTarget = new OutputTargetConfig
            { Transport = OutputTargetContract.LyBulkTransport, Width = 1920, Height = 480, DeviceId = "usb-interface:6edd1ff49a5e145027e2897c97cd4c97bb1a85b62b065462ad82d300f93becdc",
                Usb = new UsbConfig
                {
                    RetryIntervalMs = 1500,
                    TransferTimeoutMs = 1800
                }
            };
            using var plan = pump.PrepareReconfiguration(newConfig, changedTarget, board);
            pump.CommitReconfiguration(plan);
            Assert(firstTransport.DisposeCount == 0,
                "Reconfigure may not release a physical transport during its in-flight send.");

            allowSend.Set();
            Assert(SpinWait.SpinUntil(() => firstTransport.DisposeCount == 1 &&
                   Volatile.Read(ref opens) >= 2, TimeSpan.FromSeconds(5)),
                "Changing per-target USB timeout must retire and reopen only this output.");
            var values = attempts.ToArray();
            Assert(values.Length >= 2 &&
                   values[1] == 1800,
                "Reconnected physical output must use updated per-target USB parameters.");
            Assert(config.Usb.TransferTimeoutMs == 3000 &&
                   newConfig.Usb.TransferTimeoutMs == 3000,
                "Per-device reconfiguration must not mutate global USB defaults.");
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
            Assert(created.All(session => session.PublishesRuntimeMetrics),
                "Every active output session must publish independently when its provider is enabled.");

            var statuses = manager.GetStatuses();
            Assert(statuses.Count == 2 && statuses.All(status => status.IsActive),
                "Every configured target must report an independent active session after startup.");

            manager.Reconfigure(OutputLifecycleTestConfiguration.WithEnabled(config, "screen-a", false),
                manager.GetDashboardAssignments());
            Assert(created[0].DisposeCount == 1 && created[1].DisposeCount == 0,
                "Disabling one output target must dispose only that target session.");
            Assert(created[1].PublishesRuntimeMetrics,
                "Disabling one output must leave the sibling's metric publisher enabled.");

            manager.Reconfigure(config, manager.GetDashboardAssignments());
            Assert(created.Count == 3 && created[2].StartCount == 1,
                "Re-enabling a target must create a fresh session without restarting unrelated targets.");
            Assert(created[2].PublishesRuntimeMetrics && created[1].PublishesRuntimeMetrics,
                "Restarting an output must restore its own publisher without suppressing the sibling.");

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
                        { Transport = OutputTargetContract.LyBulkTransport, Width = 1920, Height = 480,
                            Id = "screen-b",
                            Name = "Right Trofeo",
                            DeviceId = "usb-interface:" + new string('b', 64),
                            DeviceEnabled = true
                        },
                        new OutputTargetConfig
                        { Transport = OutputTargetContract.LyBulkTransport, Width = 1920, Height = 480,
                            Id = "screen-c",
                            Name = "Aux Trofeo",
                            DeviceId = "usb-interface:" + new string('c', 64),
                            DeviceEnabled = true
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

            var disabledB = OutputLifecycleTestConfiguration.WithEnabled(replacement, "screen-b", false);
            manager.Reconfigure(disabledB, manager.GetDashboardAssignments());
            manager.Reconfigure(disabledB, manager.GetDashboardAssignments());
            statuses = manager.GetStatuses();
            Assert(statuses.Single(status => status.TargetId == "screen-b") is { IsActive: false, OutputEnabled: false, UsbState: "DISABLED" },
                "A disabled target must remain inactive across configuration reloads using its saved enablement.");
            manager.Reconfigure(replacement, manager.GetDashboardAssignments());
            Assert(manager.GetStatuses().All(status => status.IsActive),
                "Re-enabling a configured target must restore its session without a separate activation command.");
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

    private static void OutputSessionGlobalRestartRollback()
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
            var original = created.ToArray();
            manager.StopAll();
            failRestartA = true;

            AssertThrows<InvalidOperationException>(
                manager.Start,
                "Global restart must fail cleanly when a newly required session cannot start.");
            var failedStatuses = manager.GetStatuses();
            Assert(failedStatuses.All(status => !status.IsActive && status.UsbState == "STOPPED"),
                "Failed global restart must preserve the real global lifetime stop.");
            Assert(original.All(session => session.DisposeCount == 1) &&
                   created.Skip(2).Count() == 2 && created.Skip(2).All(session => session.DisposeCount == 1),
                "Global stop and failed restart must dispose every original and candidate session exactly once.");

            failRestartA = false;
            manager.Start();
            Assert(manager.GetStatuses().All(status => status.IsActive),
                "Global restart must remain retryable after a startup rollback.");
            Assert(created.Skip(4).Count() == 2 && created.Skip(4).All(session => session.StartCount == 1),
                "Retry after global restart rollback must create and start a fresh session set.");
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
                        { Transport = OutputTargetContract.LyBulkTransport, Width = 1920, Height = 480,
                            Id = "screen-b",
                            Name = "Right Trofeo",
                            DeviceId = "usb-interface:" + new string('b', 64),
                            DeviceEnabled = true
                        },
                        new OutputTargetConfig
                        { Transport = OutputTargetContract.LyBulkTransport, Width = 1920, Height = 480,
                            Id = "screen-c",
                            Name = "Aux Trofeo",
                            DeviceId = "usb-interface:" + new string('c', 64),
                            DeviceEnabled = true
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

        var retiredStop = RuntimeIpcProtocol.ParseCommand(
            RuntimeIpcProtocol.BuildTargetCommand("OUTPUT_STOP", targetId));
        Assert(retiredStop.Kind == RuntimeIpcCommandKind.Invalid,
            "Retired target-stop IPC must be rejected instead of recreating manual activation state.");

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

        var malformed = RuntimeIpcProtocol.ParseCommand(RuntimeIpcProtocol.StartOrReloadTargetCommand + "\tnot-json");
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
            var control = new RuntimeOutputControl(manager, () => "PinkiePie_Vertical");

            var retiredStop = RuntimeIpcProtocol.ParseCommand(
                RuntimeIpcProtocol.BuildTargetCommand("OUTPUT_STOP", "screen-a"));
            Assert(!control.Execute(retiredStop).Success && !control.ShouldExitRuntimeAfter(retiredStop) &&
                   created.Count == 2 && created.All(session => session.DisposeCount == 0),
                "A retired target-stop command must fail without changing sessions or requesting shutdown.");
            var disabledA = OutputLifecycleTestConfiguration.WithEnabled(config, "screen-a", false);
            manager.Reconfigure(disabledA, manager.GetDashboardAssignments());
            var disableACommand = new RuntimeIpcCommand(RuntimeIpcCommandKind.SetPhysicalDeviceEnabled, "screen-a", Value: false);
            Assert(
                   !control.GetStatus().Outputs.Single(output => output.TargetId == "screen-a").IsActive &&
                   !control.ShouldExitRuntimeAfter(disableACommand),
                "Disabling an Output must unload its session and keep Runtime available.");
            Assert(control.GetStatus().Outputs.Single(output => output.TargetId == "screen-b").IsActive,
                "Disabling one target must leave sibling output sessions active.");

            manager.Reconfigure(OutputLifecycleTestConfiguration.WithEnabled(disabledA, "screen-b", false),
                manager.GetDashboardAssignments());
            var disableBCommand = new RuntimeIpcCommand(RuntimeIpcCommandKind.SetPhysicalDeviceEnabled, "screen-b", Value: false);
            Assert(
                   control.GetStatus().Outputs.All(output => !output.IsActive) &&
                   !control.ShouldExitRuntimeAfter(disableBCommand),
                "Disabling the final Output must leave Runtime available for configuration.");
            AssertThrows<InvalidOperationException>(() => manager.ReloadTarget(config.Outputs.Targets[0],
                manager.GetDashboardAssignments()["screen-a"], _ => { }),
                "Target reload must not bypass disabled configuration.");

            manager.Reconfigure(config, manager.GetDashboardAssignments());
            Assert(control.GetStatus().Outputs.All(output => output.IsActive),
                "Re-enabling Outputs must restore every eligible session without manual activation state.");

            var stopAllCommand = new RuntimeIpcCommand(RuntimeIpcCommandKind.StopAll);
            var stopAll = control.Execute(stopAllCommand);
            Assert(stopAll.Success &&
                   control.GetStatus().Outputs.All(output => !output.IsActive) &&
                   control.ShouldExitRuntimeAfter(stopAllCommand),
                "Stop All must unload every configured output session and request runtime-process shutdown.");
            AssertThrows<InvalidOperationException>(() => manager.ReloadTarget(config.Outputs.Targets[0],
                manager.GetDashboardAssignments()["screen-a"], _ => { }),
                "A target command must not override the global lifetime stop during shutdown.");
            Assert(!control.Execute(new RuntimeIpcCommand(RuntimeIpcCommandKind.StartOrReloadAll)).Success &&
                   control.GetStatus().Outputs.All(output => !output.IsActive),
                "All without the common-configuration controller must fail without bypassing the global lifetime stop.");
            manager.Start();
            Assert(control.GetStatus().Outputs.All(output => output.IsActive),
                "The retained global lifecycle primitive must remain retryable after Stop All.");

            Assert(RuntimeIpcProtocol.ResponseIndicatesRuntimeStopping(
                       new RuntimeIpcResponse(true, $"Outputs stopped. {RuntimeIpcProtocol.RuntimeStoppingMessage}")) &&
                   !RuntimeIpcProtocol.ResponseIndicatesRuntimeStopping(
                       new RuntimeIpcResponse(true, "Output disabled; Runtime remains available.")),
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

    private static void RuntimeOutputStartupPolicy()
    {
        RuntimeStartupOptions.Validate([]);
        foreach (var argument in new[] { "--outputs-stopped", "--OUTPUTS-STOPPED", "--unrelated" })
            AssertThrows<ArgumentException>(() => RuntimeStartupOptions.Validate([argument]),
                "Unsupported startup arguments must fail before Runtime can create sessions or open hardware.");
    }

    private static void ScreenMenuDeviceCommandModel()
    {
        var targets = CreateMultiOutputConfig().Outputs.Targets;

        var runtimeAbsent = ScreenMenuContract.Build(targets, false, null, commandInProgress: false);
        Assert(runtimeAbsent.Targets.Length == 2 && runtimeAbsent.Targets[0].TargetName == "Left Trofeo" && runtimeAbsent.Targets[1].TargetName == "Right Trofeo",
            "Output first-level items must be based on configured user-facing output names.");
        Assert(runtimeAbsent.Targets.All(target => target.CanStartOrReload) &&
               runtimeAbsent.CanStartOrReloadAll && !runtimeAbsent.CanStopAll,
            "When Runtime is absent, reload workflow must remain available while global shutdown remains disabled.");
        Assert(runtimeAbsent.Targets.All(target => target.StatusText == "Status: Runtime not running"),
            "Output target status must explicitly represent an absent Runtime.");

        var snapshot = new RuntimeStatusSnapshot(
            "PinkiePie_Vertical",
            [
                new RuntimeOutputStatus("screen-a", "Left Trofeo", true, false, "WAITING"),
                new RuntimeOutputStatus("screen-b", "Right Trofeo", false, false, "DISABLED") { OutputEnabled = false }
            ]);
        var running = ScreenMenuContract.Build(targets, true, snapshot, commandInProgress: false);
        Assert(running.Targets[0].CanStartOrReload &&
               running.Targets[0].StatusText == "Status: Waiting for device",
            "An active target with no physical connection must remain addressable and visibly report the missing-device state.");
        Assert(!running.Targets[1].CanStartOrReload &&
               running.Targets[1].StatusText == "Status: Disabled",
            "A disabled target must deny reload and report its configured state.");
        Assert(running.CanStartOrReloadAll && running.CanStopAll,
            "Global Output commands must reflect the aggregate configured-session state.");
        var allActiveSnapshot = new RuntimeStatusSnapshot(
            "PinkiePie_Vertical",
            [
                new RuntimeOutputStatus("screen-a", "Left Trofeo", true, true, "CONNECTED"),
                new RuntimeOutputStatus("screen-b", "Right Trofeo", true, true, "CONNECTED")
            ]);
        Assert(ScreenMenuContract.Build(targets, true, allActiveSnapshot, false).CanStopAll,
            "Global shutdown must remain available when actual Output sessions are active.");

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
        Assert(busy.Targets.All(target => !target.CanStartOrReload) &&
               !busy.CanStartOrReloadAll && !busy.CanStopAll,
            "A pending Output command must disable every competing per-target and global Output action.");

        Assert(ScreenMenuContract.StartReloadText == "Reload" &&
               ScreenMenuContract.StartReloadAllText == "Reload All" &&
               ScreenMenuContract.StopAllText == "Stop / Unload All Outputs",
            "Editor labels must expose Reload and Reload All while retaining the existing global shutdown scope.");
    }


    private static void EditorMainMenuGroupCleanup()
    {
        using var menu = new MenuStrip();
        var fileMenu = new ToolStripMenuItem(EditorMainMenuContract.FileMenuText);
        var editMenu = new ToolStripMenuItem(EditorMainMenuContract.EditMenuText);
        var desktopMenu = new ToolStripMenuItem(EditorMainMenuContract.DesktopMenuText);
        var devicesMenu = new ToolStripMenuItem(EditorMainMenuContract.DevicesMenuText);
        var toolsMenu = new ToolStripMenuItem(EditorMainMenuContract.ToolsMenuText);
        var helpMenu = new ToolStripMenuItem(EditorMainMenuContract.HelpMenuText);

        EditorMainMenuContract.Populate(menu, fileMenu, editMenu, desktopMenu, devicesMenu, toolsMenu, helpMenu);
        var labels = menu.Items.Cast<ToolStripItem>().Select(item => item.Text).ToArray();
        Assert(labels.SequenceEqual(new[] { "File", "Edit", "Desktop", "Devices", "Tools", "Help" }),
            "The main menu must expose direct Desktop/Devices commands before Tools, without Settings or Outputs.");
        Assert(desktopMenu.Alignment == ToolStripItemAlignment.Left &&
               devicesMenu.Alignment == ToolStripItemAlignment.Left &&
               toolsMenu.Alignment == ToolStripItemAlignment.Left &&
               helpMenu.Alignment == ToolStripItemAlignment.Right,
            "Help must occupy the right-aligned title-bar position; all other menus remain left.");
        editMenu.DropDownItems.Add(new ToolStripMenuItem("Send to Back"));
        var editorInvoked = false;
        EditorMainMenuContract.AppendEditorSettings(editMenu, (_, _) => editorInvoked = true);
        Assert(editMenu.DropDownItems[^2] is ToolStripSeparator &&
               editMenu.DropDownItems[^1] is ToolStripMenuItem { Text: EditorMainMenuContract.EditorSettingsText },
            "Editor settings must be the last Edit command, immediately after a separator.");
        ((ToolStripMenuItem)editMenu.DropDownItems[^1]).PerformClick();
        Assert(editorInvoked, "The relocated Editor settings command must invoke its handler.");
        Assert(!labels.Contains("Settings", StringComparer.OrdinalIgnoreCase) &&
               !labels.Contains("Output", StringComparer.OrdinalIgnoreCase) &&
               !labels.Contains("Group", StringComparer.OrdinalIgnoreCase),
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
        var outputButtonField = typeof(EditorForm).GetField("_screenMenu",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert(outputButtonField?.FieldType == typeof(ToolStripDropDownButton),
            "Outputs must be a toolbar dropdown button, not a title-bar menu item.");
        Assert(fields.Contains("_groupSelectedMenuItem") &&
               fields.Contains("_ungroupSelectedMenuItem") &&
               fields.Contains("_treeGroupSelectedMenuItem") &&
               fields.Contains("_treeUngroupSelectedMenuItem") &&
               fields.Contains("_toolbarGroupButton") &&
               fields.Contains("_toolbarUngroupButton"),
            "Removing the duplicate Group menu must preserve grouping commands in Edit, Layers context menu, and toolbar surfaces.");
    }


    private static void EditorOutputsToolbarRightInset()
    {
        using var form = new Form { ClientSize = new Size(600, 100) };
        using var toolbar = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
        var outputs = new ToolStripDropDownButton("Outputs")
        {
            Alignment = ToolStripItemAlignment.Right
        };
        var originalMargin = outputs.Margin;
        EditorForm.ApplyOutputsToolbarRightInset(outputs);
        toolbar.Items.Add(outputs);
        form.Controls.Add(toolbar);
        form.PerformLayout();
        toolbar.PerformLayout();

        Assert(outputs.Margin.Right == 12 &&
               outputs.Margin.Left == originalMargin.Left &&
               outputs.Margin.Top == originalMargin.Top &&
               outputs.Margin.Bottom == originalMargin.Bottom,
            "Outputs toolbar inset must add 12 px on the right without changing vertical or left spacing.");
        Assert(outputs.Alignment == ToolStripItemAlignment.Right &&
               toolbar.ClientRectangle.Right - outputs.Bounds.Right >= 12,
            "Outputs must remain right aligned while leaving a visible gap from the toolbar edge.");
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
        var desktopMenu = new ToolStripMenuItem(EditorMainMenuContract.DesktopMenuText);
        var devicesMenu = new ToolStripMenuItem(EditorMainMenuContract.DevicesMenuText);
        var toolsMenu = new ToolStripMenuItem(EditorMainMenuContract.ToolsMenuText);
        var helpMenu = new ToolStripMenuItem(EditorMainMenuContract.HelpMenuText);

        EditorMainMenuContract.Populate(menu, fileMenu, editMenu, desktopMenu, devicesMenu, toolsMenu, helpMenu);
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
        Assert(helpMenu.Alignment == ToolStripItemAlignment.Right,
            "The Help menu must occupy the former right-aligned Output title-bar position.");
        Assert(passthrough.Length == 6,
            "Every visible top-level menu item must have an explicit non-client passthrough region.");

        var leftGroupRight = new[] { fileMenu, editMenu, desktopMenu, devicesMenu, toolsMenu }
            .Max(item => menu.Left + item.Bounds.Right);
        var rightGroupLeft = menu.Left + helpMenu.Bounds.Left;
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


    private static void EditorGeneralSettingsContract()
    {
        var config = new AppConfig();
        config.Runtime.IdleTimeoutMinutes = 17;
        config.Runtime.LogRetentionDays = 7;
        using var dialog = new EditorGeneralSettingsDialog(
            startRuntimeWithWindows: true,
            systemProviderEnabled: true,
            libreHardwareMonitorProviderEnabled: false,
            icueProviderEnabled: true,
            toolbarIconSize: 48,
            themeMode: EditorThemeMode.Dark,
            config: config);
        Assert(!dialog.Controls.OfType<TabControl>().Any(),
            "Editor Settings must be a standalone non-tabbed dialog.");
        Assert(dialog.Padding == new Padding(12),
            "Editor Settings must keep its controls inset from the window edges.");
        Assert(dialog.StartRuntimeWithWindows && dialog.SystemProviderEnabled &&
               !dialog.LibreHardwareMonitorProviderEnabled && dialog.IcueProviderEnabled &&
               dialog.ToolbarIconSize == 48 && dialog.ThemeMode == EditorThemeMode.Dark,
            "General Settings must preserve startup/provider/theme/icon values passed by their existing owners.");
        var groups = dialog.Controls.OfType<Panel>().Single(panel => panel.Controls.OfType<GroupBox>().Any()).Controls.OfType<GroupBox>().ToArray();
        Assert(groups.Select(group => group.Text).ToHashSet()
                .SetEquals(["Startup", "Runtime activity", "Logging", "Metric providers", "Interface"]),
            "General tab must expose the Runtime idle setting but no legacy device binding or per-output settings.");
        var idleLayout = groups.Single(group => group.Text == "Runtime activity")
            .Controls.OfType<TableLayoutPanel>().Single();
        var idleTimeout = idleLayout.Controls.OfType<NumericUpDown>().Single();
        Assert(dialog.RuntimeIdleTimeoutMinutes == 17 && idleTimeout.Value == 17 &&
               idleTimeout.Minimum == 0 && idleTimeout.Maximum == RuntimeIdlePolicy.MaximumTimeoutMinutes,
            "Runtime idle setting must preserve its configured value and supported range.");
        var loggingLayout = groups.Single(group => group.Text == "Logging")
            .Controls.OfType<TableLayoutPanel>().Single();
        var retention = loggingLayout.Controls.OfType<NumericUpDown>().Single();
        Assert(dialog.LogRetentionDays == 7 && retention.Value == 7 &&
               retention.Minimum == LogRetentionPolicy.MinimumDays &&
               retention.Maximum == LogRetentionPolicy.MaximumDays &&
               loggingLayout.Controls.OfType<Label>().Any(label => label.Text == "Keep logs (days)"),
            "General Settings must expose persisted global log retention with explicit limits.");
        var generalNumericControls = groups.SelectMany(group => group.Controls.OfType<TableLayoutPanel>())
            .SelectMany(layout => layout.Controls.OfType<NumericUpDown>()).ToArray();
        Assert(generalNumericControls.Length == 2 &&
               generalNumericControls.Contains(idleTimeout) && generalNumericControls.Contains(retention) &&
               !dialog.Controls.OfType<NumericUpDown>().Any(),
            "General must contain only app-wide idle and logging numeric controls, not Output device options.");
    }

    private static void EditorDesktopSettingsContract()
    {
        var config = new AppConfig();
        var original = config.Outputs.Targets.Single();
        original.Name = "Left panel";
        original.Desktop!.BoardEnabled = false;
        original.Desktop.ZOrder = 17; // Earlier user order may contain gaps.
        original.ProviderEnabled = true;
        original.Desktop.Lock = true;
        original.RefreshIntervalMs = 500;
        var physical = new OutputTargetConfig
        { Width = 1920, Height = 480, DeviceId = "usb-interface:857aa06d11e4955e39fabdc4ac52f49f61d034e95b9a5b34ebdc113c9f678f7d",
            Id = "stage22-physical",
            Name = "Disabled hardware",
            Kind = OutputTargetContract.PhysicalKind,
            Transport = OutputTargetContract.LyBulkTransport,
            DeviceEnabled = false,
            Dashboard = "Default"
        };
        config.Outputs.Targets.Add(physical);
        var model = new DesktopSettingsModel(config);
        Assert(model.Desktops.Count == 1 && !model.IsDirty &&
               !model.Desktops[0].Desktop!.BoardEnabled && model.Desktops[0].ProviderEnabled &&
               model.Desktops[0].Desktop!.Lock && model.Desktops[0].RefreshIntervalMs == 500,
            "Desktop Settings must select a detached working copy preserving independent flags and refresh.");
        model.Desktops[0].Name = "Changed panel";
        model.MarkChanged();
        Assert(original.Name == "Left panel",
            "Editing a Desktop in Settings must not change the committed config before OK.");
        var added = model.Create("Default");
        Assert(model.Desktops[0].Desktop!.ZOrder == 0 && added.Desktop!.ZOrder == 1,
            "A newly created Desktop must appear at the front of Z-order after old gaps are normalized.");
        added.ProviderEnabled = false;
        added.Desktop!.Lock = false;
        added.Desktop.BoardEnabled = true;
        added.RefreshIntervalMs = 333;
        model.MarkChanged();
        Assert(DesktopInsertionPolicy.SlotForItem(1, false) == 1 &&
               DesktopInsertionPolicy.SlotForItem(1, true) == 2 &&
               DesktopInsertionPolicy.DestinationIndex(3, 0, 3) == 2 &&
               DesktopInsertionPolicy.DestinationIndex(3, 2, 0) == 0 &&
               DesktopInsertionPolicy.DestinationIndex(3, 1, 1) == 1 &&
               DesktopInsertionPolicy.DestinationIndex(3, 1, 2) == 1 &&
               DesktopInsertionPolicy.DestinationIndex(3, 2, 2) == 2 &&
               DesktopInsertionPolicy.DestinationIndex(3, -1, 0) == -1 &&
               DesktopInsertionPolicy.DestinationIndex(3, 1, 4) == -1,
            "Desktop insertion preview must map before/after boundaries to the correct final list position.");
        model.Move(1, 0);
        Assert(model.Desktops[0] == added && added.Desktop.ZOrder == 0 &&
               model.Desktops[1].Desktop!.ZOrder == 1,
            "Dragging Desktop entries must reorder the separate output Z-order deterministically.");
        model.ApplyTo(config);
        Assert(config.Outputs.Targets.Count == 3 &&
               ReferenceEquals(config.Outputs.Targets.Single(t => t.Kind == OutputTargetContract.PhysicalKind), physical) &&
               config.Outputs.Targets.Where(t => t.Kind == OutputTargetContract.DesktopKind).Select(t => t.Id)
                   .SequenceEqual(new[] { added.Id, original.Id }) &&
               config.Outputs.Targets[0].RefreshIntervalMs == 333 &&
               !config.Outputs.Targets[0].ProviderEnabled,
            "Applying a Desktop dialog must preserve the physical Output object and each sibling's independent settings.");
        var secondModel = new DesktopSettingsModel(config);
        var extra = secondModel.Desktops.Single(t => t.Id == added.Id);
        secondModel.Remove(extra);
        secondModel.ApplyTo(config);
        Assert(config.Outputs.Targets.Count == 2 &&
               ReferenceEquals(config.Outputs.Targets[1], physical) &&
               config.Outputs.Targets[0].Name == "Changed panel" &&
               config.Outputs.Targets[0].ProviderEnabled,
            "Desktop Remove must delete only the selected virtual Output; preserve remaining providers and physical targets.");

        var single = new DesktopSettingsModel(new AppConfig());
        Assert(!single.CanRemove(single.Desktops.Single()),
            "Removing the final Output must remain prohibited.");
        var rejected = false;
        try { single.Remove(single.Desktops.Single()); }
        catch (InvalidOperationException) { rejected = true; }
        Assert(rejected, "Direct Remove calls must reject deletion of the last Output.");

        using var dialog = new DesktopSettingsDialog(new AppConfig(), ["Default"]);
        Assert(!dialog.Controls.OfType<TabControl>().Any(),
            "Desktop Settings must be a standalone non-tabbed dialog.");
        Assert(dialog.Padding == new Padding(12),
            "Desktop Settings must keep its controls inset from the window edges.");
        var desktopPage = dialog.Controls.OfType<Panel>().Single(panel => panel.Controls.OfType<TableLayoutPanel>().Any());
        Assert(desktopPage.Controls.OfType<TableLayoutPanel>().Single().ColumnCount == 2,
            "Desktop Settings must use a single two-column master/detail layout.");
        Assert(!dialog.DesktopChanged,
            "Opening Desktop Settings must not modify persisted configuration.");
        Assert(dialog.AcceptButton is Button { DialogResult: DialogResult.OK } &&
               dialog.CancelButton is Button { DialogResult: DialogResult.Cancel },
            "Desktop Settings must own OK/Cancel instead of relying on the former shared Settings dialog.");
        Assert(dialog.Controls.OfType<Label>().Any(label =>
                   label.Text == DesktopSettingsDialog.ApplyTimingNotice &&
                   label.Text.Contains("after OK", StringComparison.Ordinal) &&
                   label.Text.Contains("next start", StringComparison.Ordinal)),
            "Desktop Settings must explain when saved Outputs take effect.");
        var desktopList = desktopPage.Controls.OfType<TableLayoutPanel>().Single()
            .Controls.OfType<TableLayoutPanel>().Single()
            .Controls.OfType<ListBox>().Single();
        Assert(desktopList.DrawMode == DrawMode.OwnerDrawFixed,
            "Desktop list must support painting the precise before/after insertion marker.");
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
            Metric = FixtureOutputFps,
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
            Metric = FixtureOutputFps,
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
            Metric = FixtureOutputFps,
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
            Metric = FixtureOutputFps,
            NeedleEnabled = false
        };
        var mixedEnabled = new WidgetDefinition
        {
            Type = WidgetTypeContract.Gauge,
            Id = "context-editor-mixed-enabled",
            Metric = FixtureOutputFps,
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
            Metric = FixtureOutputFps,
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
            Metric = FixtureOutputFps,
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
            Metric = FixtureOutputFps,
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
        Assert(root["schemaVersion"]!.GetValue<int>() == DashboardSchemaMigration.CurrentSchemaVersion,
            "Canvas migration must end at current schema 21.");

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
            "Schema 17 dashboard must migrate raw Canvas paths through canonical schema 21.");
        Assert(root["schemaVersion"]!.GetValue<int>() == DashboardSchemaMigration.CurrentSchemaVersion,
            "Schema 17 migration must end at current schema 21.");

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
            Metric = FixtureOutputFps,
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
            Metric = FixtureOutputFps,
            ContentMode = BarImageContract.ContentModeImage,
            Width = 160f,
            Height = 24f,
            Image = new C.BarImagePresentationDefinition { Source = "coverage.png" }
        });

        var gauge = new C.GaugeWidgetDefinition
        {
            Id = "coverage-gauge",
            Metric = FixtureOutputFps,
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
            Metric = FixtureOutputFps,
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
                Metric = FixtureOutputFps,
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
        const string metric = FixtureOutputFps;
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
                Metric = FixtureOutputFps,
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
                      "metric": "system.output.desktop-1.fps",
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
            var metrics = new Dictionary<string, object?> { [FixtureOutputFps] = 100d };
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
            "Schema 21 current-schema deserialization must reject unknown widget fields.");
    }

    private static void CanonicalDashboardJsonRoundTrip()
    {
        var definition = CreateMinimalCanonicalDashboard();
        var json = DashboardJson.Serialize(definition);
        var parsed = DashboardJson.ParseCurrent(json, Path.Combine(Path.GetTempPath(), "dashboard.json"));
        var savedAgain = DashboardJson.Serialize(parsed);

        Assert(parsed.SchemaVersion == DashboardSchemaMigration.CurrentSchemaVersion &&
               parsed.Widgets.Count == 1 &&
               parsed.Widgets[0] is PinkieSysMon.DashboardModel.ValueWidgetDefinition value &&
               value.SourceKind == PinkieSysMon.DashboardModel.ValueSourceKind.Text &&
               value.Text == "round-trip",
            "Canonical dashboard JSON must deserialize the polymorphic widget into its concrete schema-19 type.");

        var first = JsonNode.Parse(json)!.ToJsonString();
        var second = JsonNode.Parse(savedAgain)!.ToJsonString();
        Assert(first == second,
            "Canonical schema-21 save -> parse -> save must be structurally idempotent for an unchanged dashboard.");
    }

    private static void PropertyModelSchema19MigrationIdempotence()
    {
        var root = JsonNode.Parse(DashboardJson.Serialize(CreateMinimalCanonicalDashboard(), indented: false))!.AsObject();
        root["schemaVersion"] = DashboardSchemaMigration.PropertyModelNormalizationSchemaVersion;
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

        Assert(definition.SchemaVersion == DashboardSchemaMigration.CurrentSchemaVersion &&
               definition.Widgets.Single() is PinkieSysMon.DashboardModel.ValueWidgetDefinition value &&
               value.Metric == metric,
            "Runtime dashboard loading must migrate schema 18 into the canonical schema-21 typed model before use.");
        Assert(DashboardMetricUsage.Collect(definition).SetEquals([metric]),
            "Runtime metric discovery must consume the canonical schema-21 widget model.");
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
        Assert(MetricContract.DashboardSchemaVersion == DashboardSchemaMigration.CurrentSchemaVersion &&
               MetricContract.DashboardSchemaVersion == 26,
            "The executable current dashboard contract must be schema 26.");

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
                    Metric = FixtureOutputFps,
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

