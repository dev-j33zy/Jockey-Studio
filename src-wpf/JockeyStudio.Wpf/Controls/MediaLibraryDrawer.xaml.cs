using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JockeyStudio.Wpf.Engine;

namespace JockeyStudio.Wpf.Controls;

/// <summary>1:1 of ui/src/components/MediaLibrary.tsx, populated from the
/// engine's imported media; "Load" places a track into the next empty deck.</summary>
public partial class MediaLibraryDrawer : UserControl
{
    public event RoutedEventHandler? CloseClicked;

    public event Action<string>? LoadRequested;

    public MediaLibraryDrawer()
    {
        InitializeComponent();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => CloseClicked?.Invoke(this, e);

    public void SetMedia(IReadOnlyList<MediaItem> media)
    {
        LibraryList.Children.Clear();
        bool empty = media.Count == 0;
        LibraryEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (empty) return;

        foreach (var item in media) LibraryList.Children.Add(BuildRow(item));
    }

    private Border BuildRow(MediaItem item)
    {
        var row = new Border { Style = (Style)FindResource("MediaRow") };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var meta = new StackPanel();
        var title = new TextBlock
        {
            Text = item.Title,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        title.ToolTip = item.Title;
        meta.Children.Add(title);

        var sub = new TextBlock
        {
            FontSize = 11,
            Foreground = (Brush)FindResource("TextDimBrush"),
            Text = BuildSub(item),
        };
        meta.Children.Add(sub);
        Grid.SetColumn(meta, 0);
        grid.Children.Add(meta);

        var load = new Button
        {
            Style = (Style)FindResource("TbSmallButton"),
            Content = "Load",
            Margin = new Thickness(10, 0, 0, 0),
            Tag = item.Id,
        };
        load.Click += Load_Click;
        Grid.SetColumn(load, 1);
        grid.Children.Add(load);

        row.Child = grid;
        return row;
    }

    private void Load_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string id) LoadRequested?.Invoke(id);
    }

    private static string BuildSub(MediaItem m)
    {
        string kind = m.Kind == MediaKind.Video ? "Video" : "Audio";
        string container = string.IsNullOrEmpty(m.Container)
            ? ""
            : $" · {m.Container.TrimStart('.').ToUpperInvariant()}";
        string sub = $"{kind}{container} · {m.Channels}ch · {Math.Round(m.SampleRate / 1000.0)}kHz · {FormatDuration(m.DurationSecs)}";
        if (m.PeakDb != null)
        {
            sub += $" · Peak {m.PeakDb.Value.ToString("0.0", CultureInfo.InvariantCulture)} dB";
        }
        return sub;
    }

    private static string FormatDuration(double secs)
    {
        if (secs <= 0 || double.IsNaN(secs)) return "--:--";
        long total = (long)Math.Round(secs);
        return $"{total / 60}:{total % 60:00}";
    }
}