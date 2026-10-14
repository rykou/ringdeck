namespace RingDeck.Core.Tests;

public class ConfigTransferTests
{
    static AppConfig Build(out Ring main, out Ring child, out Ring other)
    {
        child = new Ring { Name = "child", Shortcut = "Ctrl+1" };
        main = new Ring { Name = "main", Shortcut = "Ctrl+2", Slices =
        {
            new Slice { Label = "n", Action = new NestedRingAction { RingId = child.Id } },
            new Slice { Label = "img", Icon = "file:abc.png", Action = new PasteTextAction { Text = "t" } },
        } };
        other = new Ring { Name = "other", Shortcut = "Ctrl+3" };
        return new AppConfig { Rings = { main, child, other } };
    }

    [Fact]
    public void ExportTakesTheRingAndWhatItNests()
    {
        var config = Build(out var main, out var child, out _);
        var exported = ConfigTransfer.Export(config, main.Id);
        Assert.Equal(new[] { main.Id, child.Id }, exported.Rings.Select(r => r.Id));
        Assert.NotSame(main, exported.Rings[0]);
    }

    [Fact]
    public void ImportGivesFreshIdsRemapsNestingAndClearsTakenShortcuts()
    {
        var config = Build(out var main, out var child, out _);
        var added = ConfigTransfer.Import(config, ConfigTransfer.Export(config, main.Id));
        Assert.Equal(5, config.Rings.Count);
        Assert.Equal(2, added.Count);
        Assert.Equal(5, config.Rings.Select(r => r.Id).Distinct().Count());
        Assert.All(added, r => Assert.Equal("", r.Shortcut));
        var nested = Assert.IsType<NestedRingAction>(added[0].Slices[0].Action);
        Assert.Equal(added[1].Id, nested.RingId);
        Assert.Equal("", added[0].Slices[1].Icon);       // image files are not carried across
        Assert.Equal(child.Id, ((NestedRingAction)main.Slices[0].Action!).RingId);   // original untouched
    }

    [Fact]
    public void ImportKeepsAFreeShortcut()
    {
        var target = new AppConfig();
        var added = ConfigTransfer.Import(target, new AppConfig { Rings = { new Ring { Shortcut = "Ctrl+9" }, new Ring { Shortcut = "Ctrl+9" } } });
        Assert.Equal("Ctrl+9", added[0].Shortcut);
        Assert.Equal("", added[1].Shortcut);
    }

    [Fact]
    public void DuplicateIsADeepCopyWithNewIdsAndNoShortcut()
    {
        Build(out var main, out _, out _);
        var copy = ConfigTransfer.Duplicate(main);
        Assert.NotEqual(main.Id, copy.Id);
        Assert.NotEqual(main.Slices[0].Id, copy.Slices[0].Id);
        Assert.Equal("", copy.Shortcut);
        Assert.Equal("main copy", copy.Name);
        ((PasteTextAction)copy.Slices[1].Action!).Text = "changed";
        Assert.Equal("t", ((PasteTextAction)main.Slices[1].Action!).Text);
        Assert.Equal("file:abc.png", copy.Slices[1].Icon);
    }

    [Fact]
    public void DuplicateSliceIsADeepCopyWithANewId()
    {
        var original = new Slice
        {
            Label = "Rewrite", Icon = "glyph:x", Color = "#123456",
            Action = new MacroAction { Steps = { new KeyStep { Combo = "Ctrl+A" }, new TextStep { Text = "hi" } } },
        };
        var copy = ConfigTransfer.DuplicateSlice(original);
        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal("Rewrite copy", copy.Label);
        Assert.Equal("glyph:x", copy.Icon);
        Assert.Equal("#123456", copy.Color);
        var steps = Assert.IsType<MacroAction>(copy.Action).Steps;
        Assert.Equal(2, steps.Count);
        ((TextStep)steps[1]).Text = "changed";
        Assert.Equal("hi", ((TextStep)((MacroAction)original.Action!).Steps[1]).Text);   // independent copy
    }

    [Fact]
    public void DuplicateSliceKeepsAiSettingsAndConnection()
    {
        var id = Guid.NewGuid();
        var copy = ConfigTransfer.DuplicateSlice(new Slice { Label = "Ask", Action = new AiChatAction { ConnectionId = id, Instruction = "Fix", ShowChat = false } });
        var ai = Assert.IsType<AiChatAction>(copy.Action);
        Assert.Equal(id, ai.ConnectionId);
        Assert.Equal("Fix", ai.Instruction);
        Assert.False(ai.ShowChat);
    }

    [Fact]
    public void DuplicateSliceWithoutActionOrLabel()
    {
        var copy = ConfigTransfer.DuplicateSlice(new Slice { Label = "", Action = null });
        Assert.Null(copy.Action);
        Assert.Equal("Copy", copy.Label);
    }
}
