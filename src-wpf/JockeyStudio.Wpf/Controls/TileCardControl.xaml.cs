using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
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
        nameof(DurationSecs), typeof(double), typeof(TileCardControl),
        new PropertyMetadata(0.0, OnDurationChanged));

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

    public static readonly DependencyProperty AutoPlayProperty = DependencyProperty.Register(
        nameof(AutoPlay), typeof(bool), typeof(TileCardControl), new PropertyMetadata(false));

    public bool AutoPlay
    {
        get => (bool)GetValue(AutoPlayProperty);
        set => SetValue(AutoPlayProperty, value);
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
    private bool _isListView;
    private FrameworkElement? _openPopupPanel;
    private FrameworkElement? _popupTarget;
    private AdornerLayer? _popupAdornerLayer;
    private DeckDropdownAdorner? _popupAdorner;

    public bool IsListView
    {
        get => _isListView;
        set
        {
            if (_isListView == value) return;
            ClosePopups();
            _isListView = value;
            CardBorder.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            RowBorder.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            MinWidth = value ? 0 : 320;
            MinHeight = value ? 78 : 320;
        }
    }

    internal sealed class DeckDropdownAdorner : Adorner
    {
        private readonly VisualCollection _visuals;
        private readonly FrameworkElement _panel;
        private readonly FrameworkElement _target;
        private readonly FrameworkElement _placementArea;
        private readonly double _horizontalOffset;
        private readonly double _verticalOffset;

        public DeckDropdownAdorner(
            UIElement adornedElement,
            FrameworkElement panel,
            FrameworkElement target,
            double horizontalOffset,
            double verticalOffset)
            : base(adornedElement)
        {
            _panel = panel;
            _target = target;
            _horizontalOffset = horizontalOffset;
            _verticalOffset = verticalOffset;
            var window = Window.GetWindow(adornedElement);
            var windowContent = window?.Content as FrameworkElement
                ?? throw new InvalidOperationException("The deck dropdown window content is unavailable.");
            _placementArea = window is MainWindow mainWindow
                ? mainWindow.ScrollHost
                : windowContent;
            _visuals = new VisualCollection(this) { panel };
            IsHitTestVisible = true;
        }

        protected override int VisualChildrenCount => _visuals.Count;

        protected override Visual GetVisualChild(int index) => _visuals[index];

        public void DetachPanel() => _visuals.Remove(_panel);

        protected override Size MeasureOverride(Size constraint)
        {
            _panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return AdornedElement.RenderSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            Size panelSize = _panel.DesiredSize;
            Rect targetBounds = _target.TransformToAncestor(AdornedElement)
                .TransformBounds(new Rect(_target.RenderSize));
            double x = targetBounds.Left + _horizontalOffset;
            const double margin = 8;
            Rect areaBounds = _placementArea.TransformToDescendant(AdornedElement)
                .TransformBounds(new Rect(_placementArea.RenderSize));
            double left = areaBounds.Left + margin;
            double right = areaBounds.Right - margin;
            double top = areaBounds.Top + margin;
            double bottom = areaBounds.Bottom - margin;
            x = right - panelSize.Width >= left
                ? Math.Clamp(x, left, right - panelSize.Width)
                : left;

            double gap = Math.Abs(_verticalOffset);
            double aboveSpace = targetBounds.Top - top - gap;
            double belowSpace = bottom - targetBounds.Bottom - gap;
            bool fitsAbove = aboveSpace >= panelSize.Height;
            bool fitsBelow = belowSpace >= panelSize.Height;
            bool placeAbove = fitsAbove || (!fitsBelow && aboveSpace > belowSpace);
            double y = placeAbove
                ? targetBounds.Top - panelSize.Height - gap
                : targetBounds.Bottom + gap;
            double maxY = bottom - panelSize.Height;
            y = maxY >= top ? Math.Clamp(y, top, maxY) : top;

            _panel.Arrange(new Rect(new Point(x, y), panelSize));
            return finalSize;
        }
    }

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
        UpdateVolumeTooltip();
        Seek.PreviewMouseMove += Seek_PreviewMouseMove;
        Seek.MouseLeave += Seek_MouseLeave;
        Seek.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(Seek_PreviewMouseLeftButtonDown), true);
        Seek.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler(Seek_PreviewMouseLeftButtonUp), true);
        RowSeek.PreviewMouseMove += Seek_PreviewMouseMove;
        RowSeek.MouseLeave += Seek_MouseLeave;
        RowSeek.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(Seek_PreviewMouseLeftButtonDown), true);
        RowSeek.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler(Seek_PreviewMouseLeftButtonUp), true);
        VolumeSlider.ValueChanged += VolumeSlider_ValueChanged;
        VolumeSlider.PreviewMouseLeftButtonDown += VolumeSlider_PreviewMouseLeftButtonDown;
        VolumeSlider.PreviewMouseWheel += VolumeSlider_PreviewMouseWheel;
        PopupVeil.MouseDown += PopupVeil_MouseDown;
    }

    // ---------------- tile reorder drag ----------------

    private void HeaderDock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Don't start a reorder drag from inside a button (e.g. remove deck).
        if (e.OriginalSource is DependencyObject source && IsInsideButton(source)) return;
        RaiseEvent(new RoutedEventArgs(ReorderDragRequestedEvent, this));
        e.Handled = true;
    }

    private void RowBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (RowPopupVeil.Visibility == Visibility.Visible) return;
        if (RowSeek.IsMouseOver) return;
        if (e.OriginalSource is DependencyObject source && IsInsideRowControl(source)) return;
        RaiseEvent(new RoutedEventArgs(ReorderDragRequestedEvent, this));
        e.Handled = true;
    }

    private static bool IsInsideRowControl(DependencyObject? node)
    {
        for (; node != null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is ButtonBase or Slider) return true;
        }
        return false;
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
            RowMediaType.Visibility = _deck.HasMedia ? Visibility.Visible : Visibility.Collapsed;
            if (!_seeking || !_deck.HasMedia) PositionSecs = _deck.PositionSecs;
            DurationSecs = _deck.DurationSecs;
            Volume = _deck.Volume;
            EffectiveVolume = _deck.EffectiveVolume;
            AutoMixDucked = _deck.AutoMixDucked;
            AutoMixPaused = _deck.AutoMixHeldPaused;
            IsMuted = _deck.Muted;
            LoopMode = _deck.LoopMode;
            AutoMix = _deck.Fades.AutoMix;
            AutoPlay = _deck.AutoPlay;
            DeviceLabel = DefaultDeviceName();
            DevBtn.ToolTip = _deck.DeviceId == EngineConst.DEFAULT_DEVICE_ID
                ? $"Output: Default ({_defaultDeviceLabel})"
                : $"Output: {_deck.DeviceId}";
            RowDevBtn.ToolTip = DevBtn.ToolTip;
            UpdateVolumeTooltip();

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
        c.UpdateSeekMaximum();
    }

    private static void OnDurationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((TileCardControl)d).UpdateSeekMaximum();

    private void UpdateSeekMaximum()
    {
        double max = DurationSecs > 0 ? DurationSecs : 1.0;
        if (Seek.Maximum != max) Seek.Maximum = max;
        if (RowSeek.Maximum != max) RowSeek.Maximum = max;
    }

    private void UpdateStatusUi()
    {
        bool playing = Status == TileStatus.Playing;
        PlayIconShow.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
        PauseIconShow.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        Ctl.SetIsEngaged(PlayBtn, playing);
        PlayBtn.IsEnabled = HasMedia;
        Seek.IsEnabled = HasMedia;
        RowPlayIconShow.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
        RowPauseIconShow.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        Ctl.SetIsEngaged(RowPlayBtn, playing);
        RowPlayBtn.IsEnabled = HasMedia;
        RowSeek.IsEnabled = HasMedia;
        ErrorText.Visibility = Status == TileStatus.Error && !string.IsNullOrEmpty(Error)
            ? Visibility.Visible : Visibility.Collapsed;
        RowErrorText.Visibility = Status == TileStatus.Error && !string.IsNullOrEmpty(Error)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateMuteUi()
    {
        MuteIconNormal.Visibility = IsMuted ? Visibility.Collapsed : Visibility.Visible;
        MuteIconMuted.Visibility = IsMuted ? Visibility.Visible : Visibility.Collapsed;
        Ctl.SetIsEngaged(MuteBtn, IsMuted);
        RowVolumeIconShow.Visibility = IsMuted ? Visibility.Collapsed : Visibility.Visible;
        RowVolumeMuteIconShow.Visibility = IsMuted ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyLoopMode()
    {
        Ctl.SetIsEngaged(LoopBtn, LoopMode != "off");
        Ctl.SetIsEngaged(RowLoopBtn, LoopMode != "off");
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
        if (_popupAdorner != null && _popupAdornerLayer != null)
        {
            _popupAdornerLayer.Remove(_popupAdorner);
            _popupAdorner.DetachPanel();
        }
        if (_openPopupPanel != null)
        {
            _openPopupPanel.Visibility = Visibility.Collapsed;
            PopupTemplates.Children.Add(_openPopupPanel);
        }
        _popupAdorner = null;
        _popupAdornerLayer = null;
        _openPopupPanel = null;
        _popupTarget = null;
        PopupVeil.Visibility = Visibility.Collapsed;
        RowPopupVeil.Visibility = Visibility.Collapsed;
        _openPopup = null;
        Ctl.SetIsPopupOpen(VolBtn, false);
        Ctl.SetIsPopupOpen(DevBtn, false);
        Ctl.SetIsPopupOpen(LoopBtn, false);
        Ctl.SetIsPopupOpen(RowVolBtn, false);
        Ctl.SetIsPopupOpen(RowDevBtn, false);
        Ctl.SetIsPopupOpen(RowLoopBtn, false);
        UpdateVolumeTooltip();
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
        PopupVeil.Visibility = Visibility.Visible;
        RowPopupVeil.Visibility = Visibility.Visible;
        switch (kind)
        {
            case "vol":
                _openPopup = kind;
                ShowPopup(VolumePopup, IsListView ? RowVolBtn : VolBtn, -40);
                Ctl.SetIsPopupOpen(VolBtn, true);
                Ctl.SetIsPopupOpen(RowVolBtn, true);
                break;
            case "dev":
                DeviceListNeedsRefresh?.Invoke();
                RebuildDeviceList();
                _openPopup = kind;
                ShowPopup(DevicePopup, IsListView ? RowDevBtn : DevBtn, -268);
                Ctl.SetIsPopupOpen(DevBtn, true);
                Ctl.SetIsPopupOpen(RowDevBtn, true);
                break;
            case "loop":
                _openPopup = kind;
                ShowPopup(LoopPopup, IsListView ? RowLoopBtn : LoopBtn, 0);
                Ctl.SetIsPopupOpen(LoopBtn, true);
                Ctl.SetIsPopupOpen(RowLoopBtn, true);
                break;
        }
        UpdateVolumeTooltip();
    }

    private void ShowPopup(Border panel, FrameworkElement target, double horizontalOffset)
    {
        _popupAdornerLayer = AdornerLayer.GetAdornerLayer(this)
            ?? throw new InvalidOperationException("The deck dropdown adorner layer is unavailable.");
        PopupTemplates.Children.Remove(panel);
        panel.Visibility = Visibility.Visible;
        panel.DataContext = this;
        _openPopupPanel = panel;
        _popupTarget = target;
        _popupAdorner = new DeckDropdownAdorner(this, panel, target, horizontalOffset, -10);
        _popupAdornerLayer.Add(_popupAdorner);
        _popupAdorner.InvalidateMeasure();
        _popupAdorner.InvalidateArrange();
    }

    /// <summary>Hover text for the volume button: the level the deck is playing
    /// at, matching the readout in the volume modal. Suppressed while that modal
    /// is open, where the level is already on screen.</summary>
    private void UpdateVolumeTooltip()
    {
        ToolTipService.SetIsEnabled(VolBtn, _openPopup != "vol");
        ToolTipService.SetIsEnabled(RowVolBtn, _openPopup != "vol");
        VolBtn.ToolTip = $"Volume: {(int)Math.Round(EffectiveVolume * 100.0)}%";
        RowVolBtn.ToolTip = VolBtn.ToolTip;
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

    public void RefreshOpenPopupPosition()
    {
        if (_popupAdorner == null || _popupTarget == null) return;
        if (!IsPopupTargetVisible(_popupTarget))
        {
            ClosePopups();
            return;
        }
        _popupAdorner.InvalidateArrange();
    }

    private bool IsPopupTargetVisible(FrameworkElement? target)
    {
        if (target == null || !target.IsVisible
            || Window.GetWindow(this) is not MainWindow window
            || window.ScrollHost.ViewportWidth <= 0 || window.ScrollHost.ViewportHeight <= 0)
        {
            return false;
        }

        Rect targetBounds = target.TransformToAncestor(window.ScrollHost)
            .TransformBounds(new Rect(target.RenderSize));
        var viewport = new Rect(0, 0, window.ScrollHost.ViewportWidth, window.ScrollHost.ViewportHeight);
        return viewport.IntersectsWith(targetBounds);
    }

    // Let window-level outside-click dismissal distinguish the dropdown itself
    // from the deck underneath it.
    public bool IsPointOverOpenPopup(Point screenPoint)
    {
        if (_openPopupPanel == null || !_openPopupPanel.IsVisible
            || PresentationSource.FromVisual(_openPopupPanel) == null)
            return false;
        Point local = _openPopupPanel.PointFromScreen(screenPoint);
        return new Rect(_openPopupPanel.RenderSize).Contains(local);
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
    private void AutoPlayBtn_Click(object sender, RoutedEventArgs e)
    {
        _deck?.SetAutoPlay(!AutoPlay);
        SyncImmediate();
    }

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

    private void VolumeSlider_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Apply each wheel delta directly to the fader and keep it from
        // bubbling to a containing scroll viewer.
        e.Handled = true;
        double step = VolumeSlider.SmallChange > 0 ? VolumeSlider.SmallChange : 0.05;
        double value = VolumeSlider.Value + step * e.Delta / Mouse.MouseWheelDeltaForOneLine;
        VolumeSlider.Value = Math.Clamp(value, VolumeSlider.Minimum, VolumeSlider.Maximum);
    }

    private void Seek_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Slider slider) return;
        _seeking = true;
        if (!IsInsideSliderThumb(slider, e.OriginalSource as DependencyObject))
        {
            SeekToPointerPosition(slider, e);
            e.Handled = true;
        }
    }

    private void Seek_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _seeking = false;
        if (_deck != null) _deck.Seek(((Slider)sender).Value);
    }

    private void SeekToPointerPosition(Slider slider, MouseButtonEventArgs e)
    {
        if (_deck == null || !HasMedia || slider.Template.FindName("PART_Track", slider) is not Track track)
            return;

        double thumbWidth = Math.Max(track.Thumb?.ActualWidth ?? 0, 14);
        double travel = track.ActualWidth - thumbWidth;
        if (travel <= 0) return;

        Point trackOrigin = track.TranslatePoint(new Point(0, 0), slider);
        double pointerX = e.GetPosition(slider).X - trackOrigin.X;
        double ratio = Math.Clamp((pointerX - thumbWidth / 2) / travel, 0, 1);
        double value = slider.Minimum + ratio * (slider.Maximum - slider.Minimum);
        if (double.IsFinite(value)) slider.Value = value;
    }

    private static bool IsInsideSliderThumb(Slider slider, DependencyObject? source)
    {
        for (DependencyObject? node = source; node != null && !ReferenceEquals(node, slider);
             node = VisualTreeHelper.GetParent(node))
        {
            if (node is Thumb) return true;
        }
        return false;
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
        if (sender is not Slider slider) return;
        bool isRowSeek = ReferenceEquals(slider, RowSeek);
        Border tip = isRowSeek ? RowSeekTip : SeekTip;
        TrackedText tipText = isRowSeek ? RowSeekTipText : SeekTipText;
        double w = slider.ActualWidth;
        if (w <= 0) return;
        var max = DurationSecs > 0 ? DurationSecs : 1.0;
        double x = e.GetPosition(slider).X;
        double ratio = Math.Clamp(x / w, 0.0, 1.0);
        tipText.Text = FormatTime(ratio * max);
        tip.Margin = isRowSeek
            ? new Thickness(Math.Clamp(x - 28, 0, Math.Max(0, w - 56)), -17, 0, 0)
            : new Thickness(Math.Clamp(x - 28, 0, Math.Max(0, w - 56)) + 16, 9, 0, 0);
        tip.Visibility = Visibility.Visible;
    }

    private void Seek_MouseLeave(object sender, MouseEventArgs e)
    {
        if (ReferenceEquals(sender, RowSeek)) RowSeekTip.Visibility = Visibility.Collapsed;
        else SeekTip.Visibility = Visibility.Collapsed;
    }

    private static string FormatTime(double secs)
    {
        long total = (long)Math.Round(secs);
        return $"{(total / 60)}:{total % 60:00}";
    }
}