using System.Text.Json;
using System.Text.Json.Serialization;

namespace RingDeck.Core;

public sealed record LoadResult(AppConfig Config, bool FirstRun, bool Recovered, string? BrokenPath);

public sealed class ConfigStore
{
    public const int CurrentVersion = 1;

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        AllowOutOfOrderMetadataProperties = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public ConfigStore(string dir) => Dir = dir;

    public string Dir { get; }
    public string ConfigPath => Path.Combine(Dir, "config.json");
    public string BackupPath => Path.Combine(Dir, "config.bak.json");
    public string IconsDir => Path.Combine(Dir, "icons");

    public string BackupsDir => Path.Combine(Dir, "backups");
    string VersionFile => Path.Combine(Dir, "last-version.txt");
    const int BackupsKept = 10;

    /// <summary>When RingDeck starts as a different version than last time, copies config.json into backups\
    /// (named after the version it came from) and keeps the newest ten. Returns the backup's path, or null.</summary>
    public string? BackupOnVersionChange(string version, DateTime now, bool enabled = true)
    {
        Directory.CreateDirectory(Dir);
        var previous = File.Exists(VersionFile) ? File.ReadAllText(VersionFile).Trim() : null;
        if (previous == version) return null;
        string? backup = null;
        if (enabled && previous is { Length: > 0 } && File.Exists(ConfigPath))
        {
            Directory.CreateDirectory(BackupsDir);
            backup = Path.Combine(BackupsDir, $"config-{previous}-{now:yyyyMMdd-HHmmss}.json");
            File.Copy(ConfigPath, backup, true);
            File.SetLastWriteTime(backup, now);
            foreach (var old in new DirectoryInfo(BackupsDir).GetFiles("config-*.json").OrderByDescending(f => f.LastWriteTime).Skip(BackupsKept))
                old.Delete();
        }
        File.WriteAllText(VersionFile, version);
        return backup;
    }

    public static string ToJson(AppConfig config) => JsonSerializer.Serialize(config, Options);

    /// <summary>Parses and normalises a config. Returns null when the text is not a usable config.</summary>
    public static AppConfig? FromJson(string json)
    {
        try
        {
            var config = JsonSerializer.Deserialize<AppConfig>(json, Options);
            return config is null ? null : Normalise(config);
        }
        catch (JsonException) { return null; }
        catch (NotSupportedException) { return null; }
    }

    public LoadResult Load()
    {
        Directory.CreateDirectory(Dir);
        if (!File.Exists(ConfigPath))
        {
            var sample = Sample();
            Save(sample);
            return new LoadResult(sample, true, false, null);
        }
        if (TryRead(ConfigPath) is { } config) return new LoadResult(config, false, false, null);

        var broken = Path.Combine(Dir, $"config.broken-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        File.Move(ConfigPath, broken, true);
        var recovered = TryRead(BackupPath) ?? new AppConfig();
        // Write it back at once, leaving the backup alone: otherwise the next start finds no config.json,
        // takes it for a first run, and the sample ring replaces the user's rings.
        File.WriteAllText(ConfigPath, ToJson(recovered));
        return new LoadResult(recovered, false, true, broken);
    }

    /// <summary>Atomic save: write a temp file, then swap it in, keeping the previous file as the backup.</summary>
    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(Dir);
        config.Version = Math.Max(config.Version, CurrentVersion);
        var temp = ConfigPath + ".tmp";
        File.WriteAllText(temp, ToJson(config));
        if (File.Exists(ConfigPath)) File.Replace(temp, ConfigPath, BackupPath);
        else File.Move(temp, ConfigPath);
    }

