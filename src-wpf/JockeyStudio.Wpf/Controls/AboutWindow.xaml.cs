using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JockeyStudio.Wpf.Engine;
using JockeyStudio.Wpf.Helpers;

namespace JockeyStudio.Wpf.Controls;

/// <summary>Standalone About window: three tabs — About (brand + version),
/// What's New (bundled change log in sync with the updater's release notes)
/// and Updates (the check-for-updates UI that used to live in the settings
/// dialog). The host (MainWindow) feeds it the build version and relays update
/// check results through <see cref="SetUpdateStatus"/>.</summary>
public partial class AboutWindow : Window
{
    /// <summary>Raised by "Check for Updates"; the host runs the updater.</summary>
    public event EventHandler? CheckForUpdatesRequested;

    private static readonly (Button Btn, FrameworkElement Panel)[] Tabs =
    {
        (null!, null!), (null!, null!), (null!, null!),
    };

    private int _activeTab = -1;

    public AboutWindow()
    {
        InitializeComponent();
        Tabs[0] = (TabAboutButton, AboutPanel);
        Tabs[1] = (TabWhatNewButton, WhatNewPanel);
        Tabs[2] = (TabUpdatesButton, UpdatesPanel);
        SetActiveTab(0);

        LatestTitleText.Text = ChangeLog.LatestTitle;
        LatestSummaryText.Text = ChangeLog.LatestSummary;
        VersionFeed.ItemsSource = ChangeLog.Versions;
    }

    /// <summary>Jump the window to a dedicated page — 0 About, 1 What's New,
    /// 2 Updates. Called by the host when opening the window via the Help menu
    /// so each Help entry lands on its own page.</summary>
    public void OpenPage(int tab)
    {
        if (tab >= 0 && tab < Tabs.Length) SetActiveTab(tab);
    }

    /// <summary>Stamp the build version on the About tab.</summary>
    public void SetVersion(string version)
        => AboutVersionText.Text = "v" + version;

    /// <summary>Reflect an update check in the Updates tab: "Checking…" while
    /// in flight, "You're on the latest version" when current, null to clear.</summary>
    public void SetUpdateStatus(string? text)
    {
        UpdateStatusText.Text = text ?? "";
        UpdateStatusText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        UpdateButton.IsEnabled = text != "Checking…";
    }

    // ---------------- tabs ----------------

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string idx } && int.TryParse(idx, out int tab))
        {
            SetActiveTab(tab);
        }
    }

    private void SetActiveTab(int tab)
    {
        if (tab == _activeTab) return;
        _activeTab = tab;
        for (int i = 0; i < Tabs.Length; i++)
        {
            bool active = i == tab;
            Ctl.SetIsActive(Tabs[i].Btn, active);
            Tabs[i].Panel.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // ---------------- updates ----------------

    private void CheckForUpdates_Click(object sender, RoutedEventArgs e)
        => CheckForUpdatesRequested?.Invoke(this, e);

    // ---------------- close ----------------

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }
}