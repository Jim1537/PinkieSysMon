using System.Runtime.CompilerServices;

namespace PinkieSysMon;

// Attach bounded managed context while deep resource owners may be under an app
// gate. The existing outer error boundary performs the only diagnostic I/O.
internal static class RenderFailureContext
{
    private static readonly ConditionalWeakTable<Exception, State> Contexts = new();
    internal sealed record Snapshot(string[] Phases, string[] LogicalSubjects, long Omitted);
    private sealed class State
    {
        internal readonly List<string> Phases = [], Subjects = [];
        internal long Omitted;
    }
    internal static void Add(Exception error, string phase, string? subject = null)
    {
        try
        {
            var state = Contexts.GetValue(error, _ => new());
            lock (state)
            {
                Add(state.Phases, phase, state);
                if (!string.IsNullOrWhiteSpace(subject)) Add(state.Subjects, subject, state);
            }
        }
        catch { /* Metadata must never replace the original resource failure. */ }
    }
    private static void Add(List<string> values, string value, State state)
    {
        value = value.Length <= 128 ? value : value[..128] + " [TRUNCATED]";
        if (values.Contains(value)) return;
        if (values.Count < 8) values.Add(value); else state.Omitted++;
    }
    internal static Snapshot? Find(Exception? error)
    {
        for (var depth = 0; error is not null && depth < 8; depth++, error = error.InnerException)
        {
            if (!Contexts.TryGetValue(error, out var state)) continue;
            lock (state) return new(state.Phases.ToArray(), state.Subjects.ToArray(), state.Omitted);
        }
        return null;
    }
}
