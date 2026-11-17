using System;
using Avalonia;
using Avalonia.Styling;
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

        switch (theme)
        {
            case "Light":
                Application.Current.RequestedThemeVariant = ThemeVariant.Light;
                SetTextMateTheme(ThemeName.LightPlus);
                break;
            case "System":
                Application.Current.RequestedThemeVariant = ThemeVariant.Default;
                var isDark = Application.Current.ActualThemeVariant == ThemeVariant.Dark;
                SetTextMateTheme(isDark ? ThemeName.DarkPlus : ThemeName.LightPlus);
                break;
            case "Monokai":
                Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
                SetTextMateTheme(ThemeName.Monokai);
                break;
            default:
                Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
                SetTextMateTheme(ThemeName.DarkPlus);
                break;
        }
    }

    public static void SetTextMateTheme(ThemeName theme)
    {
        if (CurrentTextMateTheme == theme)
            return;
        CurrentTextMateTheme = theme;
        ThemeChanged?.Invoke(theme);
    }
}
