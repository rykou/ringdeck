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
