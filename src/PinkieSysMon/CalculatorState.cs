using System.Globalization;

namespace PinkieSysMon;

// Transient calculation state. One instance belongs to one widget in one Desktop renderer.
internal sealed class CalculatorState
{
    private const int MaximumEntryDigits = 16;
    private string _entry = "0";
    private double? _left;
    private string? _operator;
    private string? _repeatOperator;
    private double? _repeatOperand;
    private bool _replaceEntry = true;
    private bool _afterEquals;
    private bool _error;

    public string Readout => _error ? "Error" : _entry;

    public bool Press(string key, Func<string?>? clipboardRead = null, Action<string>? clipboardWrite = null)
    {
        if (key == "Copy")
        {
            if (_error || clipboardWrite is null) return false;
            try { clipboardWrite(_entry); }
            catch (Exception) { /* An unavailable clipboard must not affect arithmetic. */ }
            return false;
        }

        if (key == "Paste")
        {
            if (clipboardRead is null) return false;
            string? text;
            try { text = clipboardRead(); }
            catch (Exception) { return false; }
            if (!TryParseDecimal(text, out var pasted)) return false;
            if (_error) return false;
            if (_afterEquals) ClearPending();
            _entry = Format(pasted);
            _replaceEntry = false;
            _afterEquals = false;
            _repeatOperator = null;
            _repeatOperand = null;
            return true;
        }

        if (key == "C") { Clear(); return true; }
        if (key == "CE")
        {
            if (_error) { Clear(); return true; }
            _entry = "0";
            _replaceEntry = true;
            _afterEquals = false;
            _repeatOperator = null;
            _repeatOperand = null;
            return true;
        }

        if (_error) return false;
        if (key.Length == 1 && key[0] is >= '0' and <= '9')
        {
            BeginEntry();
            if (DigitCount(_entry) >= MaximumEntryDigits) return false;
            _entry = _entry == "0" ? key : _entry == "-0" ? "-" + key : _entry + key;
            return true;
        }

        switch (key)
        {
            case ".":
                BeginEntry();
                if (!_entry.Contains('.')) _entry += ".";
                return true;
            case "±":
                if (_afterEquals) { ClearPending(); _afterEquals = false; }
                _entry = _entry.StartsWith("-", StringComparison.Ordinal)
                    ? _entry[1..] : _entry == "0" ? "0" : "-" + _entry;
                return true;
            case "⌫":
                if (_replaceEntry) return false;
                if (_entry.Contains('E') || _entry.Contains('e'))
                {
                    _entry = "0";
                    return true;
                }
                _entry = _entry.Length <= 1 || _entry.Length == 2 && _entry[0] == '-'
                    ? "0" : _entry[..^1];
                return true;
            case "+": case "−": case "×": case "÷":
                return SetOperator(key);
            case "=":
                return EqualsKey();
            default:
                return false;
        }
    }

    private void BeginEntry()
    {
        if (_afterEquals) ClearPending();
        if (_replaceEntry || _entry.Contains('E') || _entry.Contains('e')) _entry = "0";
        _replaceEntry = false;
        _afterEquals = false;
        _repeatOperator = null;
        _repeatOperand = null;
    }

    private bool SetOperator(string next)
    {
        if (_operator is not null && !_replaceEntry)
        {
            if (!Calculate(_left!.Value, ParseEntry(), _operator, out var result))
                return SetError();
            _entry = Format(result);
        }
        _left = ParseEntry();
        _operator = next;
        _repeatOperator = null;
        _repeatOperand = null;
        _replaceEntry = true;
        _afterEquals = false;
        return true;
    }

    private bool EqualsKey()
    {
        double left, right;
        string operation;
        if (_operator is not null)
        {
            left = _left!.Value;
            right = _replaceEntry ? left : ParseEntry();
            operation = _operator;
        }
        else if (_afterEquals && _repeatOperator is not null && _repeatOperand is not null)
        {
            left = ParseEntry();
            right = _repeatOperand.Value;
            operation = _repeatOperator;
        }
        else return false;

        if (!Calculate(left, right, operation, out var result))
            return SetError();
        _entry = Format(result);
        _repeatOperator = operation;
        _repeatOperand = right;
        _operator = null;
        _left = null;
        _replaceEntry = true;
        _afterEquals = true;
        return true;
    }

    private static bool Calculate(double left, double right, string operation, out double result)
    {
        result = operation switch
        {
            "+" => left + right,
            "−" => left - right,
            "×" => left * right,
            "÷" when right != 0d => left / right,
            _ => double.NaN
        };
        return double.IsFinite(result);
    }

    private bool SetError()
    {
        _error = true;
        _operator = null;
        _left = null;
        _repeatOperand = null;
        _repeatOperator = null;
        return true;
    }

    private void ClearPending()
    {
        _operator = null;
        _left = null;
        _repeatOperator = null;
        _repeatOperand = null;
    }

    private void Clear()
    {
        ClearPending();
        _entry = "0";
        _replaceEntry = true;
        _afterEquals = false;
        _error = false;
    }

    private double ParseEntry() => double.Parse(_entry, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static string Format(double number) =>
        number == 0d ? "0" : number.ToString("G15", CultureInfo.InvariantCulture);

    private static int DigitCount(string entry) => entry.Count(char.IsDigit);

    // No thousands separators, currency symbols, NaN, Infinity or multi-value expressions.
    internal static bool TryParseDecimal(string? text, out double value)
    {
        value = 0;
        text = text?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > 64) return false;
        var index = 0;
        if (text[index] is '+' or '-') index++;
        var digits = 0;
        while (index < text.Length && char.IsAsciiDigit(text[index])) { index++; digits++; }
        if (index < text.Length && text[index] == '.')
        {
            index++;
            while (index < text.Length && char.IsAsciiDigit(text[index])) { index++; digits++; }
        }
        if (digits == 0) return false;
        if (index < text.Length && text[index] is 'e' or 'E')
        {
            index++;
            if (index < text.Length && text[index] is '+' or '-') index++;
            var exponentStart = index;
            while (index < text.Length && char.IsAsciiDigit(text[index])) index++;
            if (index == exponentStart) return false;
        }
        return index == text.Length &&
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
            double.IsFinite(value);
    }
}
