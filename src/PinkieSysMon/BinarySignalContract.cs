using System.Globalization;

namespace PinkieSysMon;

internal static class BinarySignalContract
{
    public const string TrueKey = "true";
    public const string FalseKey = "false";

    public const string EvaluationModeAuto = "Auto";
    public const string EvaluationModeSetpoint = "Setpoint";

    public const string TrueIfGreaterThan = ">";
    public const string TrueIfLessThan = "<";
    public const string TrueIfGreaterThanOrEqual = ">=";
    public const string TrueIfLessThanOrEqual = "<=";
    public const string TrueIfEqual = "=";
    public const string TrueIfNotEqual = "!=";

    public static readonly IReadOnlyList<string> SupportedEvaluationModes =
    [
        EvaluationModeAuto,
        EvaluationModeSetpoint
    ];

    public static readonly IReadOnlyList<string> SupportedTrueIfOperators =
    [
        TrueIfGreaterThan,
        TrueIfLessThan,
        TrueIfGreaterThanOrEqual,
        TrueIfLessThanOrEqual,
        TrueIfEqual,
        TrueIfNotEqual
    ];

    public static bool TryResolve(
        object? value,
        string? evaluationMode,
        string? setpoint,
        string? trueIf,
        out bool state)
    {
        if (string.Equals(evaluationMode, EvaluationModeSetpoint, StringComparison.OrdinalIgnoreCase))
            return TryResolveSetpoint(value, setpoint, trueIf, out state);

        return TryResolve(value, out state);
    }

    public static bool TryResolve(object? value, out bool state)
    {
        state = false;
        if (value is null)
            return false;

        if (value is bool boolean)
        {
            state = boolean;
            return true;
        }

        if (value is string text)
        {
            if (text.Length == 0)
            {
                state = false;
                return true;
            }

            if (IsUnavailableText(text))
                return false;

            state = true;
            return true;
        }

        if (!TryGetFiniteNumber(value, out var numeric))
            return false;

        state = numeric != 0d;
        return true;
    }

    private static bool TryResolveSetpoint(
        object? value,
        string? setpoint,
        string? trueIf,
        out bool state)
    {
        state = false;
        if (value is null)
            return false;

        if (value is bool boolean)
        {
            var expected = string.Equals(setpoint, "true", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(setpoint, "1", StringComparison.Ordinal);
            state = boolean == expected;
            return true;
        }

        if (value is string text)
        {
            if (IsUnavailableText(text))
                return false;

            state = string.Equals(text, setpoint ?? string.Empty, StringComparison.Ordinal);
            return true;
        }

        if (!TryGetFiniteNumber(value, out var numeric) ||
            string.IsNullOrWhiteSpace(setpoint) ||
            !SupportedTrueIfOperators.Contains(trueIf, StringComparer.Ordinal))
        {
            return false;
        }

        // Validate every alternative before reporting a match. An invalid term must
        // remain unavailable even if an earlier alternative matched.
        var remaining = setpoint.AsSpan();
        while (true)
        {
            var separator = remaining.IndexOf('|');
            var term = (separator < 0 ? remaining : remaining[..separator]).Trim();
            if (!TryParseNumericTerm(term, out var lower, out var upper))
            {
                state = false;
                return false;
            }

            // A range is a set of candidate reference values, not a pair of
            // endpoint comparisons. Each operator asks whether any candidate
            // in [lower, upper] satisfies the comparison.
            state |= trueIf switch
            {
                TrueIfGreaterThan => numeric > lower,
                TrueIfLessThan => numeric < upper,
                TrueIfGreaterThanOrEqual => numeric >= lower,
                TrueIfLessThanOrEqual => numeric <= upper,
                TrueIfEqual => numeric >= lower && numeric <= upper,
                TrueIfNotEqual => lower != upper || numeric != lower,
                _ => false
            };

            if (separator < 0)
                return true;

            remaining = remaining[(separator + 1)..];
        }
    }

    private static bool TryParseNumericTerm(
        ReadOnlySpan<char> term,
        out double lower,
        out double upper)
    {
        lower = default;
        upper = default;

        // Parse the entire scalar first to preserve legacy invariant-culture
        // thousands grouping and signed/exponent values (e.g. 1,234, -5, 1e-3).
        if (double.TryParse(
                term,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out var scalar))
        {
            if (!double.IsFinite(scalar))
                return false;

            lower = upper = scalar;
            return true;
        }

        // A minus sign is a range separator only if both sides independently
        // parse as finite numbers. This permits negative/exponent endpoints
        // without confusing their signs with the range separator.
        var found = false;
        for (var i = 1; i < term.Length; i++)
        {
            if (term[i] != '-' ||
                !TryParseFiniteReference(term[..i].Trim(), out var start) ||
                !TryParseFiniteReference(term[(i + 1)..].Trim(), out var finish))
            {
                continue;
            }

            // Reject reversed or ambiguous interpretations rather than guessing.
            if (found || start > finish)
                return false;

            lower = start;
            upper = finish;
            found = true;
        }

        return found;
    }

    private static bool TryParseFiniteReference(ReadOnlySpan<char> term, out double number) =>
        double.TryParse(
            term,
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out number) &&
        double.IsFinite(number);

    private static bool TryGetFiniteNumber(object value, out double numeric)
    {
        numeric = default;
        switch (Type.GetTypeCode(value.GetType()))
        {
            case TypeCode.SByte:
            case TypeCode.Byte:
            case TypeCode.Int16:
            case TypeCode.UInt16:
            case TypeCode.Int32:
            case TypeCode.UInt32:
            case TypeCode.Int64:
            case TypeCode.UInt64:
            case TypeCode.Single:
            case TypeCode.Double:
            case TypeCode.Decimal:
                try
                {
                    numeric = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                }
                catch
                {
                    return false;
                }

                return double.IsFinite(numeric);

            default:
                return false;
        }
    }

    private static bool IsUnavailableText(string text) =>
        text.Equals("nil", StringComparison.OrdinalIgnoreCase) ||
        text.Equals("undefined", StringComparison.OrdinalIgnoreCase);
}
