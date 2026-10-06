using System.Globalization;

namespace PinkieSysMon;

/// <summary>
/// Converts numeric metric values from provider-native units into a widget-selected
/// presentation unit. Quantitative renderers and text formatting share this path so
/// Min/Max, thresholds, and setpoints all use the same numeric semantics.
/// </summary>
internal static class MetricValueConverter
{
    public static bool TryConvertNumeric(
        object? value,
        MetricDescriptor descriptor,
        string? unit,
        out double converted)
    {
        converted = default;
        if (value is null || !MetricContract.IsNumericValueKind(descriptor.ValueKind))
            return false;

        try
        {
            converted = ConvertNumeric(value, descriptor, unit);
            return double.IsFinite(converted);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (InvalidCastException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static double ConvertNumeric(object value, MetricDescriptor descriptor, string? unit)
    {
        if (!MetricContract.IsNumericValueKind(descriptor.ValueKind))
            throw new InvalidOperationException($"Metric '{descriptor.Id}' is not numeric.");

        var nativeValue = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        if (!double.IsFinite(nativeValue))
            throw new FormatException($"Metric '{descriptor.Id}' is not a finite numeric value.");

        var normalizedUnit = Normalize(unit);
        if (normalizedUnit is null)
            return nativeValue;

        return descriptor.ValueKind switch
        {
            MetricValueKind.Duration => ConvertDuration(nativeValue, descriptor.BaseUnit, normalizedUnit),
            MetricValueKind.DataSize => ConvertDataSize(nativeValue, descriptor.BaseUnit, normalizedUnit),
            MetricValueKind.Number or MetricValueKind.Percent =>
                ConvertNumber(nativeValue, descriptor.BaseUnit, normalizedUnit),
            _ => throw new InvalidOperationException($"Metric '{descriptor.Id}' is not numeric.")
        };
    }

    public static double ToMilliseconds(double nativeValue, MetricUnit baseUnit) => baseUnit switch
    {
        MetricUnit.Milliseconds => nativeValue,
        MetricUnit.Seconds => nativeValue * 1000d,
        MetricUnit.Minutes => nativeValue * 60_000d,
        MetricUnit.Hours => nativeValue * 3_600_000d,
        _ => throw new InvalidOperationException($"Unsupported duration base unit: {baseUnit}.")
    };

    private static double ConvertDuration(double nativeValue, MetricUnit baseUnit, string unit)
    {
        if (unit.Equals("auto", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("The auto duration unit is textual and cannot be used for quantitative values.");

        var milliseconds = ToMilliseconds(nativeValue, baseUnit);
        return unit switch
        {
            "milliseconds" => milliseconds,
            "seconds" => milliseconds / 1000d,
            "minutes" => milliseconds / 60_000d,
            "hours" => milliseconds / 3_600_000d,
            _ => throw new FormatException($"Unsupported duration unit: {unit}.")
        };
    }

    private static double ConvertDataSize(double nativeValue, MetricUnit baseUnit, string unit)
    {
        var bytes = baseUnit switch
        {
            MetricUnit.Bytes => nativeValue,
            MetricUnit.Kilobytes => nativeValue * 1_000d,
            MetricUnit.Megabytes => nativeValue * 1_000_000d,
            MetricUnit.Gigabytes => nativeValue * 1_000_000_000d,
            MetricUnit.Terabytes => nativeValue * 1_000_000_000_000d,
            MetricUnit.Kibibytes => nativeValue * 1_024d,
            MetricUnit.Mebibytes => nativeValue * 1_048_576d,
            MetricUnit.Gibibytes => nativeValue * 1_073_741_824d,
            MetricUnit.Tebibytes => nativeValue * 1_099_511_627_776d,
            _ => throw new InvalidOperationException($"Unsupported data-size base unit: {baseUnit}.")
        };

        return unit switch
        {
            "B" => bytes,
            "KB" => bytes / 1_000d,
            "MB" => bytes / 1_000_000d,
            "GB" => bytes / 1_000_000_000d,
            "TB" => bytes / 1_000_000_000_000d,
            "KiB" => bytes / 1_024d,
            "MiB" => bytes / 1_048_576d,
            "GiB" => bytes / 1_073_741_824d,
            "TiB" => bytes / 1_099_511_627_776d,
            _ => throw new FormatException($"Unsupported byte unit: {unit}.")
        };
    }

    private static double ConvertNumber(double nativeValue, MetricUnit baseUnit, string unit) => baseUnit switch
    {
        MetricUnit.Celsius or MetricUnit.Fahrenheit => ConvertTemperature(nativeValue, baseUnit, unit),
        MetricUnit.Hertz or MetricUnit.Kilohertz or MetricUnit.Megahertz or MetricUnit.Gigahertz =>
            ConvertFrequency(nativeValue, baseUnit, unit),
        MetricUnit.BytesPerSecond or MetricUnit.KilobytesPerSecond or MetricUnit.MegabytesPerSecond or
            MetricUnit.GigabytesPerSecond or MetricUnit.TerabytesPerSecond or MetricUnit.KibibytesPerSecond or
            MetricUnit.MebibytesPerSecond or MetricUnit.GibibytesPerSecond or MetricUnit.TebibytesPerSecond or
            MetricUnit.BitsPerSecond or MetricUnit.KilobitsPerSecond or MetricUnit.MegabitsPerSecond or
            MetricUnit.GigabitsPerSecond or MetricUnit.TerabitsPerSecond =>
            ConvertDataRate(nativeValue, baseUnit, unit),
        _ => throw new InvalidOperationException($"Metric base unit {baseUnit} does not support unit conversion.")
    };

    private static double ConvertTemperature(double value, MetricUnit baseUnit, string unit)
    {
        var celsius = baseUnit == MetricUnit.Celsius ? value : (value - 32d) * 5d / 9d;
        return unit.ToUpperInvariant() switch
        {
            "C" => celsius,
            "F" => celsius * 9d / 5d + 32d,
            _ => throw new FormatException($"Unsupported temperature unit: {unit}.")
        };
    }

    private static double ConvertFrequency(double value, MetricUnit baseUnit, string unit)
    {
        var hertz = value * (baseUnit switch
        {
            MetricUnit.Hertz => 1d,
            MetricUnit.Kilohertz => 1_000d,
            MetricUnit.Megahertz => 1_000_000d,
            MetricUnit.Gigahertz => 1_000_000_000d,
            _ => throw new InvalidOperationException($"Unsupported frequency base unit: {baseUnit}.")
        });

        return unit switch
        {
            "Hz" => hertz,
            "kHz" => hertz / 1_000d,
            "MHz" => hertz / 1_000_000d,
            "GHz" => hertz / 1_000_000_000d,
            _ => throw new FormatException($"Unsupported frequency unit: {unit}.")
        };
    }

    private static double ConvertDataRate(double nativeValue, MetricUnit baseUnit, string unit)
    {
        // LHM Throughput sensors are natively B/s. Connection Speed is the known
        // Throughput exception and is tagged as bit/s by the provider contract.
        var bytesPerSecond = baseUnit switch
        {
            MetricUnit.BytesPerSecond => nativeValue,
            MetricUnit.KilobytesPerSecond => nativeValue * 1_000d,
            MetricUnit.MegabytesPerSecond => nativeValue * 1_000_000d,
            MetricUnit.GigabytesPerSecond => nativeValue * 1_000_000_000d,
            MetricUnit.TerabytesPerSecond => nativeValue * 1_000_000_000_000d,
            MetricUnit.KibibytesPerSecond => nativeValue * 1_024d,
            MetricUnit.MebibytesPerSecond => nativeValue * 1_048_576d,
            MetricUnit.GibibytesPerSecond => nativeValue * 1_073_741_824d,
            MetricUnit.TebibytesPerSecond => nativeValue * 1_099_511_627_776d,
            MetricUnit.BitsPerSecond => nativeValue / 8d,
            MetricUnit.KilobitsPerSecond => nativeValue * 1_000d / 8d,
            MetricUnit.MegabitsPerSecond => nativeValue * 1_000_000d / 8d,
            MetricUnit.GigabitsPerSecond => nativeValue * 1_000_000_000d / 8d,
            MetricUnit.TerabitsPerSecond => nativeValue * 1_000_000_000_000d / 8d,
            _ => throw new InvalidOperationException($"Unsupported data-rate base unit: {baseUnit}.")
        };

        return unit switch
        {
            "B/s" => bytesPerSecond,
            "KB/s" => bytesPerSecond / 1_000d,
            "MB/s" => bytesPerSecond / 1_000_000d,
            "GB/s" => bytesPerSecond / 1_000_000_000d,
            "TB/s" => bytesPerSecond / 1_000_000_000_000d,
            "KiB/s" => bytesPerSecond / 1_024d,
            "MiB/s" => bytesPerSecond / 1_048_576d,
            "GiB/s" => bytesPerSecond / 1_073_741_824d,
            "TiB/s" => bytesPerSecond / 1_099_511_627_776d,
            "bit/s" => bytesPerSecond * 8d,
            "kbit/s" => bytesPerSecond * 8d / 1_000d,
            "Mbit/s" => bytesPerSecond * 8d / 1_000_000d,
            "Gbit/s" => bytesPerSecond * 8d / 1_000_000_000d,
            "Tbit/s" => bytesPerSecond * 8d / 1_000_000_000_000d,
            _ => throw new FormatException($"Unsupported data-rate unit: {unit}.")
        };
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Equals("raw", StringComparison.OrdinalIgnoreCase)
            ? null
            : value.Trim();
}
