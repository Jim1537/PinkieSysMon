namespace PinkieSysMon.RegressionTests;

// Named-mutex tests use synthetic names and never open a device or real Runtime.
internal static class Issue95RuntimeProcessLeaseTests
{
    public static void ConcurrentOwnersFailClosed()
    {
        var name = @"Local\PinkieSysMon.Regression.Owner." + Guid.NewGuid().ToString("N");
        using var owner = RuntimeProcessLease.TryAcquire(name)
            ?? throw new InvalidOperationException("The first synthetic Runtime must own the lease.");

        bool secondAcquired = false;
        Exception? workerFailure = null;
        var contender = new Thread(() =>
        {
            try
            {
                using var second = RuntimeProcessLease.TryAcquire(name);
                secondAcquired = second is not null;
            }
            catch (Exception ex)
            {
                workerFailure = ex;
            }
        }) { IsBackground = true };

        contender.Start();
        Check(contender.Join(TimeSpan.FromSeconds(5)), "Competing Runtime lease check hung.");
        if (workerFailure is not null)
            throw new InvalidOperationException("Competing Runtime lease check failed.", workerFailure);
        Check(!secondAcquired,
            "A second thread/process must not acquire an active Runtime's USB ownership lease.");
    }

    public static void ReleasedOwnerAllowsNewAcquisition()
    {
        var name = @"Local\PinkieSysMon.Regression.Owner." + Guid.NewGuid().ToString("N");
        using (var first = RuntimeProcessLease.TryAcquire(name))
        {
            Check(first is not null, "Initial Runtime lease must be available.");
        }

        bool recovered = false;
        Exception? workerFailure = null;
        var next = new Thread(() =>
        {
            try
            {
                using var second = RuntimeProcessLease.TryAcquire(name);
                recovered = second is not null;
            }
            catch (Exception ex)
            {
                workerFailure = ex;
            }
        }) { IsBackground = true };
        next.Start();
        Check(next.Join(TimeSpan.FromSeconds(5)), "Released Runtime lease could not be checked.");
        if (workerFailure is not null)
            throw new InvalidOperationException("Released Runtime lease check failed.", workerFailure);
        Check(recovered, "A cleanly released lease must be available to the next Runtime.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
