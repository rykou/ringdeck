namespace RingDeck.Core.Tests;

public class SuppressionTests
{
    static GlobalSettings Settings(bool fullscreen, params string[] apps) =>
        new() { DisableInFullscreen = fullscreen, ExcludedApps = apps.ToList() };

    [Fact] public void FullscreenAppTurnsRingsOff() => Assert.True(Suppression.Applies(Settings(true), new ForegroundApp("Game.exe", true)));
    [Fact] public void FullscreenRuleCanBeSwitchedOff() => Assert.False(Suppression.Applies(Settings(false), new ForegroundApp("Game.exe", true)));
    [Fact] public void WindowedAppIsLeftAlone() => Assert.False(Suppression.Applies(Settings(true), new ForegroundApp("notepad.exe", false)));
    [Fact] public void UnknownForegroundWindowIsLeftAlone() => Assert.False(Suppression.Applies(Settings(true, "Wow.exe"), new ForegroundApp(null, false)));
    [Fact] public void FullscreenStillCountsWhenTheProgramIsUnknown() => Assert.True(Suppression.Applies(Settings(true), new ForegroundApp(null, true)));

    [Theory]
    [InlineData("Wow.exe", "Wow.exe")]
    [InlineData("wow", "Wow.exe")]
    [InlineData("WOW.EXE", "wow.exe")]
    [InlineData(" Wow.exe ", "Wow.exe")]
    [InlineData(@"C:\Games\World of Warcraft\Wow.exe", "Wow.exe")]
    [InlineData("Wow.exe", @"C:\Games\World of Warcraft\Wow.exe")]
    public void ListedProgramTurnsRingsOffEvenWindowed(string entry, string running) =>
        Assert.True(Suppression.Applies(Settings(false, "other.exe", entry), new ForegroundApp(running, false)));

    [Theory]
    [InlineData("Wow.exe", "WowClassic.exe")]
    [InlineData("", "Wow.exe")]
    [InlineData(".exe", "Wow.exe")]
    public void OtherProgramsAreNotMatched(string entry, string running) =>
        Assert.False(Suppression.Applies(Settings(false, entry), new ForegroundApp(running, false)));
}
