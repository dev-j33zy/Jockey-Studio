using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using JockeyStudio.Wpf.Controls;
using JockeyStudio.Wpf.Engine;
using JockeyStudio.Wpf.Helpers;
using Microsoft.Win32;

namespace JockeyStudio.Wpf;

/// <summary>Host window: owns the <see cref="PlayerEngine"/>, its 200ms
/// heartbeat, deck cards, media import (drag & drop) and the drawers.</summary>
public partial class MainWindow : Window
{
    private readonly PlayerEngine _engine = new();
    private readonly List<TileCardControl> _cards = new();
    private readonly DispatcherTimer _tick = new()
    {
        Interval = TimeSpan.FromMilliseconds(PlayerEngine.HEARTBEAT_MS),
    };

    // Tile reorder drag state (main window owns the session; cards just raise
    // ReorderDragRequested from their header). A header press only ARMS a drag
    // (_pendingCard); the drag (ghost + drop slot) starts after the pointer has
    // moved past DragStartThreshold, so a plain click with a few px of jitter is
    // never mistaken for a reorder.
    private TileCardControl? _dragCard;
    private TileCardControl? _pendingCard;
    private Point _pressPoint;
    private int _dragFrom = -1;
    private int _dragOver = -1;
    private Border? _dropSlot;
    private const double DragGhostOffsetY = 14.0;
    private const double DragStartThreshold = 6.0;
    private const double DragAutoScrollZone = 32.0;
    private const double DragAutoScrollStep = 24.0;

    // Update check state (1:1 of the React store updateStatus/updateInfo).
    private UpdateInfo? _updateInfo;
    private string _updateStatus = "idle"; // idle | checking | uptodate

    // The standalone About window (open once; re-shown when clicked again).
    private AboutWindow? _aboutWindow;

    // The standalone Engine Settings window (same lifecycle as _aboutWindow).
    private SettingsWindow? _settingsWindow;

    // Undo/Redo log. Each entry holds the state before and after a user edit
    // (deck add/remove/reorder, media load/unload/cut/paste, clear, playlist);
    // Undo replays "before", Redo replays "after", new edits drop the redo log.
    private readonly List<(AppState Before, AppState After)> _undoLog = new();
    private readonly List<(AppState Before, AppState After)> _redoLog = new();
    private const int MaxUndoDepth = 50;

    // Edit > Cut/Copy/Paste: the focused deck (accent ring) and the media item
    // currently sitting on the in-app clipboard.
    private Deck? _selectedDeck;
    private MediaItem? _clipboardMedia;

    // Find bar: filters the deck grid as the user types.
    private string _searchQuery = "";

    // View > zoom: rescales the deck grid by scaling its auto-fill minimum.
    private const double BaseCardWidth = 320.0;
    private const double BaseCardGap = 16.0;
    private const double ZoomMin = 0.5;
    private const double ZoomMax = 1.8;
    private double _zoom = 1.0;

    // View > full screen (F11).
    private bool _isFullScreen;
    private WindowStyle _prevWindowStyle;
    private WindowState _prevWindowState;

    // Persist the layout (loaded media, deck order, per-deck controls) on every
    // engine change, debounced so slider drags don't hammer the disk; the final
    // write happens on window close (1:1 of the React store.persist cadence).
    private readonly DispatcherTimer _saveDebounce = new()
    {
        Interval = TimeSpan.FromMilliseconds(400),
    };

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += MainWindow_SourceInitialized;

        // Restore the persisted appearance + zoom (theme is applied by the host
        // App before this window exists, so only the menu checks need syncing).
        SetZoom(AppPrefs.Zoom);
        ThemeManager.ThemeChanged += (_, _) => SyncThemeItems();
        SyncThemeItems();

        TopBar.AddDeckClicked += (_, _) => AddDeck();
        TopBar.LibraryToggled += (_, _) => ToggleLibrary();

        LibraryDrawer.CloseClicked += (_, _) => CloseLibrary();
        LibraryDrawer.LoadRequested += OnLibraryLoad;

        UpdateBubble.Clicked += (_, _) => OpenUpdateDialog();
        UpdateBubble.Dismissed += (_, _) =>
        {
            if (_updateInfo != null) _engine.DismissedUpdateVersion = _updateInfo.LatestVersion;
            RequestSave();
            UpdateBubble.Visibility = Visibility.Collapsed;
        };

        UpdateDialog.LaterClicked += (_, _) => CloseUpdateDialog();
        UpdateDialog.InstallClicked += async (_, _) => await InstallUpdateAsync();
        UpdateDialog.CloseClicked += (_, _) => CloseUpdateDialog();

        AllowDrop = true;
        DragOver += Window_DragOver;
        Drop += Window_Drop;

        _engine.PostToUi = action => Dispatcher.BeginInvoke(action);

        // Restore the persisted session (playlist, deck order, controls) on
        // launch; first-ever run starts with the default four decks.
        var persisted = StatePersistence.Load();
        if (persisted != null) _engine.Restore(persisted);
        else _engine.AddDecks(4);
        RebuildDecks();
        RefreshLibrary();

