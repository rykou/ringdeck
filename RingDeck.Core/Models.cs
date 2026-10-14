using System.Text.Json.Serialization;

namespace RingDeck.Core;

public enum OpenMode { Hold, Click }

public sealed class AppConfig
{
    public int Version { get; set; } = 1;
    public GlobalSettings Settings { get; set; } = new();
    public List<Ring> Rings { get; set; } = new();

    public Ring? Find(Guid id) => Rings.FirstOrDefault(r => r.Id == id);
}

public sealed class GlobalSettings
{
    public string Theme { get; set; } = "system";     // system | dark | light
    public string Accent { get; set; } = "#4CC2FF";
    public double Scale { get; set; } = 1.0;          // 0.5 .. 2.0
    public bool Animations { get; set; } = true;
    public bool SpiralClose { get; set; } = true;
    public bool InstantPointer { get; set; }
    /// <summary>Ring shortcuts are ignored while a fullscreen app (typically a game) has focus.</summary>
    public bool DisableInFullscreen { get; set; } = true;
    /// <summary>Programs (exe names) in which ring shortcuts are always ignored.</summary>
    public List<string> ExcludedApps { get; set; } = new();
}

public sealed class Ring
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New ring";
    public string Shortcut { get; set; } = "";
    public OpenMode Mode { get; set; } = OpenMode.Hold;
    public double Radius { get; set; } = 120;
    public string Color { get; set; } = "#1E1E24";
    public string Accent { get; set; } = "";          // empty = global accent
    public bool ShowLabels { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public List<Slice> Slices { get; set; } = new();
}

public sealed class Slice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Label { get; set; } = "New slice";
    public string Icon { get; set; } = "";
    public string Color { get; set; } = "#2D2D3A";
    public SliceAction? Action { get; set; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(PasteTextAction), "paste")]
[JsonDerivedType(typeof(MacroAction), "macro")]
[JsonDerivedType(typeof(LaunchAction), "launch")]
[JsonDerivedType(typeof(NestedRingAction), "ring")]
public abstract class SliceAction { }

public sealed class PasteTextAction : SliceAction
{
    public string Text { get; set; } = "";
    public bool RestoreClipboard { get; set; } = true;
}

public sealed class MacroAction : SliceAction
{
    public List<MacroStep> Steps { get; set; } = new();
}

public sealed class LaunchAction : SliceAction
{
    public string Target { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkingFolder { get; set; } = "";
}

public sealed class NestedRingAction : SliceAction
{
    public Guid RingId { get; set; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(KeyStep), "key")]
[JsonDerivedType(typeof(TextStep), "text")]
[JsonDerivedType(typeof(DelayStep), "delay")]
public abstract class MacroStep { }

public sealed class KeyStep : MacroStep { public string Combo { get; set; } = ""; }
public sealed class TextStep : MacroStep { public string Text { get; set; } = ""; }
public sealed class DelayStep : MacroStep { public int Ms { get; set; } = 100; }
