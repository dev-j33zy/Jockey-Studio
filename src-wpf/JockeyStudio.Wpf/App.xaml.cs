using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using JockeyStudio.Wpf.Helpers;

namespace JockeyStudio.Wpf;

/// <summary>Entry point for the WPF shell. Layout resources are merged in
/// App.xaml; all presentational wiring lives in the controls.</summary>
public partial class App : Application
{
    public App()
    {
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        DispatcherUnhandledException += (_, e) =>
        {
            try
            {
                var log = Path.Combine(Path.GetTempPath(), "jockey-wpf-crash.log");
                File.WriteAllText(log, e.Exception.ToString());
            }
            catch { }
            e.Handled = false;
        };
    }

    /// <summary>Restore the persisted appearance before the main window starts
    /// resolving resources (the palette swap must be in place first).</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        ThemeManager.Apply(AppPrefs.Theme);
        base.OnStartup(e);
    }
}