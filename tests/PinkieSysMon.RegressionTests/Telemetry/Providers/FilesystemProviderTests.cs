using PinkieSysMon;
using PinkieSysMon.Editor;

namespace PinkieSysMon.RegressionTests;

internal static class FilesystemProviderTests
{
    public static void ConfigurationAndSelector()
    {
        WithFixture((root, log) =>
        {
            var path = Path.Combine(root, "sample.dat");
            File.WriteAllBytes(path, [1, 2, 3, 4]);
            var source = Source(path, FilesystemSourceContract.FileKind, "Example");
            var config = new AppConfig();
            Check(!MetricProviderContract.IsEnabled(config.MetricProviders, MetricProviderContract.Filesystem),
                "Filesystem must be disabled on an old or fresh installation.");
            config.Filesystem.Sources.Add(source);
            var configPath = Path.Combine(root, "app.json");
            config.Save(configPath);
            var loaded = AppConfig.Load(configPath, log);
            Check(loaded.Filesystem.Sources.Single().Id == source.Id &&
                  loaded.Filesystem.Sources.Single().Path == path,
                "Filesystem source ID and literal path must survive persistence.");

            loaded.Filesystem.Sources[0].Alias = "Renamed";
            loaded.Save(configPath);
            var renamed = AppConfig.Load(configPath, log);
            var id = FilesystemSourceContract.MetricId(source.Id, FilesystemSourceContract.Size);
            Check(renamed.Filesystem.Sources[0].Id == source.Id &&
                  FilesystemSourceContract.SourceIdForMetric(id.ToUpperInvariant()) == source.Id,
                "Alias changes must not change persisted identity or case-insensitive metric lookup.");
            Check(FilesystemSourceContract.DescriptorsFor(renamed.Filesystem.Sources) is var desc &&
                  desc.Count == 3 &&
                  desc.Single(d => d.Id == id).ValueKind == MetricValueKind.DataSize &&
                  desc.Single(d => d.Id == id).BaseUnit == MetricUnit.Bytes &&
                  desc.Count(d => d.ValueKind == MetricValueKind.DateTime) == 2,
                "The three Filesystem metric descriptors must use canonical DataSize/DateTime kinds.");

            try
            {
                renamed.MetricProviders[MetricProviderContract.Filesystem] = true;
                MetricSelectorEnvironment.Configure(root, log, () => renamed);
                var enabled = MetricSelectorEnvironment.BuildSnapshot(null);
                var selected = enabled.Metrics.SingleOrDefault(m => m.Id == id);
                Check(selected is { Available: true } &&
                      selected.DisplayPath.Select(segment => segment.Text)
                          .SequenceEqual(new[] { "Filesystem", "Renamed", "size" }),
                    "The selector must use the source alias and stable ID, not the private path.");

                renamed.MetricProviders[MetricProviderContract.Filesystem] = false;
                var disabled = MetricSelectorEnvironment.BuildSnapshot(id);
                Check(disabled.Metrics.All(m => m.Id != id) &&
                      disabled.GhostMetric is { Available: false } &&
                      disabled.GhostMetric.Id == id,
                    "Disabled Filesystem source must remain selectable only as an unavailable existing binding.");

                renamed.Filesystem.Sources.Clear();
                renamed.MetricProviders[MetricProviderContract.Filesystem] = true;
                var removed = MetricSelectorEnvironment.BuildSnapshot(id);
                Check(removed.GhostMetric is { Available: false },
                    "Removed sources must retain an unavailable reference for bound widgets.");
            }
            finally
            {
                MetricContract.ReplaceProviderDescriptors(MetricProviderContract.Filesystem, []);
            }

            var badId = new AppConfig();
            badId.Filesystem.Sources.Add(source.Copy());
            badId.Filesystem.Sources.Add(source.Copy());
            ExpectInvalid(() => badId.Save(configPath), "Duplicate source ID must be rejected.");
            var duplicatePath = new AppConfig();
            duplicatePath.Filesystem.Sources.Add(source.Copy());
            var other = Source(path, FilesystemSourceContract.FileKind, "Other");
            duplicatePath.Filesystem.Sources.Add(other);
            ExpectInvalid(() => duplicatePath.Save(configPath), "Duplicate source path must be rejected.");
            var duplicateAlias = new AppConfig();
            duplicateAlias.Filesystem.Sources.Add(source.Copy());
            duplicateAlias.Filesystem.Sources.Add(Source(Path.Combine(root, "other.dat"), FilesystemSourceContract.FileKind, "example"));
            ExpectInvalid(() => duplicateAlias.Save(configPath), "Duplicate source aliases must be rejected.");
            var directoryPath = Path.Combine(root, "directory-source");
            Directory.CreateDirectory(directoryPath);
            var directorySource = Source(directoryPath, FilesystemSourceContract.DirectoryKind, "Folder");
            var folderMetrics = FilesystemSourceContract.DescriptorsFor([directorySource]);
            var filesMetric = FilesystemSourceContract.MetricId(directorySource.Id, FilesystemSourceContract.Files);
            var directoriesMetric = FilesystemSourceContract.MetricId(directorySource.Id, FilesystemSourceContract.Directories);
            Check(folderMetrics.Count == 5 &&
                  folderMetrics.Single(d => d.Id == filesMetric).ValueKind == MetricValueKind.Number &&
                  folderMetrics.Single(d => d.Id == directoriesMetric).ValueKind == MetricValueKind.Number &&
                  folderMetrics.Where(d => d.Id == filesMetric || d.Id == directoriesMetric)
                      .All(d => d.BaseUnit == MetricUnit.None),
                "Directory sources must expose two unitless numeric counts alongside existing metrics.");
            Check(FilesystemSourceContract.DescriptorsFor([source]).Count == 3 &&
                  FilesystemSourceContract.SourceIdForMetric(filesMetric.ToUpperInvariant()) == directorySource.Id,
                "File sources retain three metrics; directory metric IDs must support case-insensitive lookup.");
            using (var directoryProvider = new FilesystemTelemetrySource(
                new FilesystemConfig { Sources = [directorySource] }, log))
            {
                Check(directoryProvider.CanProvide(filesMetric) &&
                      directoryProvider.CanProvide(directoriesMetric),
                    "A Directory source must resolve both count metrics.");
            }
            using (var fileProvider = new FilesystemTelemetrySource(
                new FilesystemConfig { Sources = [source] }, log))
            {
                Check(!fileProvider.CanProvide(FilesystemSourceContract.MetricId(source.Id, FilesystemSourceContract.Files)) &&
                      !fileProvider.CanProvide(FilesystemSourceContract.MetricId(source.Id, FilesystemSourceContract.Directories)),
                    "A File source must never resolve Directory-only metrics.");
            }

            renamed.Filesystem.Sources.Add(directorySource);
            renamed.Save(configPath);
            var savedFolder = AppConfig.Load(configPath, log);
            Check(savedFolder.Filesystem.Sources.Single(s => s.Id == directorySource.Id).Kind ==
                  FilesystemSourceContract.DirectoryKind,
                "Directory source identity must survive configuration persistence.");
            try
            {
                savedFolder.MetricProviders[MetricProviderContract.Filesystem] = true;
                MetricSelectorEnvironment.Configure(root, log, () => savedFolder);
                var snapshot = MetricSelectorEnvironment.BuildSnapshot(null);
                Check(snapshot.Metrics.Any(m => m.Id == filesMetric && m.Available &&
                      m.DisplayPath.Last().Text == FilesystemSourceContract.Files) &&
                      snapshot.Metrics.Any(m => m.Id == directoriesMetric && m.Available &&
                      m.DisplayPath.Last().Text == FilesystemSourceContract.Directories) &&
                      snapshot.Metrics.All(m => m.Id != FilesystemSourceContract.MetricId(source.Id, FilesystemSourceContract.Files)),
                    "Directory counts must appear in the existing selector, never under File sources.");
                savedFolder.MetricProviders[MetricProviderContract.Filesystem] = false;
                var disabledFolder = MetricSelectorEnvironment.BuildSnapshot(filesMetric);
                Check(disabledFolder.GhostMetric is { Available: false } &&
                      disabledFolder.GhostMetric.Id == filesMetric,
                    "Disabled directory-count binding must remain unavailable without rebinding.");
            }
            finally { MetricContract.ReplaceProviderDescriptors(MetricProviderContract.Filesystem, []); }

            var relativePath = new AppConfig();
            relativePath.Filesystem.Sources.Add(Source("relative\\file.txt", FilesystemSourceContract.FileKind, "Bad"));
            ExpectInvalid(() => relativePath.Save(configPath), "Relative paths must be rejected.");

            File.WriteAllText(configPath, "{}");
            var legacy = AppConfig.Load(configPath, log);
            Check(legacy.Filesystem.Sources.Count == 0 &&
                  !MetricProviderContract.IsEnabled(legacy.MetricProviders, MetricProviderContract.Filesystem),
                "Old JSON must retain safe, OFF/empty default behavior.");
        });
    }