    static AppConfig? TryRead(string path)
    {
        try { return File.Exists(path) ? FromJson(File.ReadAllText(path)) : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    static WebRequest Normalise(WebRequest? r)
    {
        r ??= new WebRequest();
        if (!Enum.IsDefined(r.Service)) r.Service = WebService.Custom;
        r.Url ??= ""; r.Token ??= ""; r.Target ??= ""; r.Message ??= ""; r.Title ??= "";
        r.Method ??= "POST"; r.Body ??= ""; r.ContentType ??= "";
        r.Headers = (r.Headers ?? new()).Where(h => h is not null).ToList();
        foreach (var h in r.Headers) { h.Name ??= ""; h.Value ??= ""; }
        return r;
    }

    /// <summary>Every web request in the config: webhook slices and webhook steps of workflows.</summary>
    public static IEnumerable<WebRequest> WebRequests(AppConfig config) =>
        config.Rings.SelectMany(r => r.Slices).SelectMany(s => s.Action switch
        {
            WebhookAction hook => new[] { hook.Request },
            MacroAction macro => macro.Steps.OfType<WebhookStep>().Select(w => w.Request),
            _ => Enumerable.Empty<WebRequest>(),
        });

    static AppConfig Normalise(AppConfig config)
    {
        if (config.Version < 1) config.Version = 1;
        config.Settings ??= new GlobalSettings();
        config.Settings.Scale = Math.Clamp(config.Settings.Scale, 0.5, 2.0);
        config.Settings.SoundVolume = Math.Clamp(config.Settings.SoundVolume, 0, 1);
        if (!Enum.IsDefined(config.Settings.RingStyle)) config.Settings.RingStyle = RingStyle.Classic;
        config.Settings.ExcludedApps = (config.Settings.ExcludedApps ?? new())
            .Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        config.AiConnections = (config.AiConnections ?? new()).Where(c => c is not null).ToList();
        var connectionIds = new HashSet<Guid>();
        foreach (var c in config.AiConnections)
        {
            if (c.Id == Guid.Empty || !connectionIds.Add(c.Id)) { c.Id = Guid.NewGuid(); connectionIds.Add(c.Id); }
            c.Name ??= "";
            c.Endpoint ??= "";
            c.Model ??= "";
            c.ApiKey ??= "";
            c.BodyTemplate ??= "";
            c.ResponsePath ??= "";
            c.Headers = (c.Headers ?? new()).Where(h => h is not null).ToList();
            foreach (var h in c.Headers) { h.Name ??= ""; h.Value ??= ""; }
        }
        config.Rings = (config.Rings ?? new()).Where(r => r is not null).ToList();
        var seen = new HashSet<Guid>();
        foreach (var ring in config.Rings)
        {
            if (ring.Id == Guid.Empty || !seen.Add(ring.Id)) { ring.Id = Guid.NewGuid(); seen.Add(ring.Id); }
            ring.Name ??= "";
            ring.Icon ??= "";
            ring.Shortcut ??= "";
            ring.Radius = Math.Clamp(ring.Radius, 60, 400);
            if (ring.Style is { } style && !Enum.IsDefined(style)) ring.Style = null;
            ring.OnlyIn = (ring.OnlyIn ?? new()).Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            ring.Slices = (ring.Slices ?? new()).Where(s => s is not null).ToList();
            foreach (var slice in ring.Slices)
            {
                if (slice.Id == Guid.Empty) slice.Id = Guid.NewGuid();
                slice.Label ??= "";
                slice.Icon ??= "";
                slice.ConfirmMs = Math.Clamp(slice.ConfirmMs, 0, RingSession.MaxConfirmMs);
                if (slice.Action is AiChatAction ai) { ai.Instruction ??= ""; ai.SystemPrompt ??= ""; }
                if (slice.Action is MacroAction macro)
                    macro.Steps = (macro.Steps ?? new()).Where(s => s is not null).ToList();
                if (slice.Action is WebhookAction hook) hook.Request = Normalise(hook.Request);
                if (slice.Action is MacroAction m)
                    foreach (var step in m.Steps.OfType<WebhookStep>()) step.Request = Normalise(step.Request);
            }
        }
        return config;
    }

    public static AppConfig Sample() => new()
    {
        Rings =
        {
            new Ring
            {
                Name = "Starter ring", Shortcut = "Ctrl+Alt+Space",
                Slices =
                {
                    new Slice { Label = "Today's date", Icon = "glyph:\uE787", Action = new PasteTextAction { Text = "{date:yyyy-MM-dd}" } },
                    new Slice { Label = "Select all + copy", Icon = "glyph:\uE8C8", Action = new MacroAction { Steps = { new KeyStep { Combo = "Ctrl+A" }, new KeyStep { Combo = "Ctrl+C" } } } },
                    new Slice { Label = "Notepad", Icon = "glyph:\uE70F", Action = new LaunchAction { Target = "notepad.exe" } },
                    new Slice { Label = "Signature", Icon = "glyph:\uE8D2", Action = new PasteTextAction { Text = "Best regards,\n" } },
                },
            },
        },
    };
}
