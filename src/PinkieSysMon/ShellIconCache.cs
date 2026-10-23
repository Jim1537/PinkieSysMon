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

    internal ShellIconCache(Func<string, ImageAsset?>? extract = null)
    {
        _extract = extract ?? Extract;
        _supported = extract is not null || OperatingSystem.IsWindows();
    }

    public ImageAsset? Get(string target, out bool pending)
    {
        pending = false;
        if (string.IsNullOrWhiteSpace(target) || !_supported)
            return null;
        lock (_gate)
        {
            if (_disposed) return null;
            if (!_entries.TryGetValue(target, out var task))
            {
                if (_entries.Count >= MaximumEntries) return null;
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
                        lock (_gate) { if (_disposed) return null; }
                        asset = _extract(target);
                        lock (_gate)
                        {
                            if (_disposed || asset is not null && asset.DecodedByteCount > MaximumDecodedBytes - _decodedBytes)
                            {
                                asset?.Dispose();
                                return null;
                            }
                            if (asset is not null) _decodedBytes += asset.DecodedByteCount;
                            return asset;
                        }
                    }
                    catch (OperationCanceledException) { asset?.Dispose(); return null; }
                    catch (Exception ex) when (ex is ExternalException or ArgumentException or InvalidDataException)
                    {
                        asset?.Dispose();
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
            foreach (var task in _entries.Values)
            {
                // Completion and publication share this gate. Late results are disposed by their worker.
                if (task.IsCompletedSuccessfully) task.Result?.Dispose();
                else _ = task.ContinueWith(completed => completed.Result?.Dispose(),
                    CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
            }
            _entries.Clear();
            _decodedBytes = 0;
            _ = Task.WhenAll(tasks).ContinueWith(completed =>
            {
                _ = completed.Exception;
                _lifetime.Dispose();
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
        // Outstanding Shell calls cannot be forcibly interrupted; workers release their semaphore.
    }

    private static ImageAsset? Extract(string target)
    {
        var initialization = CoInitializeEx(IntPtr.Zero, 0);
        // RPC_E_CHANGED_MODE means COM is already initialized in another apartment.
        // Other failures must not enter Shell APIs without their COM prerequisite.
        if (initialization < 0 && initialization != unchecked((int)0x80010106)) return null;
        var initialized = initialization >= 0;
        IntPtr item = IntPtr.Zero;
        var info = new ShellFileInfo();
        try
        {
            if (SHParseDisplayName(target, IntPtr.Zero, out item, 0, out _) < 0 || item == IntPtr.Zero)
                return null;
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
