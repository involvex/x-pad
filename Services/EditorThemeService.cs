using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using TextMateSharp.Grammars;

namespace x_pad.Services;

public readonly record struct EditorSurface(string BackgroundHex, string ForegroundHex);

public static class EditorThemeService
{
    public static ThemeName CurrentTextMateTheme { get; private set; } = ThemeName.DarkPlus;

    public static string CurrentAppTheme { get; private set; } = "Dark";

    public static EditorSurface CurrentSurface { get; private set; } = new("#1E1E1E", "#D4D4D4");

    public static event Action<ThemeName>? ThemeChanged;

    public static void ApplyAppTheme(string theme)
    {
        if (Application.Current == null)
            return;

        CurrentAppTheme = theme;
        ThemeVariant variant;
        switch (theme)
        {
            case "Light":
                variant = ThemeVariant.Light;
                Application.Current.RequestedThemeVariant = variant;
                CurrentSurface = new("#FFFFFF", "#1E1E1E");
                SetTextMateTheme(ThemeName.LightPlus, force: true);
                break;
            case "System":
                variant = ThemeVariant.Default;
                Application.Current.RequestedThemeVariant = variant;
                var isDark = Application.Current.ActualThemeVariant == ThemeVariant.Dark;
                CurrentSurface = isDark
                    ? new("#1E1E1E", "#D4D4D4")
                    : new("#FFFFFF", "#1E1E1E");
                SetTextMateTheme(isDark ? ThemeName.DarkPlus : ThemeName.LightPlus, force: true);
                break;
            case "Monokai":
                variant = ThemeVariant.Dark;
                Application.Current.RequestedThemeVariant = variant;
                CurrentSurface = new("#272822", "#F8F8F2");
                SetTextMateTheme(ThemeName.Monokai, force: true);
                break;
            case "Hacker":
                variant = ThemeVariant.Dark;
                Application.Current.RequestedThemeVariant = variant;
                CurrentSurface = new("#020b02", "#33ff66");
                SetTextMateTheme(ThemeName.Abbys, force: true);
                break;
            case "Amoled":
                variant = ThemeVariant.Dark;
                Application.Current.RequestedThemeVariant = variant;
                CurrentSurface = new("#000000", "#E6E6E6");
                SetTextMateTheme(ThemeName.HighContrastDark, force: true);
                break;
            case "Github":
                variant = ThemeVariant.Dark;
                Application.Current.RequestedThemeVariant = variant;
                CurrentSurface = new("#0D1117", "#E6EDF3");
                SetTextMateTheme(ThemeName.DarkPlus, force: true);
                break;
            default:
                variant = ThemeVariant.Dark;
                Application.Current.RequestedThemeVariant = variant;
                CurrentSurface = new("#1E1E1E", "#D4D4D4");
                SetTextMateTheme(ThemeName.DarkPlus, force: true);
                break;
        }

        // Keep open windows + named chrome scopes in sync (Win32 title bar / menus / tabs).
        if (Application.Current.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            var scoped = variant == ThemeVariant.Default ? null : variant;
            foreach (var window in desktop.Windows)
            {
                window.RequestedThemeVariant = scoped;
                foreach (var scope in window.GetVisualDescendants().OfType<ThemeVariantScope>())
                {
                    if (scope.Name == "AppChromeScope")
                        scope.RequestedThemeVariant = scoped ?? ThemeVariant.Default;
                }
            }
        }
    }

    public static void SetTextMateTheme(ThemeName theme, bool force = false)
    {
        if (!force && CurrentTextMateTheme == theme)
            return;
        CurrentTextMateTheme = theme;
        ThemeChanged?.Invoke(theme);
    }

    /// <summary>Re-apply the current theme so late-created editors pick up TextMate colors.</summary>
    public static void RefreshCurrentTheme()
    {
        SetTextMateTheme(CurrentTextMateTheme, force: true);
    }
}
