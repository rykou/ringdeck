namespace RingDeck.Core.Tests;

public class RingStyleTests
{
    [Fact]
    public void RingsUseTheGlobalStyleUnlessTheyPickTheirOwn()
    {
        Assert.Equal(RingStyle.Classic, new GlobalSettings().RingStyle);
        Assert.Null(new Ring().Style);
        var config = ConfigStore.FromJson("""{ "settings": { "ringStyle": "neon" }, "rings": [ {}, { "style": "labels" } ] }""")!;
        Assert.Equal(RingStyle.Neon, config.Settings.RingStyle);
        Assert.Equal(RingStyle.Neon, config.Rings[0].StyleIn(config.Settings));
        Assert.Equal(RingStyle.Labels, config.Rings[1].StyleIn(config.Settings));
    }

    [Fact]
    public void StyleSurvivesSaving()
    {
        var json = ConfigStore.ToJson(new AppConfig { Settings = { RingStyle = RingStyle.Soft }, Rings = { new Ring { Style = RingStyle.Clean } } });
        Assert.Contains("\"ringStyle\": \"soft\"", json);
        var back = ConfigStore.FromJson(json)!;
        Assert.Equal(RingStyle.Soft, back.Settings.RingStyle);
        Assert.Equal(RingStyle.Clean, back.Rings[0].Style);
    }

    [Fact]
    public void AnUnknownStyleFallsBackToClassic()
    {
        var config = ConfigStore.FromJson("""{ "settings": { "ringStyle": 99 }, "rings": [ { "style": 42 } ] }""")!;
        Assert.Equal(RingStyle.Classic, config.Settings.RingStyle);
        Assert.Null(config.Rings[0].Style);
    }
}

public class RingIconTests
{
    [Fact]
    public void RingIconsAreSavedAndImportedLikeSliceIcons()
    {
        var back = ConfigStore.FromJson(ConfigStore.ToJson(new AppConfig { Rings = { new Ring { Icon = "emoji:1f680" } } }))!;
        Assert.Equal("emoji:1f680", back.Rings[0].Icon);
        Assert.Equal("", ConfigStore.FromJson("""{ "rings": [ { "icon": null } ] }""")!.Rings[0].Icon);

        // pictures are not part of an export, so an imported ring drops a picture icon but keeps an emoji
        var target = new AppConfig();
        var added = ConfigTransfer.Import(target, new AppConfig { Rings = { new Ring { Icon = "file:abc.png" }, new Ring { Icon = "emoji:1f680" } } });
        Assert.Equal("", added[0].Icon);
        Assert.Equal("emoji:1f680", added[1].Icon);
    }
}
