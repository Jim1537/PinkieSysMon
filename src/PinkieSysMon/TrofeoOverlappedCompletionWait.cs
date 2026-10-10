namespace PinkieSysMon;

// Waits for a pending OVERLAPPED operation without giving up ownership of its
// event, native OVERLAPPED memory, or transfer buffer while Windows still owns it.
// The native transport supplies CancelIoEx and WinUsb_GetOverlappedResult callbacks.
internal static class TrofeoOverlappedCompletionWait
{
    // Returns true only when cancellation won the wait. The caller must still
    // map that result to OperationCanceledException after terminal completion.
    public static bool Wait(
        WaitHandle completionEvent,
        CancellationToken cancellationToken,
        Action requestCancellation,
        Action awaitTerminalCompletion)
    {
        ArgumentNullException.ThrowIfNull(completionEvent);
        ArgumentNullException.ThrowIfNull(requestCancellation);
        ArgumentNullException.ThrowIfNull(awaitTerminalCompletion);

        if (!cancellationToken.CanBeCanceled)
        {
            completionEvent.WaitOne();
            return false;
        }

        // The completion event precedes the cancellation handle intentionally:
        // a completed transfer wins when both were signaled before observation.
        var outcome = WaitHandle.WaitAny([completionEvent, cancellationToken.WaitHandle]);
        if (outcome == 0)
            return false;

        // CancelIoEx is a request, NOT completion. Always drain the operation
        // before the caller may free or reuse native OVERLAPPED/buffer/event.
        try
        {
            requestCancellation();
        }
        finally
        {
            awaitTerminalCompletion();
        }

        return true;
    }
}
