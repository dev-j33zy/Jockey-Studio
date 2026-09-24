using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace JockeyStudio.Wpf.Controls;

/// <summary>1:1 of ui/src/components/UpdateBubble.tsx: a non-intrusive
/// notification that an update is available, shown after a silent boot check
/// until clicked (open the dialog) or dismissed (remembered).</summary>
public partial class UpdateBubbleControl : UserControl
{
    public event RoutedEventHandler? Clicked;
    public event RoutedEventHandler? Dismissed;

    public UpdateBubbleControl()
    {
        InitializeComponent();
        // No PreviewMouseDown-marking here: ButtonBase raises Click on its own
        // bubbling MouseLeftButtonDown, which WPF already marks handled so it
        // never reaches the body's Clicked handler. Clamping it in the preview
        // phase instead would swallow the press and the X could never dismiss.
        MouseLeftButtonDown += Bubble_MouseLeftButtonDown;
    }

    public void ShowLatestVersion(string version)
        => BubbleVersionText.Text = "v" + version;

    private void Bubble_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // The dismiss X is a child of the clickable body, so a press on it would
        // otherwise bubble up here and OPEN the dialog at the same time we're
        // dismissing it (the React dismiss button stops this with
        // e.stopPropagation()). Ignore anything originating under the button.
        if (e.OriginalSource is DependencyObject o
            && DismissButton.IsAncestorOf(o)) return;
        Clicked?.Invoke(this, e);
    }

    private void DismissButton_Click(object sender, RoutedEventArgs e)
        => Dismissed?.Invoke(this, e);
}