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

    private static FilesystemSourceConfig Source(string path, string kind, string alias) => new()
    {
        Id = FilesystemSourceContract.NewId(), Path = path, Kind = kind, Alias = alias
    };

    private static bool IsSize(MetricStore store, string metric, long expected) =>
        store.Snapshot().TryGetValue(metric, out var value) && value is long length && length == expected;

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
