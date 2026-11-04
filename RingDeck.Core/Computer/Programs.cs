using System.Text;
using System.Text.Json.Nodes;

namespace RingDeck.Core;

/// <summary>Something in the Start menu that can be opened. <see cref="Target"/> is what Windows starts: a
/// program's path, or "shell:AppsFolder\..." for any Start menu entry, Store apps included.</summary>
public sealed record InstalledProgram(string Name, string Target);

/// <summary>A window from agent-desktop's list-windows.</summary>
public sealed record DesktopWindow(string Id, string App, string Title);

/// <summary>Finds the program the user means from everyday words ("edge", "vs code", "whatsapp"), and the window
/// that belongs to it.</summary>
public static class ProgramFinder
{
    /// <summary>Lower case, letters and digits only, single spaces: "Visual Studio Code (User)" → "visual studio code user".</summary>
    internal static string Simple(string text)
    {
        var sb = new StringBuilder();
        foreach (var c in text.ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    static readonly string[] Vendors = { "microsoft ", "google ", "mozilla ", "adobe " };

    /// <summary>How well a Start menu name fits what was asked for, 0 (not at all) to 100 (the same).</summary>
    public static int Score(string asked, string name)
    {
        var q = Simple(asked);
        var n = Simple(name);
        if (q.Length == 0 || n.Length == 0) return 0;
        if (n == q) return 100;
        if (Vendors.Any(v => n == v + q || q == v + n)) return 95;
        var nameWords = n.Split(' ');
        var askedWords = q.Split(' ');
        if (n.StartsWith(q + " ", StringComparison.Ordinal)) return 80;              // "outlook" → "outlook classic"
        if (askedWords.All(nameWords.Contains)) return 70;                           // "teams" → "microsoft teams"
        if (askedWords.All(w => nameWords.Any(x => x.StartsWith(w, StringComparison.Ordinal)))) return 55;
        var initials = string.Concat(nameWords.Select(w => w[0]));                   // "vs code" → "visual studio code"
        if (askedWords.Length > 1 && askedWords.Take(askedWords.Length - 1).All(w => w.Length <= 3)
            && initials.StartsWith(string.Concat(askedWords[..^1]), StringComparison.Ordinal) && nameWords.Contains(askedWords[^1])) return 50;
        if (n.Replace(" ", "").Contains(q.Replace(" ", ""), StringComparison.Ordinal)) return 40;
        return 0;
    }

    /// <summary>The program that fits best, when one fits at all; a shorter name wins a tie ("Outlook" over "Outlook (classic)").</summary>
    public static InstalledProgram? Best(string asked, IEnumerable<InstalledProgram> programs) =>
        programs.Select(p => (p, s: Score(asked, p.Name))).Where(x => x.s > 0)
            .OrderByDescending(x => x.s).ThenBy(x => x.p.Name.Length).Select(x => x.p).FirstOrDefault();

    /// <summary>Names to suggest when nothing fits: those sharing a word, or starting with the same letters.</summary>
    public static List<string> Similar(string asked, IEnumerable<InstalledProgram> programs, int count = 8)
    {
        var words = Simple(asked).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var start = words.Length > 0 ? words[0][..Math.Min(3, words[0].Length)] : "";
        return programs.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(n => Simple(n).Split(' ').Any(w => words.Contains(w) || (start.Length > 0 && w.StartsWith(start, StringComparison.Ordinal))))
            .OrderBy(n => n.Length).Take(count).ToList();
    }

    /// <summary>The windows in a list-windows reply (empty when it can't be read).</summary>
    public static List<DesktopWindow> Windows(string listWindowsJson)
    {
        try
        {
            var data = JsonNode.Parse(listWindowsJson)?["data"];
            var list = data as JsonArray ?? data?["windows"] as JsonArray;
            return list?.OfType<JsonObject>()
                .Select(w => new DesktopWindow(w["id"]?.ToString() ?? "", w["app_name"]?.ToString() ?? "", w["title"]?.ToString() ?? ""))
                .Where(w => w.Id.Length > 0).ToList() ?? new();
        }
        catch (System.Text.Json.JsonException) { return new(); }
    }

    /// <summary>The window that belongs to a program: its file is called <paramref name="exe"/> ("msedge"), or its
    /// name is in the window's file name or at the end of its title ("... - Microsoft Edge", "WhatsApp").</summary>
    public static DesktopWindow? WindowOf(IEnumerable<DesktopWindow> windows, string exe, string? name)
    {
        var file = Simple(exe);
        var simpleName = name is null ? "" : Simple(name);
        bool FileIs(DesktopWindow w) => Simple(DesktopTools.ProgramName(w.App)) == file;
        bool Named(DesktopWindow w)
        {
            if (simpleName.Length == 0) return false;
            var title = Simple(w.Title);
            var app = Simple(DesktopTools.ProgramName(w.App));
            return title == simpleName || title.EndsWith(" " + simpleName, StringComparison.Ordinal)
                || (simpleName.Replace(" ", "").Length >= 4 && app.Replace(" ", "").Contains(simpleName.Replace(" ", ""), StringComparison.Ordinal));
        }
        var all = windows.ToList();
        return all.FirstOrDefault(FileIs) ?? all.FirstOrDefault(Named);
    }
}
