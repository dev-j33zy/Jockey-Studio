using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using JockeyStudio.Wpf;
using JockeyStudio.Wpf.Engine;
using JockeyStudio.Wpf.Helpers;

namespace JockeyStudio.Wpf.Controls;

/// <summary>Mirrors TileState.status.</summary>
public enum TileStatus
{
    Stopped,
    Loading,
    Playing,
    Paused,
    Ended,
    Error,
}

/// <summary>1:1 of ui/src/components/TileCard.tsx, bound to a live
/// <see cref="Deck"/> from <see cref="PlayerEngine"/>.</summary>
public partial class TileCardControl : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(TileCardControl), new PropertyMetadata(""));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty HasMediaProperty = DependencyProperty.Register(
        nameof(HasMedia), typeof(bool), typeof(TileCardControl), new PropertyMetadata(false));

    public bool HasMedia
    {
        get => (bool)GetValue(HasMediaProperty);
        set => SetValue(HasMediaProperty, value);
    }

    public static readonly DependencyProperty ErrorProperty = DependencyProperty.Register(
        nameof(Error), typeof(string), typeof(TileCardControl), new PropertyMetadata(""));

    public string Error
    {
        get => (string)GetValue(ErrorProperty);
        set => SetValue(ErrorProperty, value);
    }

    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(
        nameof(Status), typeof(TileStatus), typeof(TileCardControl),
        new PropertyMetadata(TileStatus.Stopped, OnStatusChanged));

    public TileStatus Status
    {
        get => (TileStatus)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public static readonly DependencyProperty MediaTypeProperty = DependencyProperty.Register(
        nameof(MediaType), typeof(string), typeof(TileCardControl), new PropertyMetadata(""));

    public string MediaType
    {
        get => (string)GetValue(MediaTypeProperty);
        set => SetValue(MediaTypeProperty, value);
    }

    public static readonly DependencyProperty PositionSecsProperty = DependencyProperty.Register(
        nameof(PositionSecs), typeof(double), typeof(TileCardControl),
        new PropertyMetadata(0.0, OnPositionChanged));

    public double PositionSecs
    {
        get => (double)GetValue(PositionSecsProperty);
        set => SetValue(PositionSecsProperty, value);
    }

    public static readonly DependencyProperty DurationSecsProperty = DependencyProperty.Register(
        nameof(DurationSecs), typeof(double), typeof(TileCardControl), new PropertyMetadata(0.0));

    public double DurationSecs
    {
        get => (double)GetValue(DurationSecsProperty);
        set => SetValue(DurationSecsProperty, value);
    }

    public static readonly DependencyProperty VolumeProperty = DependencyProperty.Register(
        nameof(Volume), typeof(double), typeof(TileCardControl), new PropertyMetadata(0.9));

    public double Volume
    {
        get => (double)GetValue(VolumeProperty);
        set => SetValue(VolumeProperty, value);
    }

    /// <summary>The audible volume shown on the slider: the base volume, or the
    /// ducked level while automix is ducking the deck (0 when muted).</summary>
    public static readonly DependencyProperty EffectiveVolumeProperty = DependencyProperty.Register(
        nameof(EffectiveVolume), typeof(double), typeof(TileCardControl), new PropertyMetadata(0.9));

    public double EffectiveVolume
    {
        get => (double)GetValue(EffectiveVolumeProperty);
        set => SetValue(EffectiveVolumeProperty, value);
    }

    /// <summary>True while automix is ducking this deck — drives the "ducked"
    /// hint under the volume slider.</summary>
    public static readonly DependencyProperty AutoMixDuckedProperty = DependencyProperty.Register(
        nameof(AutoMixDucked), typeof(bool), typeof(TileCardControl), new PropertyMetadata(false));

    public bool AutoMixDucked
    {
        get => (bool)GetValue(AutoMixDuckedProperty);
        set => SetValue(AutoMixDuckedProperty, value);
    }

    /// <summary>True while this deck was ducked by automix and auto-paused
    /// (fade-pause); it resumes when the newer deck that ducked it is paused or
    /// stopped. Drives the "auto-paused" hint under the volume slider.</summary>
    public static readonly DependencyProperty AutoMixPausedProperty = DependencyProperty.Register(
        nameof(AutoMixPaused), typeof(bool), typeof(TileCardControl), new PropertyMetadata(false));

    public bool AutoMixPaused
    {
        get => (bool)GetValue(AutoMixPausedProperty);
        set => SetValue(AutoMixPausedProperty, value);
    }

    public static readonly DependencyProperty IsMutedProperty = DependencyProperty.Register(
        nameof(IsMuted), typeof(bool), typeof(TileCardControl),
        new PropertyMetadata(false, OnIsMutedChanged));

    public bool IsMuted
    {
        get => (bool)GetValue(IsMutedProperty);
        set => SetValue(IsMutedProperty, value);
    }

    public static readonly DependencyProperty LoopModeProperty = DependencyProperty.Register(
        nameof(LoopMode), typeof(string), typeof(TileCardControl),
        new PropertyMetadata("off", OnLoopModeChanged));

    public string LoopMode
    {
        get => (string)GetValue(LoopModeProperty);
        set => SetValue(LoopModeProperty, value);
    }

    public static readonly DependencyProperty AutoMixProperty = DependencyProperty.Register(
        nameof(AutoMix), typeof(bool), typeof(TileCardControl), new PropertyMetadata(false));

    public bool AutoMix
    {
        get => (bool)GetValue(AutoMixProperty);
        set => SetValue(AutoMixProperty, value);
    }

    /// <summary>True while this card is the window's focused deck (the target
    /// for the Edit menu's Cut/Copy/Paste) — drives the accent ring.</summary>
    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(
        nameof(IsSelected), typeof(bool), typeof(TileCardControl), new PropertyMetadata(false));

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public static readonly DependencyProperty DeviceLabelProperty = DependencyProperty.Register(
        nameof(DeviceLabel), typeof(string), typeof(TileCardControl),
        new PropertyMetadata("System Default"));

    public string DeviceLabel
    {
        get => (string)GetValue(DeviceLabelProperty);
        set => SetValue(DeviceLabelProperty, value);
    }

    /// <summary>Raised when the user clicks the remove (x) deck button.</summary>
    public event RoutedEventHandler? RemoveClicked;

    /// <summary>Raised just before the devices popup opens, so the host can
    /// re-enumerate the endpoints and push a fresh snapshot.</summary>
    public event Action? DeviceListNeedsRefresh;

    private string? _openPopup;
    private Deck? _deck;
    private IReadOnlyList<OutputDevice> _devices = Array.Empty<OutputDevice>();
    private string _defaultDeviceLabel = "System Default";
    private bool _updating;
    private bool _seeking;
    private DateTime _lastSeek = DateTime.MinValue;

    private const int SeekThrottleMs = 90;

    /// <summary>Raised when the user presses the left button on the card
    /// header to start a tile-reorder drag. The window owns the drag session
    /// (ghost, drop slot, commit).</summary>
    public static readonly RoutedEvent ReorderDragRequestedEvent = EventManager.RegisterRoutedEvent(
        nameof(ReorderDragRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(TileCardControl));

    public event RoutedEventHandler ReorderDragRequested
    {
        add => AddHandler(ReorderDragRequestedEvent, value);
        remove => RemoveHandler(ReorderDragRequestedEvent, value);
    }

    /// <summary>Raised when Edit → Copy / the deck's Ctrl+C / its context menu
    /// asks to copy this deck's media (the window holds the clipboard).</summary>
    public static readonly RoutedEvent CopyRequestedEvent = EventManager.RegisterRoutedEvent(
        nameof(CopyRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(TileCardControl));

    public event RoutedEventHandler CopyRequested
    {
        add => AddHandler(CopyRequestedEvent, value);
        remove => RemoveHandler(CopyRequestedEvent, value);
    }

    /// <summary>Ask to cut this deck's media (copy + unload from this deck).</summary>
    public static readonly RoutedEvent CutRequestedEvent = EventManager.RegisterRoutedEvent(
        nameof(CutRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(TileCardControl));

    public event RoutedEventHandler CutRequested
    {
        add => AddHandler(CutRequestedEvent, value);
        remove => RemoveHandler(CutRequestedEvent, value);
    }

    /// <summary>Ask to paste the window's clipboard media into this deck.</summary>
    public static readonly RoutedEvent PasteRequestedEvent = EventManager.RegisterRoutedEvent(
        nameof(PasteRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(TileCardControl));

    public event RoutedEventHandler PasteRequested
    {
        add => AddHandler(PasteRequestedEvent, value);
        remove => RemoveHandler(PasteRequestedEvent, value);
    }

    public TileCardControl()
    {
        InitializeComponent();
        UpdateStatusUi();
        UpdateMuteUi();
        ApplyLoopMode();
        Seek.PreviewMouseMove += Seek_PreviewMouseMove;
        Seek.MouseLeave += Seek_MouseLeave;
        Seek.ValueChanged += Seek_ValueChanged;
        Seek.PreviewMouseLeftButtonDown += Seek_PreviewMouseLeftButtonDown;
        Seek.PreviewMouseLeftButtonUp += Seek_PreviewMouseLeftButtonUp;
        VolumeSlider.ValueChanged += VolumeSlider_ValueChanged;
        VolumeSlider.PreviewMouseLeftButtonDown += VolumeSlider_PreviewMouseLeftButtonDown;
        PopupVeil.MouseDown += PopupVeil_MouseDown;
        VolumePopup.Opened += Popup_Opened;
        DevicePopup.Opened += Popup_Opened;
        LoopPopup.Opened += Popup_Opened;
    }

    // ---------------- tile reorder drag ----------------

    private void HeaderDock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Don't start a reorder drag from inside a button (e.g. remove deck).
        if (e.OriginalSource is DependencyObject source && IsInsideButton(source)) return;
        RaiseEvent(new RoutedEventArgs(ReorderDragRequestedEvent, this));
        e.Handled = true;
    }

    private static bool IsInsideButton(DependencyObject? node)
    {
        for (; node != null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is ButtonBase) return true;
        }
        return false;
    }

    /// <summary>Switch the card into "drag ghost" presentation while the window
    /// drags it: 90% overall opacity plus the body and footer dimmed to 35%
    /// (1:1 of `.tile.dragging` in ui/src/styles/app.css).</summary>
    public void SetGhostVisual(bool ghost)
    {
        Opacity = ghost ? 0.9 : 1.0;
        CardBody.Opacity = ghost ? 0.35 : 1.0;
        CardFooter.Opacity = ghost ? 0.35 : 1.0;
    }

    // ---------------- backend binding ----------------

    public Deck? DeckValue => _deck;

    public void Bind(Deck deck) => _deck = deck;

    public void SetDeviceList(IReadOnlyList<OutputDevice> devices, string? defaultDeviceLabel = null)
    {
        _devices = devices;
        if (defaultDeviceLabel != null) _defaultDeviceLabel = defaultDeviceLabel;
    }

    /// <summary>Push the engine's deck state into the dependency properties the
    /// XAML binds to. Guarded so slider bindings don't write back to the engine.
    /// Also refreshes the play glyph / enabled states, since a status that stays
    /// the same value (e.g. Stopped before first play) never fires its callback.</summary>
    public void UpdateFromDeck()
    {
        if (_deck == null) return;
        _updating = true;
        try
        {
            Title = _deck.Title;
            HasMedia = _deck.HasMedia;
            Status = MapStatus(_deck.Status);
            Error = _deck.Error ?? "";
            MediaType = _deck.MediaType;
            if (!_seeking) PositionSecs = _deck.PositionSecs;
            DurationSecs = _deck.DurationSecs;
            Volume = _deck.Volume;
            EffectiveVolume = _deck.EffectiveVolume;
            AutoMixDucked = _deck.AutoMixDucked;
            AutoMixPaused = _deck.AutoMixHeldPaused;
            IsMuted = _deck.Muted;
            LoopMode = _deck.LoopMode;
            AutoMix = _deck.Fades.AutoMix;
            DeviceLabel = DefaultDeviceName();
            DevBtn.ToolTip = _deck.DeviceId == EngineConst.DEFAULT_DEVICE_ID
                ? $"Output: Default ({_defaultDeviceLabel})"
                : $"Output: {_deck.DeviceId}";

            UpdateStatusUi();
            UpdateMuteUi();
            ApplyLoopMode();
        }
        finally
        {
            _updating = false;
        }
    }

    private static TileStatus MapStatus(PlaybackStatus status) => status switch
    {
        PlaybackStatus.Playing => TileStatus.Playing,
        PlaybackStatus.Paused => TileStatus.Paused,
        PlaybackStatus.Ended => TileStatus.Ended,
        PlaybackStatus.Error => TileStatus.Error,
        _ => TileStatus.Stopped,
    };

    /// <summary>The label shown for the "Default device" popup row and used as
    /// the tile's device text: the effective app-level default (1:1 of the React
    /// defaultLabel), i.e. "System Default" when following the OS default, else
    /// the configured device's name.</summary>
    private string DefaultDeviceName() => _defaultDeviceLabel;

    private void SyncImmediate()
    {
        UpdateFromDeck();
        if (_openPopup == "dev") RebuildDeviceList();
    }

    // ---------------- state updates ----------------

    private static void OnStatusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((TileCardControl)d).UpdateStatusUi();

    private static void OnIsMutedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((TileCardControl)d).UpdateMuteUi();

    private static void OnPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (TileCardControl)d;
        var max = c.DurationSecs > 0 ? c.DurationSecs : 1.0;
        if (c.Seek.Maximum != max) c.Seek.Maximum = max;
    }

    private void UpdateStatusUi()
    {
        bool playing = Status == TileStatus.Playing;
        PlayIconShow.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
        PauseIconShow.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        Ctl.SetIsEngaged(PlayBtn, playing);
        PlayBtn.IsEnabled = HasMedia;
        Seek.IsEnabled = HasMedia;
        ErrorText.Visibility = Status == TileStatus.Error && !string.IsNullOrEmpty(Error)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateMuteUi()
    {
        MuteIconNormal.Visibility = IsMuted ? Visibility.Collapsed : Visibility.Visible;
        MuteIconMuted.Visibility = IsMuted ? Visibility.Visible : Visibility.Collapsed;
        Ctl.SetIsEngaged(MuteBtn, IsMuted);
    }

    private void ApplyLoopMode()
    {
        Ctl.SetIsEngaged(LoopBtn, LoopMode != "off");
        Ctl.SetIsActive(LoopOffItem, LoopMode == "off");
        Ctl.SetIsActive(LoopEndlessItem, LoopMode == "endless");
        Ctl.SetIsActive(Loop2Item, LoopMode == "x2");
        Ctl.SetIsActive(Loop3Item, LoopMode == "x3");
        Ctl.SetIsActive(Loop4Item, LoopMode == "x4");
        Ctl.SetIsActive(Loop5Item, LoopMode == "x5");
    }

    private static void OnLoopModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((TileCardControl)d).ApplyLoopMode();

    // ---------------- popups ----------------

    private void ClosePopups()
    {
        VolumePopup.IsOpen = false;
        DevicePopup.IsOpen = false;
        LoopPopup.IsOpen = false;
        PopupVeil.Visibility = Visibility.Collapsed;
        _openPopup = null;
        Ctl.SetIsPopupOpen(VolBtn, false);
        Ctl.SetIsPopupOpen(DevBtn, false);
        Ctl.SetIsPopupOpen(LoopBtn, false);
    }

    public void CloseAllPopups() => ClosePopups();

    private void TogglePopup(string kind)
    {
        if (_openPopup == kind)
        {
            ClosePopups();
            return;
        }
        CloseAllPopupsEverywhere();
        _openPopup = kind;
        PopupVeil.Visibility = Visibility.Visible;
        switch (kind)
        {
            case "vol":
                VolumePopup.IsOpen = true;
                Ctl.SetIsPopupOpen(VolBtn, true);
                break;
            case "dev":
                DeviceListNeedsRefresh?.Invoke();
                RebuildDeviceList();
                DevicePopup.IsOpen = true;
                Ctl.SetIsPopupOpen(DevBtn, true);
                break;
            case "loop":
                LoopPopup.IsOpen = true;
                Ctl.SetIsPopupOpen(LoopBtn, true);
                break;
        }
    }

    // Clicking any modal's toggle button closes every open modal across all
    // tiles before the clicked one opens, so at most one modal is ever visible.
    private void CloseAllPopupsEverywhere()
    {
        if (Window.GetWindow(this) is MainWindow window) window.CloseAllTilePopups();
        else ClosePopups();
    }

    private void PopupVeil_MouseDown(object sender, MouseButtonEventArgs e)
        => ClosePopups();

    // Keep popups inside the app window's client area so an unfocused window
    // never lets them spill over other apps.
    private void Popup_Opened(object? sender, EventArgs e)
    {
        var popup = (Popup)sender!;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            ClampPopup(popup);
            MakePopupClickTransparentToActivation(popup);
        }));
    }

    // The popup is a separate top-level window. Without WS_EX_NOACTIVATE,
    // clicking inside it activates the popup and deactivates this window, which
    // triggers the MainWindow's Deactivated handler and closes the popup while
    // the user is still using it. With the flag set, clicks still work but the
    // popup never steals activation, so it only closes when the app truly
    // loses focus to another application.
    private static void MakePopupClickTransparentToActivation(Popup popup)
    {
        if (popup.Child is not Visual visual) return;
        if (PresentationSource.FromVisual(visual) is not HwndSource src || src.Handle == IntPtr.Zero) return;
        const int gwlExStyle = -20;
        const int wsExNoActivate = 0x08000000;
        int ex = GetWindowLong(src.Handle, gwlExStyle);
        if ((ex & wsExNoActivate) == 0) SetWindowLong(src.Handle, gwlExStyle, ex | wsExNoActivate);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    // The popup window's transparent pixels (e.g. the margins around the thin
    // volume fader) let mouse-downs pass through to the main window and reach
    // Window_PreviewMouseDown. Before the main window closes the popups, it asks
    // whether the pointer is over an open popup and treats the click as intended
    // for that popup instead of as a "close on empty area" click.
    public bool IsPointOverOpenPopup(Point screenPoint)
    {
        foreach (var popup in new[] { VolumePopup, DevicePopup, LoopPopup })
        {
            if (!popup.IsOpen || popup.Child is not Visual v) continue;
            if (PresentationSource.FromVisual(v) is not HwndSource src || src.Handle == IntPtr.Zero) continue;
            RECT r;
            if (!GetWindowRect(src.Handle, out r)) continue;
            if (screenPoint.X >= r.L && screenPoint.X <= r.R
                && screenPoint.Y >= r.T && screenPoint.Y <= r.B) return true;
        }
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int L, T, R, B; }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    private void ClampPopup(Popup popup)
    {
        if (popup.Child is not FrameworkElement child
            || child.ActualWidth <= 0 || child.ActualHeight <= 0) return;
        var win = Window.GetWindow(this);
        if (win?.Content is not FrameworkElement content
            || content.ActualWidth <= 0 || content.ActualHeight <= 0) return;

        var tl = child.PointToScreen(new Point(0, 0));
        var client = content.PointToScreen(new Point(0, 0));
        const double margin = 8;
        double minL = client.X + margin, minT = client.Y + margin;
        double maxL = client.X + content.ActualWidth - child.ActualWidth - margin;
        double maxT = client.Y + content.ActualHeight - child.ActualHeight - margin;

        double dx = 0, dy = 0;
        if (tl.X < minL) dx = minL - tl.X;
        else if (tl.X > maxL && maxL > minL) dx = maxL - tl.X;
        if (tl.Y < minT) dy = minT - tl.Y;
        else if (tl.Y > maxT && maxT > minT) dy = maxT - tl.Y;

        if (dx != 0 || dy != 0)
        {
            popup.HorizontalOffset += dx;
            popup.VerticalOffset += dy;
        }
    }

    // ---------------- click wiring (engine-backed) ----------------

    private void PlayBtn_Click(object sender, RoutedEventArgs e)
    {
        _deck?.TogglePlay();
        SyncImmediate();
    }

    private void VolBtn_Click(object sender, RoutedEventArgs e) => TogglePopup("vol");
    private void DevBtn_Click(object sender, RoutedEventArgs e) => TogglePopup("dev");
    private void LoopBtn_Click(object sender, RoutedEventArgs e) => TogglePopup("loop");

    private void MuteBtn_Click(object sender, RoutedEventArgs e)
    {
        _deck?.SetMuted(!IsMuted);
        SyncImmediate();
    }

    private void AutoMixBtn_Click(object sender, RoutedEventArgs e)
    {
        _deck?.ToggleAutoMix();
        SyncImmediate();
    }

    private void LoopItem_Click(object sender, RoutedEventArgs e)
    {
        if (_deck != null && sender is Button b && b.Tag is string mode) _deck.SetLoop(mode);
        ClosedSync();
    }

    private void DevItem_Click(object sender, RoutedEventArgs e)
    {
        if (_deck != null && sender is Button b && b.Tag is string id) _deck.SetDevice(id);
        ClosedSync();
    }

    private void ClosedSync()
    {
        ClosePopups();
        UpdateFromDeck();
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
        => RemoveClicked?.Invoke(this, e);

    // ---------------- clipboard commands (Edit menu, context menu) ----------------

    private void OnCopyCommand(object sender, ExecutedRoutedEventArgs e)
        => RaiseEvent(new RoutedEventArgs(CopyRequestedEvent, this));

    private void OnCutCommand(object sender, ExecutedRoutedEventArgs e)
        => RaiseEvent(new RoutedEventArgs(CutRequestedEvent, this));

    private void OnPasteCommand(object sender, ExecutedRoutedEventArgs e)
        => RaiseEvent(new RoutedEventArgs(PasteRequestedEvent, this));

    private void OnCopyCanExecute(object sender, CanExecuteRoutedEventArgs e)
        => e.CanExecute = _deck is { HasMedia: true };

    private void OnCutCanExecute(object sender, CanExecuteRoutedEventArgs e)
        => e.CanExecute = _deck is { HasMedia: true };

    private void OnPasteCanExecute(object sender, CanExecuteRoutedEventArgs e)
        => e.CanExecute = true;

    // ---------------- volume / seek ----------------

    private void VolumeSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Clicking the track (not the thumb) sets the volume at the pointer
        // position, like the React range input. Without this, a single track
        // click fires Slider.Increase/DecreaseLarge which jumps straight to
        // the min or max (a 0/1 "boolean" fader).
        if (HitTestIsThumb(e.OriginalSource)) return;
        double h = VolumeSlider.ActualHeight;
        if (h <= 0) return;
        double y = e.GetPosition(VolumeSlider).Y;
        VolumeSlider.Value = 1.0 - Math.Clamp(y / h, 0.0, 1.0);
        e.Handled = true;
    }

    private static bool HitTestIsThumb(object? source)
    {
        for (var node = source as DependencyObject; node != null;
             node = VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node))
        {
            if (node is Thumb) return true;
        }
        return false;
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating || _deck == null) return;
        _deck.SetVolumeEffective((float)e.NewValue);
    }

    private void Seek_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating || _deck == null || !_seeking) return;
        var now = DateTime.UtcNow;
        if ((now - _lastSeek).TotalMilliseconds < SeekThrottleMs) return;
        _lastSeek = now;
        _deck.Seek(e.NewValue);
    }

    private void Seek_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _seeking = true;
        _lastSeek = DateTime.MinValue;
    }

    private void Seek_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _seeking = false;
        _deck?.Seek(Seek.Value);
    }

    // ---------------- device popup list ----------------

    private void RebuildDeviceList()
    {
        DevListPanel.Children.Clear();
        string active = _deck?.DeviceId ?? EngineConst.DEFAULT_DEVICE_ID;

        DevListPanel.Children.Add(BuildDeviceRow(
            "Default device", DefaultDeviceName(), EngineConst.DEFAULT_DEVICE_ID,
            active == EngineConst.DEFAULT_DEVICE_ID));

        foreach (var dev in _devices)
        {
            if (dev.IsDefault) continue;
            DevListPanel.Children.Add(BuildDeviceRow(dev.Name, null, dev.Id, active == dev.Id));
        }
    }

    private Button BuildDeviceRow(string title, string? sub, string id, bool active)
    {
        var button = new Button
        {
            Style = (Style)FindResource("TbDevItem"),
            Tag = id,
            Margin = new Thickness(0, 1, 0, 1),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        button.Click += DevItem_Click;
        Ctl.SetIsActive(button, active);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new TextBlock
        {
            Text = title,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(name, 0);
        grid.Children.Add(name);

        if (!string.IsNullOrEmpty(sub))
        {
            var subText = new TextBlock
            {
                Text = sub,
                FontSize = 11,
                Margin = new Thickness(10, 0, 6, 0),
                Foreground = (Brush)FindResource("TextDimBrush"),
            };
            Grid.SetColumn(subText, 1);
            grid.Children.Add(subText);
        }

        var check = new ContentControl
        {
            Content = "",
            ContentTemplate = (DataTemplate)FindResource("IconCheck"),
            Foreground = (Brush)FindResource("AccentBrush"),
            Width = 14,
            Height = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = active ? Visibility.Visible : Visibility.Collapsed,
        };
        Grid.SetColumn(check, 2);
        grid.Children.Add(check);

        button.Content = grid;
        return button;
    }

    // ---------------- seek hover tip ----------------

    private void Seek_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!HasMedia) return;
        double w = Seek.ActualWidth;
        if (w <= 0) return;
        var max = DurationSecs > 0 ? DurationSecs : 1.0;
        double x = e.GetPosition(Seek).X;
        double ratio = Math.Clamp(x / w, 0.0, 1.0);
        SeekTipText.Text = FormatTime(ratio * max);
        SeekTip.Margin = new Thickness(Math.Clamp(x - 28, 0, w - 56) + 16, 9, 0, 0);
        SeekTip.Visibility = Visibility.Visible;
    }

    private void Seek_MouseLeave(object sender, MouseEventArgs e)
        => SeekTip.Visibility = Visibility.Collapsed;

    private static string FormatTime(double secs)
    {
        long total = (long)Math.Round(secs);
        return $"{(total / 60)}:{total % 60:00}";
    }
}