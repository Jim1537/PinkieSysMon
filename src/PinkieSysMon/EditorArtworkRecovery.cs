namespace PinkieSysMon;

// Only Editor preview supplies this policy. Path and source-kind violations still propagate.
internal sealed class EditorArtworkRecovery
{
    private readonly HashSet<string> _sources = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyCollection<string> Sources => _sources;

    public void Record(string source) => _sources.Add(source);

    public T? Decode<T>(string path, Func<string, T> decode) where T : class
    {
        try { return decode(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }
}
