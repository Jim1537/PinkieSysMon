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
        TrueIfEqual
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
            !double.TryParse(
                setpoint,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out var reference) ||
            !double.IsFinite(reference))
        {
            return false;
        }

        switch (trueIf)
        {
            case TrueIfGreaterThan:
                state = numeric > reference;
                return true;
            case TrueIfLessThan:
                state = numeric < reference;
                return true;
            case TrueIfGreaterThanOrEqual:
                state = numeric >= reference;
                return true;
            case TrueIfLessThanOrEqual:
                state = numeric <= reference;
                return true;
            case TrueIfEqual:
                state = numeric == reference;
                return true;
            default:
                return false;
        }
    }

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
