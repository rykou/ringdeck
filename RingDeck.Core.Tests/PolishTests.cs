namespace RingDeck.Core.Tests;

public class PolishTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "ringdeck-backup-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Fact]
    public void NewSettingsHaveSafeDefaults()
    {
        Assert.Equal(OpenAt.Pointer, new Ring().OpenAt);
        Assert.False(new GlobalSettings().Sounds);
        Assert.Equal(0.5, new GlobalSettings().SoundVolume);
        Assert.True(new GlobalSettings().BackupOnUpdate);
        Assert.False(ConfigStore.FromJson("""{ "settings": { "backupOnUpdate": false } }""")!.Settings.BackupOnUpdate);
        var config = ConfigStore.FromJson("""{ "settings": { "soundVolume": 7 }, "rings": [ { "openAt": "center" } ] }""")!;
        Assert.Equal(1.0, config.Settings.SoundVolume);
        Assert.Equal(OpenAt.Center, config.Rings[0].OpenAt);
    }

    [Fact]
    public void BackupHappensOnlyWhenTheVersionChanges()
    {
        var store = new ConfigStore(_dir);
        store.Save(new AppConfig { Rings = { new Ring { Name = "mine" } } });
        Assert.Null(store.BackupOnVersionChange("0.3.3", new DateTime(2026, 10, 14)));                 // first run of a version: just remembered
        Assert.Null(store.BackupOnVersionChange("0.3.3", new DateTime(2026, 10, 15)));                 // same version: nothing
        var file = store.BackupOnVersionChange("0.4.0", new DateTime(2026, 10, 16, 9, 30, 0));
        Assert.NotNull(file);
        Assert.Equal("config-0.3.3-20261016-093000.json", Path.GetFileName(file));
        Assert.Contains("\"mine\"", File.ReadAllText(file!));
        Assert.Null(store.BackupOnVersionChange("0.4.0", new DateTime(2026, 10, 17)));
    }

    [Fact]
    public void KeepsOnlyTheLastTenBackups()
    {
        var store = new ConfigStore(_dir);
        store.Save(new AppConfig());
        store.BackupOnVersionChange("1.0.0", new DateTime(2026, 1, 1));
        for (var i = 1; i <= 13; i++) store.BackupOnVersionChange($"1.0.{i}", new DateTime(2026, 1, 1).AddDays(i));
        var files = Directory.GetFiles(Path.Combine(_dir, "backups"), "config-*.json").Select(Path.GetFileName).Order().ToList();
        Assert.Equal(10, files.Count);
        Assert.DoesNotContain(files, f => f!.StartsWith("config-1.0.0-") || f.StartsWith("config-1.0.1-") || f.StartsWith("config-1.0.2-"));
    }

    [Fact]
    public void NoConfigFileMeansNoBackup()
    {
        var store = new ConfigStore(_dir);
        store.BackupOnVersionChange("1.0.0", DateTime.Now);
        Assert.Null(store.BackupOnVersionChange("2.0.0", DateTime.Now));
    }

    [Theory]
    [InlineData(0, 0, 1920, 1080, 960, 540)]
    [InlineData(1920, 0, 3840, 1040, 2880, 520)]
    [InlineData(-1280, 100, 0, 1124, -640, 612)]
    public void CentreOfTheWorkArea(int left, int top, int right, int bottom, int x, int y) =>
        Assert.Equal((x, y), RingGeometry.Centre(left, top, right, bottom));
}
