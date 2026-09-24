using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace JockeyStudio.Wpf.Helpers;

/// <summary>Clips an accent slider fill to the progress ratio of the track,
/// producing the hard fill edge the CSS gradient-stop creates (the corner
/// rounding only ever applies at the track ends, never at the progress tip).
/// MultiBinding feeds ActualWidth, ActualHeight, Value, Maximum, Minimum;
/// ConverterParameter "h" or "v" selects orientation.</summary>
public sealed class TrackClipConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        double w = values.Length > 0 && values[0] is double dw ? dw : 0.0;
        double h = values.Length > 1 && values[1] is double dh ? dh : 0.0;
        double v = values.Length > 2 && values[2] is double dv ? dv : 0.0;
        double max = values.Length > 3 && values[3] is double dm ? dm : 1.0;
        double min = values.Length > 4 && values[4] is double dn ? dn : 0.0;
        double span = max - min;
        double ratio = span > 0 ? Math.Max(0.0, Math.Min(1.0, (v - min) / span)) : 0.0;
        if (parameter is string ps && ps == "v")
        {
            double fy = h * (1.0 - ratio);
            return new Rect(0.0, fy, w, Math.Max(0.0, h * ratio));
        }
        return new Rect(0.0, 0.0, Math.Max(0.0, w * ratio), h);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Scales the accent fill inside a slider's track proportionally to
/// Value/Maximum. Consumed as a MultiBinding on a ScaleTransform.</summary>
public sealed class RatioScaleConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        double value = 0.0;
        double max = 1.0;
        if (values.Length > 0 && values[0] is double v) value = v;
        if (values.Length > 1 && values[1] is double m) max = m;
        if (values.Length > 2 && values[2] is double min) value -= min;
        if (max <= 0.0) max = 1.0;
        double ratio = value / max;
        if (ratio < 0.0) ratio = 0.0;
        if (ratio > 1.0) ratio = 1.0;
        return ratio;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Themed button corner radius, read by shared Button templates so each
/// style can override radius (CSS .btn=8/.tool-btn=6/.dev-item=7 etc.).</summary>
public static class Btn
{
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius",
        typeof(CornerRadius),
        typeof(Btn),
        new FrameworkPropertyMetadata(new CornerRadius(8)));

    public static CornerRadius GetCornerRadius(DependencyObject obj) => (CornerRadius)obj.GetValue(CornerRadiusProperty);
    public static void SetCornerRadius(DependencyObject obj, CornerRadius value) => obj.SetValue(CornerRadiusProperty, value);
}

/// <summary>Attached boolean state drives the engaged/open/accent visuals on the
/// deck tile controls (1:1 with the .ctl-icon.active / .ctl-icon.open CSS states).</summary>
public static class Ctl
{
    public static readonly DependencyProperty IsEngagedProperty = DependencyProperty.RegisterAttached(
        "IsEngaged",
        typeof(bool),
        typeof(Ctl),
        new FrameworkPropertyMetadata(false));

    public static bool GetIsEngaged(DependencyObject obj) => (bool)obj.GetValue(IsEngagedProperty);
    public static void SetIsEngaged(DependencyObject obj, bool value) => obj.SetValue(IsEngagedProperty, value);

    public static readonly DependencyProperty IsPopupOpenProperty = DependencyProperty.RegisterAttached(
        "IsPopupOpen",
        typeof(bool),
        typeof(Ctl),
        new FrameworkPropertyMetadata(false));

    public static bool GetIsPopupOpen(DependencyObject obj) => (bool)obj.GetValue(IsPopupOpenProperty);
    public static void SetIsPopupOpen(DependencyObject obj, bool value) => obj.SetValue(IsPopupOpenProperty, value);

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.RegisterAttached(
        "IsActive",
        typeof(bool),
        typeof(Ctl),
        new FrameworkPropertyMetadata(false));

    public static bool GetIsActive(DependencyObject obj) => (bool)obj.GetValue(IsActiveProperty);
    public static void SetIsActive(DependencyObject obj, bool value) => obj.SetValue(IsActiveProperty, value);
}