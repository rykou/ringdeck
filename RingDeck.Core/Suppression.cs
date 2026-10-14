namespace RingDeck.Core;

/// <summary>The program that has keyboard focus, and whether its window covers the whole screen.</summary>
public readonly record struct ForegroundApp(string? Exe, bool Fullscreen);

/// <summary>Decides when ring shortcuts stay out of the way, for example inside a game.</summary>
public static class Suppression
{
    public static bool Applies(GlobalSettings settings, ForegroundApp app)
    {
        if (settings.DisableInFullscreen && app.Fullscreen) return true;
        var running = Name(app.Exe);
        return running.Length > 0 && settings.ExcludedApps.Any(entry => Name(entry).Equals(running, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>"C:\Games\Wow.exe", "wow.exe" and " Wow " all name the same program.</summary>
    static string Name(string? program)
    {
        var name = (program ?? "").Trim();
        name = name[(name.LastIndexOfAny(new[] { '\\', '/' }) + 1)..];
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }
}
