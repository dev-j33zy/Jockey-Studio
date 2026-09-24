using System.Globalization;
using System.Windows.Data;

namespace JockeyStudio.Wpf.Helpers;

/// <summary>1:1 of util.formatTime(secs) — "m:ss".</summary>
public sealed class FormatTimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double secs = value is double d ? d : 0.0;
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
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double secs = value is double d ? d : 0.0;
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
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => $"{(int)Math.Round(value is double d ? d * 100.0 : 0.0)}";

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