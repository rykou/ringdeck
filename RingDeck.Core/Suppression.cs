namespace RingDeck.Core;

/// <summary>The program that has keyboard focus, and whether its window covers the whole screen.</summary>
public readonly record struct ForegroundApp(string? Exe, bool Fullscreen);

/// <summary>Decides when ring shortcuts stay out of the way, for example inside a game.</summary>
public static class Suppression
{
    public static bool Applies(GlobalSettings settings, ForegroundApp app)
    {
        if (settings.DisableInFullscreen && app.Fullscreen) return true;
        return ProgramNames.AnyMatch(settings.ExcludedApps, app.Exe);
    }
}

/// <summary>Comparing program names the way people write them: "C:\Games\Wow.exe", "wow.exe" and " Wow " are the same.</summary>
public static class ProgramNames
{
    public static string Normalize(string? program)
    {
        var name = (program ?? "").Trim();
        name = name[(name.LastIndexOfAny(new[] { '\\', '/' }) + 1)..];
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    /// <summary>True when the running program (exe name or path) is one of the listed ones.</summary>
    public static bool AnyMatch(IEnumerable<string> list, string? running)
    {
        var name = Normalize(running);
        return name.Length > 0 && list.Any(entry => Normalize(entry).Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}