    public static void ScannerCompletenessAndBudgets()
    {
        WithFixture((root, _) =>
        {
            // An empty directory is a real complete result containing numeric zero,
            // not a missing reading, root entry, or a fallback.
            var emptyPath = Path.Combine(root, "empty");
            Directory.CreateDirectory(emptyPath);
            var emptySource = Source(emptyPath, FilesystemSourceContract.DirectoryKind, "Empty");
            var emptyResult = FilesystemScanner.Scan(emptySource);
            Check(emptyResult.Complete && emptyResult.Entries == 0 &&
                  emptyResult.Files == 0 && emptyResult.Directories == 0 &&
                  emptyResult.SizeBytes == 0,
                "An empty directory must produce zero files, directories, and size.");
            var fileCountDescriptor = FilesystemSourceContract.DescriptorsFor([emptySource])
                .Single(d => d.Id == FilesystemSourceContract.MetricId(emptySource.Id, FilesystemSourceContract.Files));
            Check(MetricValueFormatter.Format(emptyResult.Files, fileCountDescriptor, null, null,
                      System.Globalization.CultureInfo.InvariantCulture) == "0",
                "A valid zero file count must format as 0, not a fallback or another value.");
            Directory.CreateDirectory(Path.Combine(emptyPath, "child"));
            var emptyChildResult = FilesystemScanner.Scan(emptySource);
            Check(emptyChildResult.Complete && emptyChildResult.Files == 0 &&
                  emptyChildResult.Directories == 1 && emptyChildResult.SizeBytes == 0,
                "A directory containing only an empty child folder must count zero files.");
            
            var folder = Path.Combine(root, "scan");
            var nested = Path.Combine(folder, "nested");
            Directory.CreateDirectory(nested);
            File.WriteAllBytes(Path.Combine(folder, "small.bin"), [1, 2, 3]);
            File.WriteAllBytes(Path.Combine(nested, "other.bin"), [1, 2, 3, 4, 5]);
            var directory = Source(folder, FilesystemSourceContract.DirectoryKind, "Tree");
            var complete = FilesystemScanner.Scan(directory);
            Check(complete.Complete && complete.SizeBytes == 8 &&
                  complete.Entries == 3 && complete.Directories == 1 && complete.Files == 2 &&
                  complete.Created is DateTime && complete.Modified is DateTime,
                "A complete traversal must sum logical lengths and report root timestamps.");

            var exactlyAtLimit = FilesystemScanner.ScanWithEntryLimitForTests(directory, 3);
            Check(exactlyAtLimit.Complete && exactlyAtLimit.Entries == 3,
                "A fully enumerated directory with exactly the entry budget must pass.");
            File.WriteAllBytes(Path.Combine(folder, "extra.bin"), [7]);
            var beyondLimit = FilesystemScanner.ScanWithEntryLimitForTests(directory, 3);
            Check(beyondLimit.Status == FilesystemScanStatus.EntryLimit &&
                  beyondLimit.SizeBytes is null && beyondLimit.Entries == 4,
                "One extra filesystem entry must reject the entire value.");
            File.Delete(Path.Combine(folder, "extra.bin"));

            var file = Source(Path.Combine(folder, "small.bin"), FilesystemSourceContract.FileKind, "File");
            var fileResult = FilesystemScanner.Scan(file);
            Check(fileResult.Complete && fileResult.SizeBytes == 3 && fileResult.Entries == 0,
                "An individual file must not recurse into unrelated directories.");
            var timeout = FilesystemScanner.Scan(directory, elapsedOverride: () => TimeSpan.FromSeconds(16));
            Check(timeout.Status == FilesystemScanStatus.TimedOut && timeout.SizeBytes is null,
                "A timed-out preflight cannot publish a partial size.");
            var cancelled = FilesystemScanner.Scan(directory, new CancellationToken(true));
            Check(cancelled.Status == FilesystemScanStatus.Cancelled && cancelled.SizeBytes is null,
                "Cancellation cannot expose partial success.");
            var wrongKind = FilesystemScanner.Scan(Source(folder, FilesystemSourceContract.FileKind, "Wrong"));
            Check(wrongKind.Status == FilesystemScanStatus.WrongKind, "Type mismatch must be rejected.");
            var missing = FilesystemScanner.Scan(Source(Path.Combine(root, "missing"), FilesystemSourceContract.DirectoryKind, "Missing"));
            Check(missing.Status == FilesystemScanStatus.PathNotFound,
                "Missing path must be distinguished from too many entries.");

            // Reparse-point creation requires a suitable local Windows policy.
            // If available, validate actual traversal without following a cycle.
            var link = Path.Combine(nested, "cycle");
            try
            {
                Directory.CreateSymbolicLink(link, folder);
                var skipped = FilesystemScanner.Scan(directory);
                Check(skipped.Complete && skipped.SizeBytes == 8 &&
                      skipped.SkippedReparse == 1 && skipped.Entries == 4,
                    "A directory junction/symlink must count as an entry without following its target.");
                var rootLink = FilesystemScanner.Scan(Source(link, FilesystemSourceContract.DirectoryKind, "Link"));
                Check(rootLink.Status == FilesystemScanStatus.ReparseRoot,
                    "Reparse-point roots must never bypass traversal safety.");
            }
            catch (Exception error) when (error is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                // Environmental permission to create symlinks is not a test failure.
            }
        });
    }

