using System.Text.Json.Nodes;

namespace RingDeck.Core;

/// <summary>What running agent-desktop once printed. <see cref="Ok"/> is false when it reported a failure.</summary>
public sealed record DesktopOutput(bool Ok, string Text);

/// <summary>agent-desktop isn't installed where RingDeck looked, so nothing can run. The message says where it looked.</summary>
public sealed class DesktopMissingException(string message) : Exception(message);

/// <summary>Runs agent-desktop (https://github.com/vercel-labs/agent-desktop), which reads and works Windows apps
/// through accessibility. Implemented in RingDeck.Windows.</summary>
public interface IAgentDesktop
{
    /// <summary>Runs it with these arguments, without a shell. Throws <see cref="DesktopMissingException"/> when it
    /// isn't installed.</summary>
    Task<DesktopOutput> RunAsync(IReadOnlyList<string> args, CancellationToken ct);

    /// <summary>The full path of a program named like "msedge" or "winword.exe", found the way Windows' Run box finds
    /// it (App Paths, then PATH); null when unknown. agent-desktop only finds programs in the Windows folder itself.</summary>
    string? FindProgram(string name) => null;

    /// <summary>Starts a program by its full path the way double-clicking it does; false when that isn't possible
    /// here. Needed because agent-desktop won't start an app that already has processes running, as a browser
    /// usually does even with every window closed.</summary>
    bool StartProgram(string path) => false;

    /// <summary>Everything in the Start menu, Store apps included.</summary>
    IReadOnlyList<InstalledProgram> InstalledPrograms() => Array.Empty<InstalledProgram>();

    /// <summary>Program files (.exe) in the usual install folders whose name fits <paramref name="query"/>.</summary>
    IReadOnlyList<string> SearchProgramFiles(string query) => Array.Empty<string>();

    /// <summary>All the text inside a window that offers it as text (terminals, documents), which agent-desktop's
    /// tree doesn't include: empty while it shows nothing (a program still starting), null when the window has no
    /// such text or can't be found.</summary>
    string? ReadWindowText(string windowId) => null;

    /// <summary>A window's title right now (a terminal's tab title often names the program running in it).</summary>
    string? WindowTitle(string windowId) => null;
}

/// <summary>The tools a "Use the computer" step gives the AI, and how each becomes an agent-desktop command line.
/// Deliberately missing: the clipboard (RingDeck sets it once, at the end), closing apps, dragging, and any way to
/// pass agent-desktop's --force (which lets dangerous key combinations through).</summary>
public static class DesktopTools
{
    public const string ReportStep = "report_step", Finish = "finish", FindProgram = "find_program", TypeText = "type_text", ReadText = "read_text";
    /// <summary>The most of a window's text read_text sends: the end, where a terminal's latest output is.</summary>
    public const int ReadTextChars = 12000;
    /// <summary>The most text one tool result sends back to the AI. A whole Edge window read is about 50,000 characters,
    /// mostly the page; the AI can read less with find, skeleton or root.</summary>
    public const int MaxOutputChars = 40000;

    static JsonObject Str(string description) => new() { ["type"] = "string", ["description"] = description };
    static JsonObject Int(string description) => new() { ["type"] = "integer", ["description"] = description };
    static JsonObject Bool(string description) => new() { ["type"] = "boolean", ["description"] = description };

    static JsonObject Schema(params (string Name, JsonObject Type, bool Required)[] properties)
    {
        var props = new JsonObject();
        foreach (var p in properties) props[p.Name] = p.Type;
        var schema = new JsonObject { ["type"] = "object", ["properties"] = props };
        var required = properties.Where(p => p.Required).Select(p => (JsonNode)p.Name).ToArray();
        if (required.Length > 0) schema["required"] = new JsonArray(required);
        return schema;
    }

    static (string, JsonObject, bool) Opt(string name, JsonObject type) => (name, type, false);
    static (string, JsonObject, bool) Req(string name, JsonObject type) => (name, type, true);

    const string RefHelp = "An element ref from the latest snapshot or find, such as @s8f3k2p9:e8";
    const string AppHelp = "App name, such as msedge, chrome, outlook or notepad";