        _engine.Changed += RequestSave;
        _saveDebounce.Tick += (_, _) =>
        {
            _saveDebounce.Stop();
            PersistState();
        };

        _tick.Tick += (_, _) => EngineTick();
        _tick.Start();

        // Silent background update check — the app must never block its own
        // startup or produce any indication that it is scanning (1:1 of the
        // React store.init checkForUpdates()).
        _ = CheckForUpdatesAsync(manual: false);

        Closed += (_, _) =>
        {
            _tick.Stop();
            _saveDebounce.Stop();
            PersistState();
            _engine.Dispose();
        };

        // Tile reorder drag input (window-level so captured moves still land).
        PreviewMouseMove += Window_PreviewMouseMove;
        PreviewMouseUp += Window_PreviewMouseUp;
        PreviewKeyDown += Window_PreviewKeyDown;

        // Tile popups are separate transparent top-level windows. When the app
        // loses activation (alt-tab, another app focused, minimize) they must
        // close so they never float over other applications' windows.
        Deactivated += (_, _) =>
        {
            if (_pendingCard != null)
            {
                ReleaseMouseCapture();
                Cursor = Cursors.Arrow;
                _pendingCard = null;
            }
            if (_dragCard != null) EndDeckReorder(false);
            CloseAllTilePopups();
        };
    }

    // ---------------- decks ----------------

    private void RequestSave()
    {
        _saveDebounce.Stop();
        _saveDebounce.Start();
    }

    private void PersistState() => StatePersistence.Save(_engine.CaptureState());

    private void AddDeck()
    {
        RunUndoable(() => _engine.AddDeck());
        RebuildDecks();
    }

    private void AddFour_Click(object sender, RoutedEventArgs e)
    {
        RunUndoable(() => _engine.AddDecks(4));
        RebuildDecks();
    }

    private void RebuildDecks()
    {
        // A rebuild can drop decks (Restore replaces every object), so forget a
        // deleted selection rather than pointing at a stale deck instance.
        if (_selectedDeck != null && !_engine.Decks.Contains(_selectedDeck)) _selectedDeck = null;

        DeckPanel.Children.Clear();
        _cards.Clear();
        foreach (var deck in _engine.Decks) AddDeckCard(deck);
        UpdateDeckArea();
        ApplyDeckFilter();
    }

    private void AddDeckCard(Deck deck)
    {
        var card = new TileCardControl();
        card.Bind(deck);
        card.IsSelected = ReferenceEquals(deck, _selectedDeck);
        card.SetDeviceList(_engine.ListDevices(), _engine.DefaultDeviceDisplayName());
        card.ReorderDragRequested += OnReorderDragRequested;
        card.RemoveClicked += (_, _) =>
        {
            RunUndoable(() => _engine.RemoveDeck(deck.Id));
            RebuildDecks();
        };
        card.DeviceListNeedsRefresh += RefreshAudioDevices;
        card.CopyRequested += (_, _) => CopyFromDeck(deck);
        card.CutRequested += (_, _) => CutFromDeck(deck);
        card.PasteRequested += (_, _) => PasteIntoDeck(deck);
        card.UpdateFromDeck();
        _cards.Add(card);
        DeckPanel.Children.Add(card);
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        RunUndoable(() => _engine.ResetAll());
        foreach (var card in _cards) card.UpdateFromDeck();
        UpdateDeckArea();
        ApplyDeckFilter();
    }

    private void UpdateDeckArea()
    {
        bool hasDecks = _engine.Decks.Count > 0;
        EmptyState.Visibility = hasDecks ? Visibility.Collapsed : Visibility.Visible;
        ClearDecksButton.Visibility = hasDecks ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------------- tile reorder drag ----------------

    private void OnReorderDragRequested(object sender, RoutedEventArgs e)
    {
        if (_dragCard != null || _pendingCard != null || sender is not TileCardControl card) return;
        _pendingCard = card;
        _pressPoint = Mouse.GetPosition(this);
        Focus();
        CaptureMouse();
    }

    /// <summary>Start a tile-reorder drag: the dragged card leaves the grid and
    /// becomes a ghost on the overlay, a dashed drop slot takes its place in
    /// the flow, and the window captures the mouse so the slot slides as the
    /// pointer moves (1:1 of ui/src/App.tsx reorder dragging).</summary>
    private void BeginDeckReorder(TileCardControl card)
    {
        int from = _cards.IndexOf(card);
        if (from < 0) return;

        _dragFrom = from;
        _dragCard = card;

        card.Width = card.ActualWidth;
        card.Height = card.ActualHeight;
        DeckPanel.Children.Remove(card);
        card.IsHitTestVisible = false;
        card.SetGhostVisual(true);
        card.RenderTransformOrigin = new Point(0.5, 0.5);
        card.RenderTransform = new ScaleTransform(0.92, 0.92);
        DragLayer.Children.Add(card);

        _dropSlot = BuildDropSlot();
        DeckPanel.Children.Insert(Math.Min(from, DeckPanel.Children.Count), _dropSlot);
        DeckPanel.UpdateLayout();

        _dragOver = ComputeOver(CellRects(), GhostRect());
        MoveDropSlotTo(_dragOver);

        Point client = Mouse.GetPosition(this);
        PositionGhost(client.X, client.Y);

        Focus();
        CaptureMouse();
        Cursor = Cursors.Hand;
    }

    private void Window_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_pendingCard != null)
        {
            if ((Mouse.GetPosition(this) - _pressPoint).Length > DragStartThreshold)
            {
                var card = _pendingCard;
                _pendingCard = null;
                BeginDeckReorder(card);
            }
        }
        if (_dragCard == null) return;

        _dragOver = ComputeOver(CellRects(), GhostRect());
        MoveDropSlotTo(_dragOver);
        DeckPanel.UpdateLayout();

        Point client = e.GetPosition(this);
        PositionGhost(client.X, client.Y);
        AutoScroll(e.GetPosition(ScrollHost).Y);
    }

    private void Window_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_pendingCard != null)
        {
            ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
            _pendingCard = null;
            e.Handled = true;
            return;
        }
        if (_dragCard == null) return;
        bool commit = e.ChangedButton == MouseButton.Left;
        EndDeckReorder(commit);
        e.Handled = true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_pendingCard != null && e.Key == Key.Escape)
        {
            ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
            _pendingCard = null;
            e.Handled = true;
        }
        if (_dragCard != null && e.Key == Key.Escape)
        {
            EndDeckReorder(false);
            e.Handled = true;
        }
    }

    /// <summary>Finish the drag. If <paramref name="commit"/> and the slot moved
    /// from the deck's original position, reorder the engine decks and rebuild
    /// the (snapped) grid; otherwise just restore the grid as-is.</summary>
    private void EndDeckReorder(bool commit)
    {
        if (_dragCard == null) return;

        ReleaseMouseCapture();
        Cursor = Cursors.Arrow;

        if (_dropSlot != null) DeckPanel.Children.Remove(_dropSlot);
        DragLayer.Children.Clear();

        int from = _dragFrom;
        int over = _dragOver;
        _dragCard = null;
        _dropSlot = null;
        _dragFrom = -1;
        _dragOver = -1;

        if (commit && from >= 0 && over != from)
        {
            var ids = _engine.Decks.Select(d => d.Id).ToList();
            var draggedId = ids[from];
            ids.RemoveAt(from);
            ids.Insert(Math.Min(over, ids.Count), draggedId);
            RunUndoable(() => _engine.ReorderDecks(ids));
        }

        RebuildDecks();
    }

    /// <summary>The dragged card's current box in DeckPanel coordinates — exactly
    /// the geometry the ghost is drawn at (its center tracks the pointer, top
    /// is offset by DragGhostOffsetY), translated into the grid's space so the
    /// scroll offset never skews the drop math.</summary>
    private Rect GhostRect()
    {
        if (_dragCard == null) return Rect.Empty;
        Point origin = DeckPanel.TranslatePoint(new Point(0, 0), this);
        Point client = Mouse.GetPosition(this);
        double w = _dragCard.Width;
        double h = _dragCard.Height;
        return new Rect(client.X - origin.X - w / 2.0, client.Y - origin.Y - DragGhostOffsetY, w, h);
    }

    /// <summary>Drop slot index (0..count) in the deck-grid arrangement: delegate of
    /// <see cref="DropLayout.ComputeOver"/> — rows fully above the dragged
    /// card count entirely; within its row the slot sits on the cell that
    /// holds more than half of the card (dominant side), landing 1:1 on the
    /// aimed cell in every drag direction.</summary>
    private static int ComputeOver(IReadOnlyList<Rect> rects, Rect ghost)
        => DropLayout.ComputeOver(rects, ghost);

    /// <returns>The current rect of every grid cell in reading order — the cards
    /// AND the drop slot (which is an occupied cell of the arrangement too), so
    /// <see cref="DropLayout.ComputeOver"/> returns the exact grid position the
    /// slot should land on and the indicator tracks the aimed cell 1:1.</returns>
    private List<Rect> CellRects()
    {
        var rects = new List<Rect>();
        foreach (var child in DeckPanel.Children)
        {
            if (child is not FrameworkElement el || el.RenderSize.Width <= 0) continue;
            var topLeft = el.TranslatePoint(new Point(0, 0), DeckPanel);
            rects.Add(new Rect(topLeft, el.RenderSize));
        }
        return rects;
    }

    private void MoveDropSlotTo(int over)
    {
        if (_dropSlot == null) return;
        int idx = DeckPanel.Children.IndexOf(_dropSlot);
        if (idx == over) return;
        DeckPanel.Children.Remove(_dropSlot);
        DeckPanel.Children.Insert(Math.Min(over, DeckPanel.Children.Count), _dropSlot);
        DeckPanel.InvalidateMeasure();
    }

    private static Border BuildDropSlot()
    {
        var tile = new Border { CornerRadius = new CornerRadius(12), MinHeight = 320, IsHitTestVisible = false };
        tile.Child = new Rectangle
        {
            Fill = new SolidColorBrush(Color.FromArgb(0x0F, 0x3E, 0xCF, 0x8E)),
            Stroke = new SolidColorBrush(Color.FromArgb(0xFF, 0x3E, 0xCF, 0x8E)),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 3, 3 },
            RadiusX = 12,
            RadiusY = 12,
        };
        return tile;
    }

    private void PositionGhost(double clientX, double clientY)
    {
        if (_dragCard == null) return;
        Canvas.SetLeft(_dragCard, clientX - _dragCard.Width / 2.0);
        Canvas.SetTop(_dragCard, clientY - DragGhostOffsetY);
    }

    private void AutoScroll(double viewportY)
    {
        double vh = ScrollHost.ActualHeight;
        if (viewportY < DragAutoScrollZone && ScrollHost.VerticalOffset > 0)
            ScrollHost.ScrollToVerticalOffset(ScrollHost.VerticalOffset - DragAutoScrollStep);
        else if (viewportY > vh - DragAutoScrollZone)
            ScrollHost.ScrollToVerticalOffset(ScrollHost.VerticalOffset + DragAutoScrollStep);
    }

    private void EngineTick()
    {
        _engine.Tick();
        var devices = _engine.ListDevices();
        string defaultLabel = _engine.DefaultDeviceDisplayName();
        foreach (var card in _cards)
        {
            card.SetDeviceList(devices, defaultLabel);
            card.UpdateFromDeck();
        }
    }

    // ---------------- media import + library ----------------

    private void RefreshLibrary() => LibraryDrawer.SetMedia(_engine.Media);

    private void OnLibraryLoad(string mediaId)
    {
        RunUndoable(() =>
        {
            try { _engine.LoadIntoNextEmpty(mediaId); }
            catch (InvalidOperationException) { }
        });
        RebuildDecks();
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;

        var target = FindDeckAt(e.GetPosition(DeckPanel));
        bool changed = RunUndoable(() =>
        {
            bool first = true;
            foreach (var path in paths)
            {
                MediaItem item;
                try { item = _engine.ImportMedia(path); }
                catch (InvalidOperationException) { continue; }

                if (first && target != null) _engine.LoadMediaIntoTile(target.Id, item.Id);
                else _engine.LoadIntoNextEmpty(item.Id);
                first = false;
            }
        });
        if (!changed) return;
        RebuildDecks();
        RefreshLibrary();
    }

    private Deck? FindDeckAt(Point point)
    {
        var hit = DeckPanel.InputHitTest(point) as DependencyObject;
        for (var cur = hit; cur != null;)
        {
            if (cur is TileCardControl { DeckValue: { } deck }) return deck;
            cur = VisualTreeHelper.GetParent(cur) ?? LogicalTreeHelper.GetParent(cur);
        }
        return null;
    }

    // ---------------- popup dismissal ----------------

    // Click anywhere in the window (except a popup's own toggle button, whose
    // TogglePopup handles open/close) dismisses open tile popups.
    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // A click on a deck makes it the Edit menu's target (accent ring).
        SelectDeckAt(e.GetPosition(DeckPanel));
        if (IsPopupToggleButton(e.OriginalSource as DependencyObject)) return;
        // A click can land on this window even when the pointer is over a
        // popup (the popup's transparent pixels let it through); don't treat it
        // as a "click on empty area" while an open popup covers that point.
        var screen = PointToScreen(e.GetPosition(this));
        if (IsPointOverOpenPopup(screen)) return;
        CloseAllTilePopups();
    }

    public void CloseAllTilePopups()
    {
        foreach (var tile in FindVisualChildren<TileCardControl>(DeckPanel))
            tile.CloseAllPopups();
    }

    private bool IsPointOverOpenPopup(System.Windows.Point screen)
    {
        foreach (var tile in FindVisualChildren<TileCardControl>(DeckPanel))
        {
            if (tile.IsPointOverOpenPopup(screen)) return true;
        }
        return false;
    }

    private static bool IsPopupToggleButton(DependencyObject? source)
    {
        for (var cur = source; cur != null;
             cur = VisualTreeHelper.GetParent(cur) ?? LogicalTreeHelper.GetParent(cur))
        {
            if (cur is Button { Name: "VolBtn" or "DevBtn" or "LoopBtn" }) return true;
        }
        return false;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in FindVisualChildren<T>(child)) yield return nested;
        }
    }

    // ---------------- device refresh ----------------

    /// <summary>Re-enumerate the audio endpoints (1:1 of the React
    /// backend.refreshDevices → refresh flow, shared by the tile popups, the
    /// settings panel and the periodic tick that keeps device lists fresh).</summary>
    private void RefreshAudioDevices()
    {
        _engine.Devices.Refresh();
        var fresh = _engine.ListDevices();
        string defaultLabel = _engine.DefaultDeviceDisplayName();
        if (_settingsWindow != null) _settingsWindow.Load(_engine.Settings, fresh);
        foreach (var card in _cards) card.SetDeviceList(fresh, defaultLabel);
    }

    // ---------------- update checks ----------------

    /// <summary>Check the GitHub release feed. A manual click opens the dialog
    /// on a hit; a silent boot check only raises the bubble, and only once per
    /// dismissed version (1:1 of the React store.checkForUpdates).</summary>
    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_updateStatus == "checking") return;
        _updateStatus = "checking";
        SetUpdatesStatus("Checking…");

        UpdateInfo? info;
        try
        {
            info = await Updater.CheckForUpdatesAsync(AppInfo.Version);
        }
        catch
        {
            _updateStatus = "idle";
            SetUpdatesStatus(null);
            return;
        }

        if (info == null)
        {
            // Manual up-to-date: React sets updateStatus='uptodate' AND clears
            // updateInfo, so the bubble must go too — otherwise a boot-check
            // bubble lingers claiming an update the Updates tab now says isn't
            // there (1:1, updateInfo is the single source the bubble renders).
            _updateStatus = "uptodate";
            _updateInfo = null;
            UpdateBubble.Visibility = Visibility.Collapsed;
            SetUpdatesStatus("You're on the latest version");
            return;
        }

        _updateStatus = "idle";
        SetUpdatesStatus(null);
        _updateInfo = info;

        bool dismissed = string.Equals(_engine.DismissedUpdateVersion, info.LatestVersion, StringComparison.Ordinal);
        if (manual)
        {
            // The install flow lives in the main window's update dialog; drop the
            // About window so it isn't left floating over the modal-less overlay.
            if (_aboutWindow != null) CloseAboutWindow();
            UpdateDialog.ShowUpdate(info);
            OpenUpdateDialog();
        }
        else if (!dismissed)
        {
            UpdateBubble.ShowLatestVersion(info.LatestVersion);
            UpdateBubble.Visibility = Visibility.Visible;
        }
    }

    /// <summary>Relay an update-check status into the About window's Updates tab
    /// (no-op while the window isn't open, e.g. a silent boot check).</summary>
    private void SetUpdatesStatus(string? text)
    {
        if (_aboutWindow != null) _aboutWindow.SetUpdateStatus(text);
    }

    /// <summary>Download and run the new installer via a detached helper, then
    /// shut the app down (1:1 of the React store.installUpdate/update.rs).</summary>
    private async Task InstallUpdateAsync()
    {
        var info = _updateInfo;
        if (info == null) return;
        UpdateDialog.SetInstalling(true);
        try
        {
            string exe = Environment.ProcessPath
                ?? System.IO.Path.Combine(AppContext.BaseDirectory, "JockeyStudio.exe");
            await Updater.InstallUpdateAsync(info, exe);
            Application.Current.Shutdown();
        }
        catch
        {
            UpdateDialog.SetInstalling(false);
        }
    }

    // ---------------- View menu (full screen, appearance, zoom, docs) ----------------

    /// <summary>View → Full Screen (F11).</summary>
    public static readonly RoutedUICommand FullScreenCommand = new(
        "Toggle Full Screen", nameof(FullScreenCommand), typeof(MainWindow));

    /// <summary>View → Documentation: opens the GitHub repository readme.</summary>
    public static readonly RoutedUICommand OpenDocumentationCommand = new(
        "Open Documentation", nameof(OpenDocumentationCommand), typeof(MainWindow));

    private void FullScreen_Executed(object sender, ExecutedRoutedEventArgs e) => ToggleFullScreen();

    /// <summary>Toggle between the normal chrome and a borderless maximized
    /// view; the previous style/state are restored when toggling back.</summary>
    private void ToggleFullScreen()
    {
        _isFullScreen = !_isFullScreen;
        if (_isFullScreen)
        {
            _prevWindowStyle = WindowStyle;
            _prevWindowState = WindowState;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowStyle = _prevWindowStyle;
            WindowState = _prevWindowState;
        }
        FullScreenItem.IsChecked = _isFullScreen;
    }

    // ---- Appearance (Light / Dark / System) ----

    private void ThemeLight_Click(object sender, RoutedEventArgs e) => ApplyThemeMode(ThemeMode.Light);

    private void ThemeDark_Click(object sender, RoutedEventArgs e) => ApplyThemeMode(ThemeMode.Dark);

    private void ThemeSystem_Click(object sender, RoutedEventArgs e) => ApplyThemeMode(ThemeMode.System);

    private void ApplyThemeMode(ThemeMode mode)
    {
        ThemeManager.Apply(mode);
        AppPrefs.SetTheme(mode);
        SyncThemeItems();
    }

    /// <summary>Mirror the active theme onto the Appearance menu checkmarks.</summary>
    private void SyncThemeItems()
    {
        ThemeLightItem.IsChecked = ThemeManager.Current == ThemeMode.Light;
        ThemeDarkItem.IsChecked = ThemeManager.Current == ThemeMode.Dark;
        ThemeSystemItem.IsChecked = ThemeManager.Current == ThemeMode.System;
    }

    // ---- Zoom ----

    private void WindowZoomIn_Executed(object sender, ExecutedRoutedEventArgs e) => SetZoom(_zoom * 1.1);

    private void WindowZoomOut_Executed(object sender, ExecutedRoutedEventArgs e) => SetZoom(_zoom / 1.1);

    private void WindowZoomIn_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        => e.CanExecute = _zoom < ZoomMax;

    private void WindowZoomOut_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        => e.CanExecute = _zoom > ZoomMin;

    /// <summary>Rescales the deck grid by scaling the auto-fill minimum card
    /// width (and the gap) that TileGridPanel measures from, then persists the
    /// zoom level for the next launch.</summary>
    private void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, ZoomMin, ZoomMax);
        DeckPanel.MinItemWidth = BaseCardWidth * _zoom;
        DeckPanel.ItemGap = BaseCardGap * _zoom;
        AppPrefs.SetZoom(_zoom);
    }

    // ---- Documentation ----

    private void OpenDocumentation_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/dev-j33zy/Jockey-Studio")
            {
                UseShellExecute = true,
            });
        }
        catch
        {
            // no browser available — nothing else to do
        }
    }

    // ---------------- File menu ----------------

    /// <summary>File → Open Playlist command (Ctrl+Shift+O).</summary>
    public static readonly RoutedUICommand OpenPlaylistCommand = new(
        "Open Playlist", nameof(OpenPlaylistCommand), typeof(MainWindow));

    /// <summary>Extensions the Open dialog lists (the same set MediaProbe /
    /// Media Foundation can decode: audio + video containers).</summary>
    private const string SupportedMediaFilter =
        "Media files (*.mp3;*.wav;*.flac;*.m4a;*.aac;*.wma;*.ogg;*.mp4;*.mov;*.mkv;*.avi;*.webm;*.wmv;*.m4v;*.mpg;*.mpeg)|" +
        "*.mp3;*.wav;*.flac;*.m4a;*.aac;*.wma;*.ogg;*.mp4;*.mov;*.mkv;*.avi;*.webm;*.wmv;*.m4v;*.mpg;*.mpeg|" +
        "All files (*.*)|*.*";

    /// <summary>File → Open File: browse the file explorer and load each selected
    /// supported media file into the next empty deck (auto-adding decks when
    /// none are free), mirroring drag & drop import.</summary>
    private void MenuOpenFile_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open media file",
            Filter = SupportedMediaFilter,
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) != true) return;

        bool changed = RunUndoable(() =>
        {
            foreach (var path in dlg.FileNames)
            {
                try
                {
                    var item = _engine.ImportMedia(path);
                    _engine.LoadIntoNextEmpty(item.Id);
                }
                catch (InvalidOperationException)
                {
                    // unsupported file — skip it, keep loading the rest
                }
            }
        });
        if (!changed) return;
        RebuildDecks();
        RefreshLibrary();
    }

    /// <summary>File → Open Playlist: pick a saved .jcky playlist in the file
    /// explorer and restore it as the current session (decks, media, settings).</summary>
    private void MenuOpenPlaylist_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open playlist",
            Filter = "Jockey Studio Playlist (*.jcky)|*.jcky|All files (*.*)|*.*",
            Multiselect = false,
        };
        if (dlg.ShowDialog(this) != true) return;

        var state = StatePersistence.LoadFrom(dlg.FileName);
        if (state == null)
        {
            MessageBox.Show(this,
                "This file is not a Jockey Studio playlist (or is unreadable).",
                "Open Playlist", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        RunUndoable(() => _engine.Restore(state));
        RebuildDecks();
        RefreshLibrary();
        RequestSave();
    }

    private void MenuCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        => e.CanExecute = true;

    /// <summary>File → Save: write the current decks/playlists + settings to
    /// the current playlist file (.jcky), prompting once (Save As) when none is
    /// chosen yet. The automatic session restore keeps using its own app-state
    /// file.</summary>
    private void MenuSave_Executed(object sender, ExecutedRoutedEventArgs e)
        => SaveSession(prompt: false);

    /// <summary>File → Save As: always pick a target file first.</summary>
    private void MenuSaveAs_Executed(object sender, ExecutedRoutedEventArgs e)
        => SaveSession(prompt: true);

    private string? _sessionFilePath;

    private void SaveSession(bool prompt)
    {
        string? path = _sessionFilePath;
        if (prompt || string.IsNullOrEmpty(path))
        {
            var dlg = new SaveFileDialog
            {
                Title = "Save Jockey Studio playlist",
                Filter = "Jockey Studio Playlist (*.jcky)|*.jcky|All files (*.*)|*.*",
                FileName = "Jockey Studio Playlist.jcky",
                DefaultExt = ".jcky",
                AddExtension = true,
            };
            if (dlg.ShowDialog(this) != true) return;
            path = dlg.FileName;
            _sessionFilePath = path;
        }
        // The .jcky payload is JSON, but only this app reads/writes the format.
        StatePersistence.SaveTo(_engine.CaptureState(), path);
    }

    /// <summary>File → Edit Preferences: open the Engine Settings window.</summary>
    private void FilePreferences_Click(object sender, RoutedEventArgs e)
        => OpenSettingsWindow();

    /// <summary>File → Exit: shut the app down (the window's Closed handler
    /// persists the session to the default location first).</summary>
    private void FileExit_Click(object sender, RoutedEventArgs e)
        => Application.Current.Shutdown();

    // ---------------- Edit menu (Undo/Redo, Cut/Copy/Paste, Find) ----------------

    /// <summary>Wrap a user edit in the undo log: snapshot before + after, skip
    /// no-ops and drop the redo log. Returns true when the edit changed state.</summary>
    private bool RunUndoable(Action change)
    {
        var before = _engine.CaptureState();
        change();
        var after = _engine.CaptureState();
        if (StatePersistence.ToJson(before) == StatePersistence.ToJson(after)) return false;
        _undoLog.Add((before, after));
        if (_undoLog.Count > MaxUndoDepth) _undoLog.RemoveAt(0);
        _redoLog.Clear();
        return true;
    }

    private void EditUndo_Executed(object sender, ExecutedRoutedEventArgs e) => DoUndo();

    private void EditRedo_Executed(object sender, ExecutedRoutedEventArgs e) => DoRedo();

    private void EditUndo_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        => e.CanExecute = _undoLog.Count > 0;

    private void EditRedo_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        => e.CanExecute = _redoLog.Count > 0;

    private void DoUndo()
    {
        if (_undoLog.Count == 0) return;
        var entry = _undoLog[^1];
        _undoLog.RemoveAt(_undoLog.Count - 1);
        _redoLog.Add(entry);
        ApplyHistoryState(entry.Before);
    }

    private void DoRedo()
    {
        if (_redoLog.Count == 0) return;
        var entry = _redoLog[^1];
        _redoLog.RemoveAt(_redoLog.Count - 1);
        _undoLog.Add(entry);
        ApplyHistoryState(entry.After);
    }

    /// <summary>Replay a snapshot from the undo/redo log: restore decks, layout
    /// and library, then refresh every surface. Restore replaces the deck
    /// objects, so the focus ring resets to no deck.</summary>
    private void ApplyHistoryState(AppState state)
    {
        _engine.Restore(state);
        _selectedDeck = null;
        RebuildDecks();
        RefreshLibrary();
        RequestSave();
    }

    // ---- Cut / Copy / Paste (focused deck target) ----

    private void EditCut_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_selectedDeck != null) CutFromDeck(_selectedDeck);
    }

    private void EditCopy_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_selectedDeck != null) CopyFromDeck(_selectedDeck);
    }

    private void EditPaste_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_selectedDeck != null) PasteIntoDeck(_selectedDeck);
    }

    private void EditClipboard_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        => e.CanExecute = _selectedDeck is { HasMedia: true };

    private void EditPaste_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        => e.CanExecute = _clipboardMedia != null && _selectedDeck != null;

    /// <summary>Copy a deck's media onto the in-app clipboard (media stays in
    /// the library, source deck keeps it loaded).</summary>
    private void CopyFromDeck(Deck deck)
    {
        if (!deck.HasMedia) return;
        _clipboardMedia = _engine.Media.FirstOrDefault(m => m.Id == deck.MediaId);
    }

    /// <summary>Cut: copy onto the clipboard then unload it from this deck, so
    /// pasting elsewhere moves the track (undoable).</summary>
    private void CutFromDeck(Deck deck)
    {
        if (!deck.HasMedia) return;
        _clipboardMedia = _engine.Media.FirstOrDefault(m => m.Id == deck.MediaId);
        if (_clipboardMedia == null) return;
        if (!RunUndoable(() => _engine.UnloadMedia(deck.Id))) return;
        RebuildDecks();
        RefreshLibrary();
        ApplyDeckFilter();
    }

    /// <summary>Paste the clipboard media into a deck (replacing whatever it
    /// had loaded), so cut→paste also moves a track between decks.</summary>
    private void PasteIntoDeck(Deck deck)
    {
        if (_clipboardMedia == null) return;
        if (!RunUndoable(() => _engine.LoadMediaIntoTile(deck.Id, _clipboardMedia.Id))) return;
        RebuildDecks();
        RefreshLibrary();
    }

    // ---- Find (filter the deck grid) ----

    private void EditFind_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (FindBar.Visibility != Visibility.Visible) FindBar.Visibility = Visibility.Visible;
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchQuery = SearchBox.Text.Trim();
        ApplyDeckFilter();
    }

    /// <summary>Show only decks whose loaded media title contains the query
    /// (case-insensitive); an empty query shows every deck.</summary>
    private void ApplyDeckFilter()
    {
        foreach (var card in _cards)
        {
            card.Visibility = DeckMatchesQuery(card.DeckValue)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private bool DeckMatchesQuery(Deck? deck)
        => _searchQuery.Length == 0
           || (deck != null && deck.HasMedia
               && deck.Title.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase));

    // ---- deck selection (target of the Edit menu) ----

    private void SelectDeckAt(Point point)
    {
        var deck = FindDeckAt(point);
        if (deck == null) return;
        _selectedDeck = deck;
        foreach (var card in _cards) card.IsSelected = ReferenceEquals(card.DeckValue, deck);
    }

    // ---------------- drawers ----------------

    private void ToggleLibrary()
    {
        bool open = LibraryDrawer.Visibility != Visibility.Visible;
        LibraryDrawer.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        TopBar.IsLibraryOpen = open;
        if (open) _settingsWindow?.Close();
    }

    private void CloseLibrary()
    {
        LibraryDrawer.Visibility = Visibility.Collapsed;
        TopBar.IsLibraryOpen = false;
    }

    // ---------------- Settings window ----------------

    /// <summary>Create (once) and show the Engine Settings window as a modal
    /// over the main window, pushing the live engine settings + device snapshot
    /// in first. Escaping or the OS close button dismisses it; clicking Settings
    /// again reopens with fresh values.</summary>
    private void OpenSettingsWindow()
    {
        if (_settingsWindow != null)
        {
            if (!_settingsWindow.IsVisible) _settingsWindow.ShowDialog();
            return;
        }

        var window = new SettingsWindow
        {
            Owner = this,
        };
        window.Load(_engine.Settings, _engine.ListDevices());
        window.SettingsChanged += (_, settings) => _engine.SetSettings(settings);
        window.RefreshDevicesRequested += (_, _) => RefreshAudioDevices();
        window.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow = window;
        window.ShowDialog();
    }

    // ---------------- About window ----------------

    /// <summary>Create (once) and show the About window as a modal over the
    /// main window, landing on the requested page (0 About, 1 What's New,
    /// 2 Updates). Escaping or the close button dismisses it; reopening shows
    /// the same window on the requested page.</summary>
    private void OpenAboutWindow(int page = 0)
    {
        if (_aboutWindow != null)
        {
            if (!_aboutWindow.IsVisible) _aboutWindow.ShowDialog();
            _aboutWindow.OpenPage(page);
            return;
        }

        var about = new AboutWindow
        {
            Owner = this,
        };
        about.SetVersion(AppInfo.Version);
        about.CheckForUpdatesRequested += (_, _) => _ = CheckForUpdatesAsync(manual: true);
        about.Closed += (_, _) => _aboutWindow = null;
        _aboutWindow = about;
        about.OpenPage(page);
        about.ShowDialog();
    }

    // Help menu entries each land on the About window's dedicated page.
    private void HelpAbout_Click(object sender, RoutedEventArgs e) => OpenAboutWindow(page: 0);

    private void HelpWhatsNew_Click(object sender, RoutedEventArgs e) => OpenAboutWindow(page: 1);

    private void HelpCheckUpdates_Click(object sender, RoutedEventArgs e) => OpenAboutWindow(page: 2);

    private void CloseAboutWindow()
    {
        var about = _aboutWindow;
        _aboutWindow = null;
        about?.Close();
    }

    // ---------------- update overlays ----------------

    /// <summary>Open the update dialog. Populate it from the last checked info
    /// first so both entry points behave identically: clicking the bubble (silent
    /// path, where ShowUpdate wasn't called) and "Check for updates" (manual,
    /// which already loaded the info) land on the same populated dialog.</summary>
    private void OpenUpdateDialog()
    {
        if (_updateInfo != null) UpdateDialog.ShowUpdate(_updateInfo);
        UpdateDialog.Visibility = Visibility.Visible;
    }

    /// <summary>Close only the dialog; the bubble stays so a dismissed-then-
    /// reopened notification is still reachable (1:1 of the React
    /// closeUpdateDialog, which keeps updateInfo set until dismiss/install).</summary>
    private void CloseUpdateDialog() => UpdateDialog.Visibility = Visibility.Collapsed;

    // ---------------- native window styling ----------------

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero) SetImmersiveDarkMode(hwnd);
    }

    private static bool SetImmersiveDarkMode(IntPtr hwnd)
    {
        // DWMWA_USE_IMMERSIVE_DARK_MODE = 20 (Win11) / 19 (Win10 1809+)
        int enabled = 1;
        foreach (int attribute in new[] { 20, 19 })
        {
            if (DwmSetWindowAttribute(hwnd, attribute, ref enabled, sizeof(int)) == 0) return true;
        }
        return false;
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}