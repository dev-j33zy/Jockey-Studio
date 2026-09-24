using System.Windows;
using System.Windows.Controls;

namespace JockeyStudio.Wpf.Controls;

/// <summary>1:1 of ui/src/components/TopBar.tsx. Layout-only: buttons raise
/// events; the host window wires drawer/window visibility.</summary>
public partial class TopBarControl : UserControl
{
    public static readonly DependencyProperty IsLibraryOpenProperty = DependencyProperty.Register(
        nameof(IsLibraryOpen),
        typeof(bool),
        typeof(TopBarControl),
        new PropertyMetadata(false));

    public bool IsLibraryOpen
    {
        get => (bool)GetValue(IsLibraryOpenProperty);
        set => SetValue(IsLibraryOpenProperty, value);
    }

    /// <summary>Raised by the "New Deck" (plus) button; the host window adds a deck.</summary>
    public event RoutedEventHandler? AddDeckClicked;
    public event RoutedEventHandler? LibraryToggled;

    public TopBarControl()
    {
        InitializeComponent();
    }

    private void AddDeckButton_Click(object sender, RoutedEventArgs e)
        => AddDeckClicked?.Invoke(this, e);

    private void LibraryButton_Click(object sender, RoutedEventArgs e)
        => LibraryToggled?.Invoke(this, e);
}