    /// <summary>Types with the real keyboard into a whole window: only for steps that allow typing.</summary>
    public static AiTool TypeTextTool { get; } = new(TypeText,
        "Type text and press keys in a window with the real keyboard: for terminals and windows that have no text field to type into. "
        + "It brings the window to the front first. Set enter true to press Enter after the text (to run a command). "
        + "Use keys for arrow keys, menus and shortcuts, such as \"down down down enter\", \"esc\" or \"ctrl+shift+v\" (paste in a terminal). "
        + "Afterwards it watches the screen until it settles and returns what's on it, saying if nothing changed.",
        Schema(Req("window_id", Str("The window, from list_windows")), Opt("text", Str("The text to type")),
            Opt("enter", Bool("Press Enter after the text (default false)")),
            Opt("keys", Str("Keys to press after the text, in order, separated by spaces: names such as down, up, enter, tab, esc, f5, or combinations such as ctrl+c")),
            Opt("wait_for", Str("Text that shows the window is ready afterwards, such as a prompt or a program's title: it waits until it appears")),
            Opt("wait_seconds", Int($"How long to watch the screen afterwards, at most (default {ComputerAgent.DefaultWatchSeconds}, up to {ComputerAgent.MaxWatchSeconds}); use more for programs that start slowly"))));

    /// <summary>The tools a step gives the AI: all of <see cref="All"/>, plus typing into windows when the step allows it.</summary>
    public static IReadOnlyList<AiTool> For(ComputerStep step) => step.AllowTyping ? All.Append(TypeTextTool).ToList() : All;

