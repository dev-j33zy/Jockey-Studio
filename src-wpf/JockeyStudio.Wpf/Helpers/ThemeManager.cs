using System.Windows;

namespace JockeyStudio.Wpf.Helpers;

public enum ThemeMode
{
    Light,
    Dark,
    System,
}

/// <summary>Runtime theme switching. The dark and light palettes are two
/// ResourceDictionaries with identical keys (Themes/Colors.xaml and
/// Themes/Colors.Light.xaml). The dark one is merged first in App.xaml so any
/// StaticResource (e.g. glow colors) resolves at parse; switching themes simply
/// swaps the palette dictionary occupying that merged slot. Every control that
/// touches a palette brush uses DynamicResource, so the swap repaints the app
/// immediately. ThemeMode.System follows the OS setting.</summary>
public static class ThemeManager
{
    public static ThemeMode Current { get; private set; } = ThemeMode.System;

    public static bool IsDarkEffective { get; private set; } = true;

    public static event EventHandler? ThemeChanged;

    /// <summary>Switch the active palette immediately and notify listeners so
    /// the Appearance menu can follow. Never throws.</summary>
    public static void Apply(ThemeMode mode)
    {
        try
        {
            Current = mode;
            var app = Application.Current;
            if (app == null) return;

            bool dark = mode switch
            {
                ThemeMode.Light => false,
                ThemeMode.Dark => true,
                _ => !AppsUseLightTheme(),
            };
            IsDarkEffective = dark;

            var target = new ResourceDictionary
            {
                Source = new Uri(dark ? "Themes/Colors.xaml" : "Themes/Colors.Light.xaml", UriKind.Relative),
            };

            var merged = app.Resources.MergedDictionaries;
            for (int i = 0; i < merged.Count; i++)
            {
                string? src = merged[i].Source?.OriginalString;
                if (src == null) continue;
                if (src.EndsWith("Colors.Light.xaml", StringComparison.OrdinalIgnoreCase)
                    || src.EndsWith("Colors.xaml", StringComparison.OrdinalIgnoreCase))
                {
                    merged[i] = target;
                    ThemeChanged?.Invoke(null, EventArgs.Empty);
                    return;
                }
            }

            // No palette slot (shouldn't happen) — make it available up front.
            merged.Insert(0, target);
            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }
        catch
        {
            // a theme swap must never crash the app
        }
    }

    /// <summary>True when Windows is set to light apps (HKCU AppsUseLightTheme);
    /// a missing/unreadable key falls back to dark.</summary>
    private static bool AppsUseLightTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int n) return n != 0;
        }
        catch
        {
        }
        return false;
    }
}