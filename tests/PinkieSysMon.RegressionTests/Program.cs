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

