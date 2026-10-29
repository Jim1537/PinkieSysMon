using System.Diagnostics;
using System.Text.Json;

namespace PinkieSysMon;

// Explicit outcomes describe observed boundaries, not inferred application success.
internal sealed class LogOperation : IDisposable
{
    private readonly FileLogger _log;
    private readonly string _operation;
    private readonly string? _subject;
    private readonly string? _parent;
    private readonly long _started = Stopwatch.GetTimestamp();
    private string _phase = "begin";
    private bool _finished;

    internal LogOperation(FileLogger log, string operation, string? subject = null, string? parent = null, string? id = null)
    {
        Id = id ?? Guid.NewGuid().ToString("N");
        _log = log;
        _operation = operation;
        _subject = subject;
        _parent = parent;
        Write("started");
    }

    internal string Id { get; }
    internal string CurrentPhase => _phase;

    internal void Phase(string phase, string? detail = null)
    {
        _phase = phase;
        Write("progress", detail);
    }

    internal void Complete(string? detail = null) => Finish("succeeded", detail);
    internal void Partial(string detail) => Finish("partial", detail);
    internal void Cancel(string? detail = null) => Finish("cancelled", detail);

    internal void Fail(Exception error, string? detail = null)
    {
        _finished = true;
        Write("failed", detail, error);
    }

    internal void RecordFailure(Exception? error, string phase, string? detail = null) =>
        Write("failure", detail, error, phase, errorRecord: true);

    internal void Pending(string phase, string detail) =>
        Write("pending", detail, phase: phase, errorRecord: true);

    private void Finish(string outcome, string? detail)
    {
        if (_finished) return;
        _finished = true;
        Write(outcome, detail);
    }

    private void Write(string outcome, string? detail = null, Exception? error = null, string? phase = null,
        bool errorRecord = false)
    {
        var message = JsonSerializer.Serialize(new
        {
            eventName = _operation + "." + outcome,
            operation = _operation, operationId = Id, parentOperationId = _parent,
            subject = _subject, phase = phase ?? _phase, outcome,
            elapsedMs = Stopwatch.GetElapsedTime(_started).TotalMilliseconds, detail
        });
        if (error is null && !errorRecord) _log.Info(message);
        else _log.Error(message, error);
    }

    public void Dispose()
    {
        if (!_finished) Finish("incomplete", "Scope ended without a confirmed terminal outcome.");
    }
}

