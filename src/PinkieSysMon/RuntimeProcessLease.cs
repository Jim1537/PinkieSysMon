using System.Security.Principal;

namespace PinkieSysMon;

// Process-level admission, not device identification and not an IPC success signal.
// All installations for the same Windows account share this mutex across sessions.
internal sealed class RuntimeProcessLease : IDisposable
{
    private const string UserMutexPrefix = @"Global\PinkieSysMon.Runtime.Owner.v1.";
    private readonly Mutex _mutex;
    private bool _released;

    private RuntimeProcessLease(Mutex mutex) => _mutex = mutex;

    internal static RuntimeProcessLease? TryAcquireForCurrentUser()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Runtime USB ownership requires Windows.");

        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value
            ?? throw new InvalidOperationException("The Windows account SID is unavailable; Runtime ownership cannot be verified.");
        return TryAcquire(UserMutexPrefix + sid);
    }

    // Explicit names allow isolated deterministic concurrency tests without
    // competing with the user's real Runtime.
    internal static RuntimeProcessLease? TryAcquire(string mutexName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);
        var mutex = new Mutex(initiallyOwned: false, name: mutexName);
        try
        {
            bool acquired;
            try
            {
                acquired = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                // The prior owning thread is dead; Windows released its ownership.
                acquired = true;
            }

            if (acquired)
                return new RuntimeProcessLease(mutex);

            mutex.Dispose();
            return null;
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_released)
            return;
        // A Mutex is thread-owned; always release on the acquiring thread.
        _mutex.ReleaseMutex();
        _released = true;
        _mutex.Dispose();
    }
}