    public static void RuntimeDemandAndRecovery()
    {
        WithFixture((root, log) =>
        {
            var file = Path.Combine(root, "live.bin");
            File.WriteAllBytes(file, [1, 2, 3, 4]);
            var sourceConfig = Source(file, FilesystemSourceContract.FileKind, "Live");
            var metric = FilesystemSourceContract.MetricId(sourceConfig.Id, FilesystemSourceContract.Size);
            var source = new FilesystemTelemetrySource(
                new FilesystemConfig { Sources = [sourceConfig] }, log, TimeSpan.FromMilliseconds(100));
            try
            {
                var policy = new TelemetryConfig
                {
                    Metrics = new(StringComparer.OrdinalIgnoreCase)
                    {
                        [metric] = new MetricPollingConfig { IntervalMs = 100 }
                    }
                };
                var store = new MetricStore();
                using (var engine = new TelemetryEngine(store, [source], policy,
                           [MetricProviderContract.Filesystem], [], log))
                {
                    engine.Start();
                    Thread.Sleep(250);
                    Check(source.ScanAttempts == 0, "An undemanded source must never scan.");
                    engine.Reconfigure(policy, [MetricProviderContract.Filesystem], [metric]);
                    Check(SpinWait.SpinUntil(() => IsSize(store, metric, 4), TimeSpan.FromSeconds(8)),
                        "Runtime must publish a fully scanned requested source.");

                    File.Delete(file);
                    Check(SpinWait.SpinUntil(() => IsUnavailable(store, metric), TimeSpan.FromSeconds(8)),
                        "A later failed scan must invalidate the previous successful size.");
                    File.WriteAllBytes(file, [1, 2, 3, 4, 5, 6]);
                    Check(SpinWait.SpinUntil(() => IsSize(store, metric, 6), TimeSpan.FromSeconds(8)),
                        "A retained missing source must recover without changing its stable ID.");

                    engine.Reconfigure(policy, [], [metric]);
                    Check(SpinWait.SpinUntil(() => IsUnavailable(store, metric), TimeSpan.FromSeconds(3)),
                        "Provider OFF must publish unavailable, not a cached success.");
                    var attempts = source.ScanAttempts;
                    Thread.Sleep(300);
                    Check(source.ScanAttempts == attempts,
                        "Disabled demand must not schedule additional scans.");
                }
            }
            finally { source.Dispose(); }
        });
    }