    /// <summary>Every tool every step gets, in the order the AI sees them.</summary>
    public static IReadOnlyList<AiTool> All { get; } = new[]
    {
        new AiTool(ReportStep, "Say which of the user's steps you are starting. Call it before working on each step.",
            Schema(Req("step", Int("The step's number, from 1")), Req("description", Str("The step in a few words, such as \"Click Inbox\"")))),
        new AiTool(Finish, "End the task. Call it exactly once, when every step is done or when a step can't be done.",
            Schema(Req("status", new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("done", "failed"), ["description"] = "done or failed" }),
                Req("step", Int("For failed: the number of the step that failed. For done: the last step's number.")),
                Req("result", Str("For done: the result the user asked for, as plain text that reads well on a phone. For failed: a short reason, such as \"Gmail asks to sign in\".")))),
        new AiTool(FindProgram, "Search this computer for an installed program by name or by words in its name: the Start menu (Store apps included) "
            + "and program files in Program Files. Use it whenever launch can't find a program, then launch the exact name or path it gives.",
            Schema(Req("query", Str("Words from the program's name, such as \"whatsapp\" or \"visual studio\"")))),
        new AiTool(ReadText, "Read the text inside a window, such as a terminal's output (Windows Terminal, cmd, PowerShell) or a document, "
            + "which snapshot and find don't show. Returns the end of the text, where the latest output is. With wait_for, it waits until that text appears.",
            Schema(Req("window_id", Str("The window, from list_windows")), Opt("wait_for", Str("Wait until this text appears on screen")),
                Opt("wait_seconds", Int($"With wait_for: how long to wait at most (default {ComputerAgent.DefaultWatchSeconds}, up to {ComputerAgent.MaxWatchSeconds})")))),
        new AiTool("list_apps", "List running apps (name and process id). Cheap; reads no window contents.", Schema()),
        new AiTool("list_windows", "List visible windows with their ids and titles.", Schema(Opt("app", Str("Only this app's windows. " + AppHelp)))),
        new AiTool("snapshot", "Read a window's accessibility tree. Every element you can act on gets a ref. Refs only work until the window changes. "
            + "It reads 10 levels deep by default, which often stops before a web page's content: use find to read that.",
            Schema(Opt("app", Str(AppHelp)), Opt("window_id", Str("One window, from list_windows")),
                Opt("interactive_only", Bool("Only elements you can act on (default true, much smaller). Set false to read text such as an email list.")),
                Opt("max_depth", Int("How deep to read, 1 to 30 (default 10)")), Opt("skeleton", Bool("A shallow overview, good for a first look at a big window")),
                Opt("root", Str("Start from this ref instead of the whole window. " + RefHelp)))),
        new AiTool("find", "Search a window for elements by role, name or text, however deep they are. Smaller and faster than a snapshot, "
            + "and the way to read lists on web pages: each match's name holds its text. Email inboxes and tables: role row.",
            Schema(Opt("app", Str(AppHelp)), Opt("window_id", Str("One window, from list_windows")),
                Opt("role", Str("button, link, textfield, checkbox, menuitem, tab, row, cell, listitem, ...")), Opt("name", Str("The element's name")),
                Opt("text", Str("Text in its name, value, title or description")), Opt("first", Bool("Only the first match")),
                Opt("limit", Int("Most matches to return (default 50)")))),
        new AiTool("get", "Read one property of an element.",
            Schema(Req("ref", Str(RefHelp)), Opt("property", new JsonObject
            {
                ["type"] = "string", ["enum"] = new JsonArray("text", "value", "title", "bounds", "role", "states"), ["description"] = "Which property (default text)",
            }))),
        new AiTool("wait", "Wait for some text or a window to appear, or just pause.",
            Schema(Opt("ms", Int("Just pause this many milliseconds (at most 30000)")), Opt("text", Str("Wait until this text appears")),
                Opt("window", Str("Wait until a window with this title appears")), Opt("app", Str(AppHelp)),
                Opt("timeout_ms", Int("Give up after this many milliseconds (default 30000, at most 60000)")))),
        new AiTool("launch", "Open an app by its everyday name (Edge, Outlook, WhatsApp, Visual Studio Code, ...), its Start menu name, or its full path. "
            + "When it's already open, its window comes to the front instead. If it can't be found, use find_program before giving up.",
            Schema(Req("app", Str(AppHelp)), Opt("activate", Bool("Bring its window to the front and wait for it")))),
        new AiTool("click", "Click an element. Take a new snapshot afterwards to see what changed.", Schema(Req("ref", Str(RefHelp)))),
        new AiTool("type", "Type text into a text field, at the caret.", Schema(Req("ref", Str(RefHelp)), Req("text", Str("The text")))),
        new AiTool("set_value", "Replace the whole value of a text field.", Schema(Req("ref", Str(RefHelp)), Req("value", Str("The new value")))),
        new AiTool("press", "Press a key or combination, such as return, escape, tab, ctrl+l or alt+left.",
            Schema(Req("combo", Str("The keys")), Opt("window_id", Str("Send them to this window, from list_windows")))),
        new AiTool("scroll", "Scroll an element, such as a list or a page.",
            Schema(Req("ref", Str(RefHelp)), Opt("direction", new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("up", "down", "left", "right") }),
                Opt("amount", Int("How far, 1 to 100 (default 3)")))),
    };

    /// <summary>The agent-desktop arguments for a call to one of the desktop tools (not report_step or finish).
    /// Throws <see cref="ArgumentException"/> with a message for the AI when a needed value is missing.
    /// Values the AI chose go after "--", so one starting with "-" can't be taken for an option.</summary>
    public static IReadOnlyList<string> Args(ToolCall call, bool realInput)
    {
        var a = call.Arguments;
        var flags = new List<string>();
        var positionals = new List<string>();
        void Opt(string flag, string name) { if (Text(a, name) is { } v) flags.AddRange(new[] { flag, v }); }
        void Num(string flag, string name, int min, int max) { if (Number(a, name) is { } n) flags.AddRange(new[] { flag, Math.Clamp(n, min, max).ToString() }); }
        void Flag(string flag, string name) { if (IsTrue(a, name)) flags.Add(flag); }
        void Headed() { if (realInput) flags.Add("--headed"); }
        string Need(string name) => Text(a, name) ?? throw new ArgumentException($"\"{name}\" is required.");
        // Typed text keeps its spaces.
        string NeedRaw(string name) => a[name] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : throw new ArgumentException($"\"{name}\" is required.");
        string command;
        switch (call.Name)
        {
            case "list_apps": command = "list-apps"; break;
            case "list_windows": command = "list-windows"; Opt("--app", "app"); break;
            case "snapshot":
                command = "snapshot";
                Opt("--app", "app"); Opt("--window-id", "window_id");
                if (a["interactive_only"] is not JsonValue v || !v.TryGetValue<bool>(out var only) || only) flags.Add("-i");
                Num("--max-depth", "max_depth", 1, 30); flags.Add("--compact"); Flag("--skeleton", "skeleton"); Opt("--root", "root");
                break;
            case "find":
                command = "find";
                Opt("--app", "app"); Opt("--window-id", "window_id"); Opt("--role", "role"); Opt("--name", "name"); Opt("--text", "text");
                Flag("--first", "first"); Num("--limit", "limit", 1, 200);
                break;
            case "get": command = "get"; Opt("--property", "property"); positionals.Add(Need("ref")); break;
            case "wait":
                command = "wait";
                Opt("--text", "text"); Opt("--window", "window"); Opt("--app", "app"); Num("--timeout", "timeout_ms", 100, 60000);
                if (Number(a, "ms") is { } ms) positionals.Add(Math.Clamp(ms, 0, 30000).ToString());
                if (positionals.Count == 0 && flags.Count == 0) throw new ArgumentException("Give ms, text or window.");
                break;
            case "launch": command = "launch"; Flag("--activate", "activate"); Headed(); positionals.Add(Need("app")); break;
            case "click": command = "click"; Headed(); positionals.Add(Need("ref")); break;
            case "type": command = "type"; Headed(); positionals.Add(Need("ref")); positionals.Add(NeedRaw("text")); break;
            case "set_value": command = "set-value"; positionals.Add(Need("ref")); positionals.Add(NeedRaw("value")); break;
            case "press": command = "press"; Opt("--window-id", "window_id"); Headed(); positionals.Add(Need("combo")); break;
            case "scroll":
                command = "scroll";
                Opt("--direction", "direction"); Num("--amount", "amount", 1, 100); positionals.Add(Need("ref"));
                break;
            default: throw new ArgumentException($"There is no tool called \"{call.Name}\".");
        }
        var args = new List<string> { command };
        args.AddRange(flags);
        if (positionals.Count > 0) { args.Add("--"); args.AddRange(positionals); }
        return args;
    }

    /// <summary>Everyday names for programs whose file is called something else.</summary>
    static readonly Dictionary<string, string> ProgramAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["edge"] = "msedge", ["microsoft edge"] = "msedge", ["google chrome"] = "chrome", ["firefox"] = "firefox", ["mozilla firefox"] = "firefox",
        ["word"] = "winword", ["microsoft word"] = "winword", ["excel"] = "excel", ["microsoft excel"] = "excel",
        ["powerpoint"] = "powerpnt", ["microsoft powerpoint"] = "powerpnt", ["outlook"] = "outlook", ["microsoft outlook"] = "outlook",
        ["file explorer"] = "explorer", ["files"] = "explorer", ["calculator"] = "calc", ["paint"] = "mspaint",
    };

    /// <summary>The program's file name without ".exe" ("Microsoft Edge" and "C:\...\msedge.exe" both give "msedge"),
    /// which is also how agent-desktop names its app.</summary>
    public static string ProgramName(string app)
    {
        app = app.Trim().Trim('"');
        if (ProgramAliases.TryGetValue(app, out var alias)) return alias;
        var file = app[(app.LastIndexOfAny(new[] { '\\', '/' }) + 1)..];      // both slashes, whichever system this runs on
        return file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? file[..^4] : file;
    }

    /// <summary>A line for the log saying what a call did, without what it read or typed (it can be private).</summary>
    public static string Describe(ToolCall call)
    {
        var a = call.Arguments;
        string? T(string name) => Text(a, name);
        return call.Name switch
        {
            "click" or "get" or "scroll" => $"{call.Name} {T("ref")}",
            "type" => $"type {T("text")?.Length ?? 0} characters into {T("ref")}",
            "set_value" => $"set the value of {T("ref")} ({T("value")?.Length ?? 0} characters)",
            "press" => $"press {T("combo")}",
            "launch" => $"open {T("app")}",
            FindProgram => $"find program {T("query")}",
            TypeText => $"type {T("text")?.Length ?? 0} characters into window {T("window_id")}" + (IsTrue(a, "enter") ? " and press Enter" : "")
                + (T("keys") is { } keys ? $" and press {keys}" : ""),
            ReadText => $"read the text of window {T("window_id")}",
            "snapshot" or "list_windows" => T("app") is { } app ? $"{call.Name} of {app}" : call.Name,
            "find" => "find " + string.Join(" ", new[] { T("role"), T("name"), T("text") }.Where(x => x is not null)),
            _ => call.Name,
        };
    }

    /// <summary>Output cut to <see cref="MaxOutputChars"/>, saying so and how to read less.</summary>
    public static string Shorten(string text) => text.Length <= MaxOutputChars ? text
        : text[..MaxOutputChars] + $"\n...[cut: {text.Length - MaxOutputChars} more characters. Read less: use find, root, max_depth or skeleton]";

    internal static string? Text(JsonObject a, string name)
    {
        if (a[name] is not JsonValue v) return null;
        var s = v.TryGetValue<string>(out var str) ? str : v.ToJsonString();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    internal static int? Number(JsonObject a, string name)
    {
        if (a[name] is not JsonValue v) return null;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<double>(out var d) && !double.IsNaN(d)) return (int)Math.Clamp(d, int.MinValue, int.MaxValue);
        return v.TryGetValue<string>(out var s) && int.TryParse(s, out var p) ? p : null;
    }

    internal static bool IsTrue(JsonObject a, string name) =>
        a[name] is JsonValue v && ((v.TryGetValue<bool>(out var b) && b) || (v.TryGetValue<string>(out var s) && s.Equals("true", StringComparison.OrdinalIgnoreCase)));
}
