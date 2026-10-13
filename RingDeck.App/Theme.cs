using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using RingDeck.Core;

namespace RingDeck.App;

public static class Theme
{
    public static bool IsDark { get; private set; } = true;
    public static Color Accent { get; private set; } = Color.FromRgb(0x4C, 0xC2, 0xFF);
    public static Color Foreground => IsDark ? Color.FromRgb(0xF2, 0xF2, 0xF5) : Color.FromRgb(0x16, 0x16, 0x1A);
    public static Color Subtle => IsDark ? Color.FromRgb(0x9A, 0x9A, 0xA8) : Color.FromRgb(0x6A, 0x6A, 0x78);

    public static Color Parse(string? hex, Color fallback)
    {
        try { return string.IsNullOrWhiteSpace(hex) ? fallback : (Color)ColorConverter.ConvertFromString(hex.Trim()); }
        catch (FormatException) { return fallback; }
    }

    public static void Apply(GlobalSettings settings)
    {
        IsDark = settings.Theme == "dark" || (settings.Theme != "light" && SystemIsDark());
        Accent = Parse(settings.Accent, Color.FromRgb(0x4C, 0xC2, 0xFF));
        var app = Application.Current;
        app.ThemeMode = settings.Theme switch { "dark" => ThemeMode.Dark, "light" => ThemeMode.Light, _ => ThemeMode.System };
        app.Resources["SubtleBrush"] = Frozen(Subtle);
        app.Resources["StageBrush"] = Frozen(IsDark ? Color.FromRgb(0x14, 0x14, 0x1A) : Color.FromRgb(0xE4, 0xE4, 0xEC));
    }

    public static SolidColorBrush Frozen(Color color, double opacity = 1)
    {
        var brush = new SolidColorBrush(color) { Opacity = opacity };
        brush.Freeze();
        return brush;
    }

    static bool SystemIsDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }
}
