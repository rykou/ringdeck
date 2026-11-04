using System.Diagnostics;
using System.Text;
using System.Text.Json;
using RingDeck.Core;

namespace RingDeck.Windows;

/// <summary>Runs agent-desktop.exe for "Use the computer" steps. Arguments go straight to the process (no shell), so
/// text the AI types can't run commands.</summary>
public sealed class AgentDesktop(string configuredPath) : IAgentDesktop
{
    /// <summary>The longest one command may take; agent-desktop's own waits stop at 60 seconds.</summary>
    static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(75);

    /// <summary>Where the computer-agent installer puts it.</summary>
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "tools", "agent-desktop", "agent-desktop.exe");

    /// <summary>The configured file, else the usual install folder, else agent-desktop.exe on PATH; null when none exists.</summary>
    public static string? Find(string configuredPath)
    {
        var configured = Environment.ExpandEnvironmentVariables(configuredPath.Trim().Trim('"'));
        if (configured.Length > 0)
        {
            if (Directory.Exists(configured)) configured = Path.Combine(configured, "agent-desktop.exe");
            return File.Exists(configured) ? configured : null;
        }
        if (File.Exists(DefaultPath)) return DefaultPath;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim('"'), "agent-desktop.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { }                    // a PATH entry with characters a path can't have
        }
        return null;
    }

    /// <summary>App Paths (per user, then for the machine, where installers register programs such as msedge.exe and
    /// winword.exe), then PATH.</summary>
    public string? FindProgram(string name)
    {
        var file = name.Trim().Trim('"');
        if (file.Length == 0 || file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        if (!file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) file += ".exe";
        foreach (var root in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
        {
            using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + file);
            if (key?.GetValue(null) is string path)
            {
                path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
                if (File.Exists(path)) return path;
            }
        }
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(Environment.ExpandEnvironmentVariables(dir.Trim('"')), file);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    /// <summary>A program's path, a shortcut, or "shell:AppsFolder\..." (any Start menu entry, Store apps included).</summary>
    public bool StartProgram(string path)
    {
        try
        {
            if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = false })?.Dispose();
            else if (File.Exists(path))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) ?? "" })?.Dispose();
            else return false;
            return true;
        }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    IReadOnlyList<InstalledProgram>? _programs;

    /// <summary>The Start menu's "All apps" (the shell's Applications folder, what Get-StartApps lists), read once
    /// per run. Each opens through shell:AppsFolder, which works for Store apps too.</summary>
    public IReadOnlyList<InstalledProgram> InstalledPrograms() => _programs ??= ReadAppsFolder();

    static IReadOnlyList<InstalledProgram> ReadAppsFolder()
    {
        var found = new List<InstalledProgram>();
        // Shell COM objects want an STA thread; use a fresh one so this works from any caller.
        var thread = new Thread(() =>
        {
            try
            {
                var shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType is null) return;
                dynamic shell = Activator.CreateInstance(shellType)!;
                try
                {
                    dynamic? folder = shell.NameSpace("shell:::{4234d49b-0245-4df3-b780-3893943456e1}");
                    if (folder is null) return;
                    foreach (dynamic item in folder.Items())
                    {
                        string name = item.Name, id = item.Path;
                        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(id)) found.Add(new InstalledProgram(name, @"shell:AppsFolder\" + id));
                    }
                }
                finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
            }
            catch (Exception) { }                            // no shell (or it refused): the other ways of finding still work
        }) { IsBackground = true, Name = "RingDeck apps list" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        // Still reading after 15 seconds: give up on the list rather than read it while it's being filled.
        return thread.Join(TimeSpan.FromSeconds(15)) ? found : Array.Empty<InstalledProgram>();
    }

    /// <summary>Program files under Program Files (both) and the user's AppData\Local\Programs, a few folders deep,
    /// whose file name has all the query's words in it. Uninstallers and updaters are left out.</summary>
    public IReadOnlyList<string> SearchProgramFiles(string query)
    {
        var words = ProgramFinderWords(query);
        if (words.Length == 0) return Array.Empty<string>();
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
        }.Where(r => r.Length > 0 && Directory.Exists(r)).Distinct(StringComparer.OrdinalIgnoreCase);
        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 4, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        var hits = new List<string>();
        var stop = Stopwatch.StartNew();
        foreach (var root in roots)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*.exe", options))
                {
                    if (stop.Elapsed > TimeSpan.FromSeconds(10) || hits.Count >= 40) break;
                    var name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                    var folder = Path.GetFileName(Path.GetDirectoryName(file) ?? "").ToLowerInvariant();
                    if (Noise.Any(name.Contains)) continue;
                    if (words.All(w => name.Contains(w) || folder.Contains(w))) hits.Add(file);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        // Best first: the file named most like the words, then the shortest path.
        return hits.OrderByDescending(f => words.Count(w => Path.GetFileNameWithoutExtension(f).Contains(w, StringComparison.OrdinalIgnoreCase)))
            .ThenBy(f => f.Length).ToList();
    }

    /// <summary>The text of a window's text areas (UI Automation's text pattern: Windows Terminal, cmd and PowerShell
    /// consoles, documents), the longest one when there are several. agent-desktop's ids are "w-" plus the
    /// window handle.</summary>
    public string? ReadWindowText(string windowId)
    {
        var digits = windowId.Trim().StartsWith("w-", StringComparison.OrdinalIgnoreCase) ? windowId.Trim()[2..] : windowId.Trim();
        if (!long.TryParse(digits, out var handle) || handle == 0) return null;
        string? text = null;
        // UI Automation calls into the other program and can hang on one that's stuck, so read on a thread of its own.
        var thread = new Thread(() =>
        {
            try
            {
                var root = System.Windows.Automation.AutomationElement.FromHandle(new IntPtr(handle));
                var withText = new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.IsTextPatternAvailableProperty, true);
                string? best = null;
                foreach (System.Windows.Automation.AutomationElement element in root.FindAll(System.Windows.Automation.TreeScope.Subtree, withText))
                {
                    if (element.GetCurrentPattern(System.Windows.Automation.TextPattern.Pattern) is not System.Windows.Automation.TextPattern pattern) continue;
                    var value = pattern.DocumentRange.GetText(-1);
                    if (best is null || value.Length > best.Length) best = value;
                }
                text = best;
            }
            catch (Exception) { }                            // gone, or it refused: say there's nothing to read
        }) { IsBackground = true, Name = "RingDeck window text" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return thread.Join(TimeSpan.FromSeconds(10)) ? text : null;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr hwnd);

    public string? WindowTitle(string windowId)
    {
        var digits = windowId.Trim().StartsWith("w-", StringComparison.OrdinalIgnoreCase) ? windowId.Trim()[2..] : windowId.Trim();
        if (!long.TryParse(digits, out var handle) || handle == 0) return null;
        var hwnd = new IntPtr(handle);
        var text = new StringBuilder(GetWindowTextLength(hwnd) + 1);
        return GetWindowText(hwnd, text, text.Capacity) > 0 ? text.ToString() : null;
    }

    static readonly string[] Noise = { "unins", "uninstall", "setup", "update", "crash", "helper", "installer", "elevation", "notification" };

    static string[] ProgramFinderWords(string query) =>
        query.ToLowerInvariant().Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries).Where(w => w != "exe").ToArray();

    public async Task<DesktopOutput> RunAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        var exe = Find(configuredPath) ?? throw new DesktopMissingException(configuredPath.Trim().Length > 0
            ? $"agent-desktop was not found at {configuredPath.Trim()}. Check its location in Settings."
            : $"agent-desktop is not installed (looked in {Path.GetDirectoryName(DefaultPath)} and on PATH). Install it, or set its location in Settings.");
        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) info.ArgumentList.Add(a);

        using var process = Process.Start(info) ?? throw new InvalidOperationException("Windows did not start agent-desktop.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(CommandTimeout);
        // Read both streams while it runs, so a large snapshot can't fill the pipe and stall it.
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
            if (ct.IsCancellationRequested) throw;
            return new DesktopOutput(false, $"agent-desktop {args.FirstOrDefault()} took longer than {CommandTimeout.TotalSeconds:0} seconds and was stopped.");
        }
        var output = (await stdout).Trim();
        var errors = (await stderr).Trim();
        return new DesktopOutput(Succeeded(output, process.ExitCode), output.Length > 0 ? output : errors.Length > 0 ? errors : $"(no output, exit code {process.ExitCode})");
    }

    /// <summary>It prints one JSON envelope, with "ok": false on failure; anything else goes by the exit code.</summary>
    static bool Succeeded(string output, int exitCode)
    {
        try
        {
            using var doc = JsonDocument.Parse(output);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("ok", out var ok)) return ok.ValueKind != JsonValueKind.False;
        }
        catch (JsonException) { }
        return exitCode == 0;
    }
}
