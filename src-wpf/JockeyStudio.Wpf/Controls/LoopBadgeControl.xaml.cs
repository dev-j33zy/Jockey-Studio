using System.Windows;
using System.Windows.Controls;

namespace JockeyStudio.Wpf.Controls;

/// <summary>Loop-repeat icon with its numeric/infinity badge (1:1 of the
/// LoopBadgeIcon in TileCard.tsx).</summary>
public partial class LoopBadgeControl : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(string),
        typeof(LoopBadgeControl),
        new PropertyMetadata("off", OnValueChanged));

    /// <summary>One of "off", "endless", "x2".."x5".</summary>
    public string? Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public LoopBadgeControl()
    {
        InitializeComponent();
        UpdateBadge();
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((LoopBadgeControl)d).UpdateBadge();

    private void UpdateBadge()
    {
        BadgeText.Text = Value switch
        {
            "endless" => "\u221E",
            "x2" => "2",
            "x3" => "3",
            "x4" => "4",
            "x5" => "5",
            _ => string.Empty,
        };
    }
}