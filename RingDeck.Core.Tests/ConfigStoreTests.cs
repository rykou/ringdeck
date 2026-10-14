namespace RingDeck.Core.Tests;

public class ConfigStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "ringdeck-tests-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Fact]
    public void FirstRunCreatesSampleRing()
    {
        var result = new ConfigStore(_dir).Load();
        Assert.True(result.FirstRun);
        Assert.NotEmpty(result.Config.Rings);
        Assert.NotEmpty(result.Config.Rings[0].Slices);
        Assert.True(Shortcut.TryParse(result.Config.Rings[0].Shortcut, out _));
        Assert.True(File.Exists(Path.Combine(_dir, "config.json")));
    }

    [Fact]
    public void RoundTripsEveryActionType()
    {
        var store = new ConfigStore(_dir);
        var target = new Ring { Name = "Child" };
        var config = new AppConfig { Rings = { target, new Ring { Name = "Main", Shortcut = "Ctrl+Alt+Q", Mode = OpenMode.Click, Slices =
        {
            new Slice { Label = "p", Action = new PasteTextAction { Text = "hi {date}", RestoreClipboard = false } },
            new Slice { Label = "m", Action = new MacroAction { Steps = { new KeyStep { Combo = "Ctrl+C" }, new TextStep { Text = "x" }, new DelayStep { Ms = 5 } } } },
            new Slice { Label = "l", Action = new LaunchAction { Target = "notepad.exe", Arguments = "a.txt", WorkingFolder = "C:\\" } },
            new Slice { Label = "n", Action = new NestedRingAction { RingId = target.Id } },
        } } } };
        store.Save(config);
        var loaded = store.Load();
        Assert.False(loaded.FirstRun);
        Assert.False(loaded.Recovered);
        Assert.Equal(ConfigStore.ToJson(config), ConfigStore.ToJson(loaded.Config));
        var main = loaded.Config.Rings[1];
        Assert.Equal(OpenMode.Click, main.Mode);
        Assert.IsType<PasteTextAction>(main.Slices[0].Action);
        Assert.Equal(3, Assert.IsType<MacroAction>(main.Slices[1].Action).Steps.Count);
        Assert.Equal(target.Id, Assert.IsType<NestedRingAction>(main.Slices[3].Action).RingId);
    }

    [Fact]
    public void SecondSaveKeepsBackupOfPrevious()
    {
        var store = new ConfigStore(_dir);
        store.Save(new AppConfig { Rings = { new Ring { Name = "one" } } });
        store.Save(new AppConfig { Rings = { new Ring { Name = "two" } } });
        Assert.Contains("\"one\"", File.ReadAllText(store.BackupPath));
        Assert.Contains("\"two\"", File.ReadAllText(store.ConfigPath));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void CorruptFileFallsBackToBackupAndIsKept()
    {
        var store = new ConfigStore(_dir);
        store.Save(new AppConfig { Rings = { new Ring { Name = "good" } } });
        store.Save(new AppConfig { Rings = { new Ring { Name = "newer" } } });
        File.WriteAllText(store.ConfigPath, "{ not json");
        var result = store.Load();
        Assert.True(result.Recovered);
        Assert.Equal("good", result.Config.Rings[0].Name);
        Assert.NotNull(result.BrokenPath);
        Assert.Equal("{ not json", File.ReadAllText(result.BrokenPath!));
    }

    [Fact]
    public void RecoveredConfigSurvivesARestart()
    {
        var store = new ConfigStore(_dir);
        store.Save(new AppConfig { Rings = { new Ring { Name = "good" } } });
        store.Save(new AppConfig { Rings = { new Ring { Name = "newer" } } });
        File.WriteAllText(store.ConfigPath, "{ not json");
        Assert.True(store.Load().Recovered);
        var second = new ConfigStore(_dir).Load();
        Assert.False(second.FirstRun);
        Assert.False(second.Recovered);
        Assert.Equal("good", Assert.Single(second.Config.Rings).Name);
        Assert.Contains("\"good\"", File.ReadAllText(store.BackupPath));
    }

    [Fact]
    public void CorruptFileWithoutBackupGivesEmptyConfig()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "config.json"), "[1,2");
        var result = new ConfigStore(_dir).Load();
        Assert.True(result.Recovered);
        Assert.Empty(result.Config.Rings);
    }

    [Fact]
    public void HandEditedFileIsNormalised()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "config.json"), """
            { "rings": [ { "name": "bare" },
                         { "name": "x", "slices": [ { "label": "no action" }, null,
                           { "label": "late type", "action": { "text": "t", "type": "paste" } } ] } ] }
            """);
        var result = new ConfigStore(_dir).Load();
        Assert.False(result.Recovered);
        Assert.Equal(1, result.Config.Version);
        Assert.NotNull(result.Config.Settings);
        Assert.Empty(result.Config.Rings[0].Slices);
        Assert.NotEqual(Guid.Empty, result.Config.Rings[0].Id);
        Assert.NotEqual(result.Config.Rings[0].Id, result.Config.Rings[1].Id);
        Assert.Equal(2, result.Config.Rings[1].Slices.Count);
        Assert.Null(result.Config.Rings[1].Slices[0].Action);
        Assert.IsType<PasteTextAction>(result.Config.Rings[1].Slices[1].Action);
    }

    [Fact]
    public void GameSettingsDefaultOnAndNormalise()
    {
        Assert.True(new GlobalSettings().DisableInFullscreen);
        Assert.Empty(new GlobalSettings().ExcludedApps);
        var config = ConfigStore.FromJson("""{ "settings": { "disableInFullscreen": false, "excludedApps": [" Wow.exe ", "", null, "wow.exe", "Game.exe"] } }""")!;
        Assert.False(config.Settings.DisableInFullscreen);
        Assert.Equal(new[] { "Wow.exe", "Game.exe" }, config.Settings.ExcludedApps);
        Assert.Empty(ConfigStore.FromJson("""{ "settings": { "excludedApps": null } }""")!.Settings.ExcludedApps);
        Assert.True(ConfigStore.FromJson("""{ "settings": { } }""")!.Settings.DisableInFullscreen);   // configs from 0.1.x gain the default
    }

    [Fact]
    public void OutOfRangeSettingsAreClamped()
    {
        var config = ConfigStore.FromJson("""{ "settings": { "scale": 99 }, "rings": [ { "radius": -5 } ] }""")!;
        Assert.Equal(2.0, config.Settings.Scale);
        Assert.Equal(60, config.Rings[0].Radius);
    }

    [Fact]
    public void NewerVersionNumberIsNotDowngradedOnSave()
    {
        var store = new ConfigStore(_dir);
        var config = new AppConfig { Version = 7 };
        store.Save(config);
        Assert.Equal(7, store.Load().Config.Version);
    }
}
