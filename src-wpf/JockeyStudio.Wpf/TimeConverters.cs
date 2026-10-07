using System.Globalization;
using System.Windows.Data;

namespace JockeyStudio.Wpf.Helpers;

/// <summary>1:1 of util.formatTime(secs) — "m:ss".</summary>
public sealed class FormatTimeConverter : IValueConverter
{
    private static double ToDouble(object? value, double fallback = 0.0)
    {
        if (value is null) return fallback;
        if (value is double d) return d;
        if (value is float f) return f;
        if (value is int i) return i;
        if (value is long l) return l;
        if (value is decimal dc) return (double)dc;
        if (value is IConvertible c)
        {
            try { return c.ToDouble(CultureInfo.InvariantCulture); }
            catch { }
        }
        return fallback;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double secs = ToDouble(value, 0.0);
        if (secs <= 0.0) secs = 0.0;
        long total = (long)Math.Round(secs);
        return $"{(total / 60)}:{total % 60:00}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>1:1 of util.formatDuration(secs) — "m:ss" or "--:--".</summary>
public sealed class FormatDurationConverter : IValueConverter
{
    private static double ToDouble(object? value, double fallback = 0.0)
    {
        if (value is null) return fallback;
        if (value is double d) return d;
        if (value is float f) return f;
        if (value is int i) return i;
        if (value is long l) return l;
        if (value is decimal dc) return (double)dc;
        if (value is IConvertible c)
        {
            try { return c.ToDouble(CultureInfo.InvariantCulture); }
            catch { }
        }
        return fallback;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double secs = ToDouble(value, 0.0);
        if (secs <= 0.0) return "--:--";
        long total = (long)Math.Round(secs);
        return $"{(total / 60)}:{total % 60:00}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Volume (0..1) → rounded percent text ("90").</summary>
public sealed class RoundPercentToStringConverter : IValueConverter
{
    private static double ToDouble(object? value, double fallback = 0.0)
    {
        if (value is null) return fallback;
        if (value is double d) return d;
        if (value is float f) return f;
        if (value is int i) return i;
        if (value is long l) return l;
        if (value is decimal dc) return (double)dc;
        if (value is IConvertible c)
        {
            try { return c.ToDouble(CultureInfo.InvariantCulture); }
            catch { }
        }
        return fallback;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => $"{(int)Math.Round(ToDouble(value, 0.0) * 100.0)}";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Logical NOT for IsEnabled bindings.</summary>
public sealed class NotConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}