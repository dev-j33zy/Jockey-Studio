using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JockeyStudio.Wpf.Engine;
using JockeyStudio.Wpf.Helpers;
using static JockeyStudio.Wpf.Engine.EngineConst;

namespace JockeyStudio.Wpf.Controls;

/// <summary>Standalone Engine Settings window in the same chrome as the About
/// window (native OS title bar + title row + tab strip). Reads the current
/// engine settings + device snapshot via <see cref="Load"/> and pushes edits
/// back through <see cref="SettingsChanged"/> (the host calls engine.SetSettings)
/// so changes apply to the live session in real time — no save button.</summary>
public partial class SettingsWindow : Window
{
    public event EventHandler<EngineSettings>? SettingsChanged;
    public event EventHandler? RefreshDevicesRequested;

    private readonly (Button Btn, FrameworkElement Panel)[] Tabs =
    {
        (null!, null!), (null!, null!), (null!, null!),
    };

    private EngineSettings _settings = new();
    private IReadOnlyList<OutputDevice> _devices = Array.Empty<OutputDevice>();
    private bool _loading;
    private int _activeTab = -1;

    public SettingsWindow()
    {
        InitializeComponent();
        Tabs[0] = (TabAutomixButton, AutomixPanel);
        Tabs[1] = (TabPlaybackButton, PlaybackPanel);
        Tabs[2] = (TabAudioButton, AudioPanel);
        SetActiveTab(0);
    }

    /// <summary>Push fresh engine settings + device snapshot into the controls.
    /// Called whenever the window opens and after a device refresh.</summary>
    public void Load(EngineSettings settings, IReadOnlyList<OutputDevice> devices)
    {
        _settings = settings;
        _devices = devices;
        _loading = true;
        try
        {
            AutoMixDbSlider.Value = Math.Clamp(settings.AutoMixDb, AutoMixDbSlider.Minimum, AutoMixDbSlider.Maximum);
            AttackSlider.Value = Math.Clamp(settings.AutoMixAttackMs / 1000.0, AttackSlider.Minimum, AttackSlider.Maximum);
            ReleaseSlider.Value = Math.Clamp(settings.AutoMixReleaseMs / 1000.0, ReleaseSlider.Minimum, ReleaseSlider.Maximum);
            FadeInSlider.Value = Math.Clamp(settings.DefaultFadeIn, FadeInSlider.Minimum, FadeInSlider.Maximum);
            FadeOutSlider.Value = Math.Clamp(settings.DefaultFadeOut, FadeOutSlider.Minimum, FadeOutSlider.Maximum);
            RebuildDeviceCombo(settings.DefaultDeviceId);
        }
        finally
        {
            _loading = false;
        }
    }

    private void RebuildDeviceCombo(string selectedId)
    {
        DeviceCombo.Items.Clear();
        var systemDefault = new ComboBoxItem { Tag = DEFAULT_DEVICE_ID, Content = "System Default" };
        DeviceCombo.Items.Add(systemDefault);
        foreach (var dev in _devices)
        {
            if (dev.Id == DEFAULT_DEVICE_ID || string.IsNullOrEmpty(dev.Name)) continue;
            DeviceCombo.Items.Add(new ComboBoxItem { Tag = dev.Id, Content = dev.Name });
        }

        ComboBoxItem? target = null;
        foreach (ComboBoxItem item in DeviceCombo.Items)
        {
            if (string.Equals(item.Tag as string, selectedId, StringComparison.OrdinalIgnoreCase))
            {
                target = item;
                break;
            }
        }
        DeviceCombo.SelectedItem = target != null ? target : systemDefault;
        RefreshDeviceValueText();
    }

    private void RefreshDeviceValueText()
        => DeviceValueText.Text = DeviceDisplayText(SelectedDeviceId);

    /// <summary>1:1 of the React panel label: "System Default" for the OS
    /// default, else the configured device's name, else its raw id.</summary>
    private string DeviceDisplayText(string id)
    {
        if (string.Equals(id, DEFAULT_DEVICE_ID, StringComparison.OrdinalIgnoreCase)) return "System Default";
        foreach (var dev in _devices)
        {
            if (string.Equals(dev.Id, id, StringComparison.OrdinalIgnoreCase)) return dev.Name;
        }
        return id;
    }

    private string SelectedDeviceId =>
        (DeviceCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? DEFAULT_DEVICE_ID;

    private EngineSettings BuildSettings() => new()
    {
        AutoMixEnabled = _settings.AutoMixEnabled,
        AutoMixDb = (float)AutoMixDbSlider.Value,
        AutoMixAttackMs = (ulong)Math.Round(AttackSlider.Value * 1000.0),
        AutoMixReleaseMs = (ulong)Math.Round(ReleaseSlider.Value * 1000.0),
        DefaultFadeIn = (float)FadeInSlider.Value,
        DefaultFadeOut = (float)FadeOutSlider.Value,
        DefaultDeviceId = SelectedDeviceId,
    };

    private void SettingSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        SettingsChanged?.Invoke(this, BuildSettings());
    }

    private void DeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        RefreshDeviceValueText();
        SettingsChanged?.Invoke(this, BuildSettings());
    }

    private void RefreshDevices_Click(object sender, RoutedEventArgs e)
        => RefreshDevicesRequested?.Invoke(this, e);

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