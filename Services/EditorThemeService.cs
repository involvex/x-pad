using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using TextMateSharp.Grammars;

namespace x_pad.Services;

public static class EditorThemeService
{
    public static ThemeName CurrentTextMateTheme { get; private set; } = ThemeName.DarkPlus;

    public static event Action<ThemeName>? ThemeChanged;

    public static void ApplyAppTheme(string theme)
    {
        if (Application.Current == null)
            return;

        ThemeVariant variant;
        switch (theme)
        {
            case "Light":
                variant = ThemeVariant.Light;
                Application.Current.RequestedThemeVariant = variant;
                SetTextMateTheme(ThemeName.LightPlus);
                break;
            case "System":
                variant = ThemeVariant.Default;
                Application.Current.RequestedThemeVariant = variant;
                var isDark = Application.Current.ActualThemeVariant == ThemeVariant.Dark;
                SetTextMateTheme(isDark ? ThemeName.DarkPlus : ThemeName.LightPlus);
                break;
            case "Monokai":
                variant = ThemeVariant.Dark;
                Application.Current.RequestedThemeVariant = variant;
                SetTextMateTheme(ThemeName.Monokai);
                break;
            default:
                variant = ThemeVariant.Dark;
                Application.Current.RequestedThemeVariant = variant;
                SetTextMateTheme(ThemeName.DarkPlus);
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
