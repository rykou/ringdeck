namespace RingDeck.Core.Tests;

public class RingPickerTests
{
    static Ring R(string name, string shortcut = "Alt+Q", params string[] onlyIn) => new() { Name = name, Shortcut = shortcut, OnlyIn = onlyIn.ToList() };

    [Fact]
    public void ProgramSpecificRingWinsOverTheGeneralOne()
    {
        var general = R("General");
        var excel = R("Excel", "Alt+Q", "EXCEL.EXE");
        Assert.Same(excel, RingPicker.Pick(new[] { general, excel }, "excel.exe"));
        Assert.Same(general, RingPicker.Pick(new[] { general, excel }, "notepad.exe"));
    }

    [Fact]
    public void NoGeneralRingMeansNothingOutsideTheListedPrograms()
    {
        var excel = R("Excel", "Alt+Q", "excel");
        Assert.Null(RingPicker.Pick(new[] { excel }, "notepad.exe"));
        Assert.Null(RingPicker.Pick(new[] { excel }, null));      // unknown program: only general rings apply
        Assert.Same(excel, RingPicker.Pick(new[] { excel }, @"C:\Office\EXCEL.EXE"));
    }

    [Fact]
    public void FirstInListWinsAmongEquals()
    {
        var a = R("A", "Alt+Q", "code.exe");
        var b = R("B", "Alt+Q", "code.exe", "devenv.exe");
        Assert.Same(a, RingPicker.Pick(new[] { a, b }, "code.exe"));
        Assert.Same(b, RingPicker.Pick(new[] { a, b }, "devenv.exe"));
        var g1 = R("G1"); var g2 = R("G2");
        Assert.Same(g1, RingPicker.Pick(new[] { g1, g2 }, "x.exe"));
    }

    [Fact]
    public void NothingToPickFromGivesNull() => Assert.Null(RingPicker.Pick(Array.Empty<Ring>(), "x.exe"));

    [Fact]
    public void ClashesOnlyWhenRingsReallyCompete()
    {
        var general = R("General");
        var general2 = R("General 2");
        var excel = R("Excel", "Alt+Q", "excel.exe");
        var excel2 = R("Excel 2", "Alt+Q", "EXCEL");
        var word = R("Word", "Alt+Q", "winword.exe");
        var other = R("Other", "Alt+W");
        var off = R("Off"); off.Enabled = false;
        var config = new AppConfig { Rings = { general, general2, excel, excel2, word, other, off } };
        Assert.Same(general2, RingPicker.Clash(config, general));
        Assert.Same(excel2, RingPicker.Clash(config, excel));     // same program, case and .exe ignored
        Assert.Null(RingPicker.Clash(config, word));               // different program: no clash, even with a general ring
        Assert.Null(RingPicker.Clash(config, other));
        Assert.Null(RingPicker.Clash(new AppConfig { Rings = { general, off } }, general));   // disabled rings don't count
        Assert.Null(RingPicker.Clash(config, R("NoKey", "")));
    }

    [Fact]
    public void OnlyInIsNormalisedAndRoundTrips()
    {
        var config = ConfigStore.FromJson("""{ "rings": [ { "name": "x", "onlyIn": [" chrome.exe ", "", null, "CHROME.EXE"] }, { "name": "y" } ] }""")!;
        Assert.Equal(new[] { "chrome.exe" }, config.Rings[0].OnlyIn);
        Assert.Empty(config.Rings[1].OnlyIn);
        Assert.Contains("\"onlyIn\"", ConfigStore.ToJson(config));
    }
}
