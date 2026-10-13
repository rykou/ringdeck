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

    static AppConfig Normalise(AppConfig config)
    {
        if (config.Version < 1) config.Version = 1;
        config.Settings ??= new GlobalSettings();
        config.Settings.Scale = Math.Clamp(config.Settings.Scale, 0.5, 2.0);
        config.Rings = (config.Rings ?? new()).Where(r => r is not null).ToList();
        var seen = new HashSet<Guid>();
        foreach (var ring in config.Rings)
        {
            if (ring.Id == Guid.Empty || !seen.Add(ring.Id)) { ring.Id = Guid.NewGuid(); seen.Add(ring.Id); }
            ring.Name ??= "";
            ring.Shortcut ??= "";
            ring.Radius = Math.Clamp(ring.Radius, 60, 400);
            ring.Slices = (ring.Slices ?? new()).Where(s => s is not null).ToList();
            foreach (var slice in ring.Slices)
            {
                if (slice.Id == Guid.Empty) slice.Id = Guid.NewGuid();
                slice.Label ??= "";
                slice.Icon ??= "";
                if (slice.Action is MacroAction macro)
                    macro.Steps = (macro.Steps ?? new()).Where(s => s is not null).ToList();
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
