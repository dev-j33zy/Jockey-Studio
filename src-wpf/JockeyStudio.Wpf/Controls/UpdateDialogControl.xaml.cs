using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JockeyStudio.Wpf.Engine;

namespace JockeyStudio.Wpf.Controls;

/// <summary>1:1 of ui/src/components/UpdateDialog.tsx: shows an available release
/// (current → latest, release notes) and lets the user defer or install it.</summary>
public partial class UpdateDialogControl : UserControl
{
    public event RoutedEventHandler? LaterClicked;
    public event RoutedEventHandler? InstallClicked;
    public event RoutedEventHandler? CloseClicked;

    public UpdateDialogControl()
    {
        InitializeComponent();
    }

    /// <summary>Populate the dialog with a release before showing it.</summary>
    public void ShowUpdate(UpdateInfo info)
    {
        CurrentVersionText.Text = "v" + info.CurrentVersion;
        LatestVersionText.Text = "v" + info.LatestVersion;
        ReleaseNotesText.Text = string.IsNullOrWhiteSpace(info.ReleaseNotes)
            ? "No release notes for this version."
            : info.ReleaseNotes;
        SetInstalling(false);
    }

    /// <summary>While the installer is being fetched the buttons disable and a
    /// progress note appears (1:1 of the React updateInProgress state).</summary>
    public void SetInstalling(bool installing)
    {
        InstallButton.Content = installing ? "Downloading & installing…" : "Install Now";
        InstallButton.IsEnabled = !installing;
        LaterButton.IsEnabled = !installing;
        UpdateProgressNote.Visibility = installing ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Veil_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => CloseClicked?.Invoke(this, e);

    private void Dialog_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => e.Handled = true;

    private void LaterButton_Click(object sender, RoutedEventArgs e)
        => LaterClicked?.Invoke(this, e);

    private void InstallButton_Click(object sender, RoutedEventArgs e)
        => InstallClicked?.Invoke(this, e);

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => CloseClicked?.Invoke(this, e);
}