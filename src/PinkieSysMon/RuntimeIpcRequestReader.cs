using System.Text;

namespace PinkieSysMon;

// Existing commands and future Test controls share one finite receive limit.
// Each IPC connection carries exactly one line, so trailing bytes are ignored.
internal static class RuntimeIpcRequestReader
{
    internal const int MaxCommandCharacters = 8192;

    internal static async Task<string?> ReadLineAsync(
        TextReader reader, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var buffer = new char[256];
        var line = new StringBuilder();

        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)
                .ConfigureAwait(false);
            if (count == 0)
                return line.Length == 0 ? null : line.ToString();

            var newline = Array.IndexOf(buffer, '\n', 0, count);
            var length = newline < 0 ? count : newline;
            if (line.Length + length > MaxCommandCharacters)
                throw new InvalidDataException("Runtime IPC command exceeds the permitted length.");

            line.Append(buffer, 0, length);
            if (newline >= 0)
                return line.ToString().TrimEnd('\r');
        }
    }
}