    public static void DirectoryCountsAndRecovery()
    {
        WithFixture((root, log) =>
        {
            var folder = Path.Combine(root, "tree");
            var nested = Path.Combine(folder, "nested");
            var deepest = Path.Combine(nested, "deep");
            Directory.CreateDirectory(deepest);
            File.WriteAllBytes(Path.Combine(folder, "first.bin"), [1]);
            File.WriteAllBytes(Path.Combine(nested, "second.bin"), [1, 2]);
            File.WriteAllBytes(Path.Combine(deepest, "third.bin"), [1, 2, 3]);

            var configured = Source(folder, FilesystemSourceContract.DirectoryKind, "Tree");
            var files = FilesystemSourceContract.MetricId(configured.Id, FilesystemSourceContract.Files);
            var directories = FilesystemSourceContract.MetricId(configured.Id, FilesystemSourceContract.Directories);
            var size = FilesystemSourceContract.MetricId(configured.Id, FilesystemSourceContract.Size);
            using var source = new FilesystemTelemetrySource(
                new FilesystemConfig { Sources = [configured] }, log, TimeSpan.FromMilliseconds(150));
            var polling = new TelemetryConfig
            {
                Metrics = new(StringComparer.OrdinalIgnoreCase)
                {
                    [files] = new MetricPollingConfig { IntervalMs = 100 },
                    [directories] = new MetricPollingConfig { IntervalMs = 100 },
                    [size] = new MetricPollingConfig { IntervalMs = 100 }
                }
            };
            var store = new MetricStore();
            using (var engine = new TelemetryEngine(store, [source], polling,
                       [MetricProviderContract.Filesystem], [], log))
            {
                engine.Start();
                Thread.Sleep(250);
                Check(source.ScanAttempts == 0,
                    "Unused directory count metrics must not trigger a filesystem scan.");
                engine.Reconfigure(polling, [MetricProviderContract.Filesystem], [files, directories, size]);
                Check(SpinWait.SpinUntil(() =>
                    IsCount(store, files, 3) && IsCount(store, directories, 2) &&
                    IsSize(store, size, 6), TimeSpan.FromSeconds(8)),
                    "A single complete recursive traversal must publish both counts and logical size.");

                var empty = Path.Combine(folder, "empty");
                Directory.CreateDirectory(empty);
                File.WriteAllBytes(Path.Combine(empty, "fourth.bin"), [9]);
                Check(SpinWait.SpinUntil(() =>
                    IsCount(store, files, 4) && IsCount(store, directories, 3) &&
                    IsSize(store, size, 7), TimeSpan.FromSeconds(8)),
                    "All counts must refresh together after nested filesystem changes.");

                var missing = Path.Combine(root, "tree-moved");
                Directory.Move(folder, missing);
                Check(SpinWait.SpinUntil(() =>
                    IsUnavailable(store, files) && IsUnavailable(store, directories) &&
                    IsUnavailable(store, size), TimeSpan.FromSeconds(8)),
                    "A failed scan must invalidate counts and size as one atomic result.");
                Directory.Move(missing, folder);
                Check(SpinWait.SpinUntil(() =>
                    IsCount(store, files, 4) && IsCount(store, directories, 3) &&
                    IsSize(store, size, 7), TimeSpan.FromSeconds(8)),
                    "Folder count metrics must recover after source reappearance.");

                // Removing every file must transition the existing demanded source from
                // a positive count to a real zero while empty child folders remain.
                File.Delete(Path.Combine(folder, "first.bin"));
                File.Delete(Path.Combine(nested, "second.bin"));
                File.Delete(Path.Combine(deepest, "third.bin"));
                File.Delete(Path.Combine(folder, "empty", "fourth.bin"));
                Check(SpinWait.SpinUntil(() =>
                    IsCount(store, files, 0) && IsCount(store, directories, 3) &&
                    IsSize(store, size, 0), TimeSpan.FromSeconds(8)),
                    "Runtime must publish zero, not the earlier file count or fallback, after a complete empty-file scan.");

                engine.Reconfigure(polling, [], [files, directories, size]);
                Check(SpinWait.SpinUntil(() =>
                    IsUnavailable(store, files) && IsUnavailable(store, directories) &&
                    IsUnavailable(store, size), TimeSpan.FromSeconds(3)),
                    "Disabling the provider must clear all new metrics and previous size.");
            }
        });
    }

    private static FilesystemSourceConfig Source(string path, string kind, string alias) => new()
    {
        Id = FilesystemSourceContract.NewId(), Path = path, Kind = kind, Alias = alias
    };

    private static bool IsSize(MetricStore store, string metric, long expected) =>
        store.Snapshot().TryGetValue(metric, out var value) && value is long length && length == expected;

    private static bool IsCount(MetricStore store, string metric, int expected) =>
        store.Snapshot().TryGetValue(metric, out var value) && value is int count && count == expected;

    private static bool IsUnavailable(MetricStore store, string metric) =>
        store.Snapshot().TryGetValue(metric, out var value) && value is null;

    private static void ExpectInvalid(Action action, string message)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException(message);
    }

    private static void WithFixture(Action<string, FileLogger> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "pinkie-filesystem-regression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Exception? failure = null;
        try
        {
            action(root, new FileLogger(Path.Combine(root, "regression.log")));
        }
        catch (Exception error) { failure = error; throw; }
        finally { RegressionFixtureCleanup.Delete(root, failure); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
