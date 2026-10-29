using System.Runtime.InteropServices;
using SkiaSharp;

namespace PinkieSysMon;

// Per-renderer acquisition owner. Neither a Shell invocation path nor an asset-path resolver.
internal sealed class ShellIconCache : IDisposable
{
    private const int MaximumEntries = 256;
    private const long MaximumDecodedBytes = 4L * 1024 * 1024;
    private readonly object _gate = new();
    private readonly Dictionary<string, Task<ImageAsset?>> _entries = new(StringComparer.Ordinal);
    // Shell extensions may stall. Retired renderers cancel queued work; at most two native
    // extractions can remain in flight across the entire process, including retired caches.
    private static readonly SemaphoreSlim Workers = new(2);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Func<string, ImageAsset?> _extract;
    private readonly bool _supported;
    private long _decodedBytes;
    private bool _disposed;
    private readonly FileLogger? _log;
    private readonly DiagnosticStateObserver? _diagnostics;
    private readonly string _incident = "shell-icon." + Guid.NewGuid().ToString("N");
    private long _capacityRefusals;
    private Task _closeCompletion = Task.CompletedTask;
    internal Task AcquisitionCompletion { get { lock (_gate) return Task.WhenAll(_entries.Values); } }
    internal Task CloseCompletion { get { lock (_gate) return _closeCompletion; } }

    internal ShellIconCache(Func<string, ImageAsset?>? extract = null, FileLogger? log = null)
    {
        _log = log;
        _diagnostics = log is null ? null : new(log, "render.shell-icon");
        _extract = extract ?? Extract;
        _supported = extract is not null || OperatingSystem.IsWindows();
    }

    public ImageAsset? Get(string target, out bool pending, string? widgetId = null)
    {
        pending = false;
        if (string.IsNullOrWhiteSpace(target) || !_supported)
            return null;
        lock (_gate)
        {
            if (_disposed) return null;
            if (!_entries.TryGetValue(target, out var task))
            {
                if (_entries.Count >= MaximumEntries) { _capacityRefusals++; return null; }
                var token = _lifetime.Token;
                task = Task.Run(async () =>
                {
                    ImageAsset? asset = null;
                    var entered = false;
                    try
                    {
                        await Workers.WaitAsync(token).ConfigureAwait(false);
                        entered = true;
                        token.ThrowIfCancellationRequested();
                        bool retired;
                        lock (_gate) retired = _disposed;
                        if (retired) return null;
                        asset = _extract(target);
                        bool rejected;
                        lock (_gate)
                        {
                            rejected = _disposed || asset is not null && asset.DecodedByteCount > MaximumDecodedBytes - _decodedBytes;
                            if (!rejected && asset is not null) _decodedBytes += asset.DecodedByteCount;
                        }
                        if (rejected)
                        {
                            asset?.Dispose();
                            _diagnostics?.Observe("acquisition", "retired-or-budget-fallback");
                            return null;
                        }
                        _diagnostics?.Observe("acquisition", asset is null ? "no-icon" : "available",
                            $"logicalWidget={BoundWidget(widgetId)}; decodedBytes={asset?.DecodedByteCount ?? 0}");
                        return asset;
                    }
                    catch (OperationCanceledException) { asset?.Dispose(); return null; }
                    catch (Exception ex) when (ex is ExternalException or ArgumentException or InvalidDataException)
                    {
                        asset?.Dispose();
                        RenderFailureContext.Add(ex, "shell.icon.acquire", widgetId);
                        _log?.ErrorThrottled(_incident, TimeSpan.FromSeconds(30), "Shell icon acquisition failed; cached optional fallback remains.", ex);
                        _diagnostics?.Observe("acquisition", "failed", $"logicalWidget={BoundWidget(widgetId)}; cachedFallback=true");
                        return null;
                    }
                    finally { if (entered) Workers.Release(); }
                });
                _entries.Add(target, task);
            }
            pending = !task.IsCompleted;
            return task.IsCompletedSuccessfully ? task.Result : null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _lifetime.Cancel();
            var tasks = _entries.Values.ToArray();
            var entries = _entries.Count; var bytes = _decodedBytes; var refused = _capacityRefusals;
            foreach (var task in _entries.Values)
            {
                // Completion and publication share this gate. Late results are disposed by their worker.
                if (task.IsCompletedSuccessfully) task.Result?.Dispose();
                else _ = task.ContinueWith(completed => completed.Result?.Dispose(),
                    CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
            }
            _entries.Clear();
            _decodedBytes = 0;
            _closeCompletion = Task.WhenAll(tasks).ContinueWith(completed =>
            {
                _ = completed.Exception;
                _log?.FlushSuppressed(_incident);
                _diagnostics?.Dispose();
                if (entries > 0 || refused > 0) _log?.Info(System.Text.Json.JsonSerializer.Serialize(new
                { eventName = "render.shell-icon.closed", cacheIncident = _incident, entries, decodedBytesAtClose = bytes, capacityRefusals = refused }));
                _lifetime.Dispose();
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
        // Outstanding Shell calls cannot be forcibly interrupted; workers release their semaphore.
    }

    private static string BoundWidget(string? id) => id is null ? "unknown" : id.Length <= 128 ? id : id[..128] + " [TRUNCATED]";

    private static ImageAsset? Extract(string target)
    {
        var initialization = CoInitializeEx(IntPtr.Zero, 0);
        // RPC_E_CHANGED_MODE means COM is already initialized in another apartment.
        // Other failures must not enter Shell APIs without their COM prerequisite.
        if (initialization < 0 && initialization != unchecked((int)0x80010106))
            throw new COMException("Shell icon COM initialization failed.", initialization);
        var initialized = initialization >= 0;
        IntPtr item = IntPtr.Zero;
        var info = new ShellFileInfo();
        try
        {
            var parsed = SHParseDisplayName(target, IntPtr.Zero, out item, 0, out _);
            if (parsed < 0) throw new COMException("Shell icon target resolution failed.", parsed);
            if (item == IntPtr.Zero) return null;
            if (SHGetFileInfo(item, 0, ref info, (uint)Marshal.SizeOf<ShellFileInfo>(), 0x100 | 0x8) == IntPtr.Zero || info.Icon == IntPtr.Zero)
                return null;
            using var icon = Icon.FromHandle(info.Icon);
            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            var decoded = SKBitmap.Decode(stream.ToArray());
            if (decoded is null) return null;
            try { return ImageAsset.FromBitmap(decoded); }
            catch { decoded.Dispose(); throw; }
        }
        finally
        {
            if (info.Icon != IntPtr.Zero) DestroyIcon(info.Icon);
            if (item != IntPtr.Zero) Marshal.FreeCoTaskMem(item);
            if (initialized) CoUninitialize();
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string? DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string? TypeName;
    }
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint apartment);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindContext, out IntPtr item, uint attributes, out uint resultAttributes);
    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(IntPtr item, uint attributes, ref ShellFileInfo info, uint size, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}

