using System.Reflection;

namespace JockeyStudio.Wpf.Engine;

/// <summary>Shared build metadata. The version drives the update comparison, the
/// "v0.1.2" labels and the About window, and must be kept in sync with the
/// version of the latest release published to GitHub (see <see cref="Updater"/>).</summary>
public static class AppInfo
{
    /// <summary>The running build version (e.g. "0.1.2").</summary>
    public static string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.2.0";
}