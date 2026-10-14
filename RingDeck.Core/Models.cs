using System.Text.Json.Serialization;

namespace RingDeck.Core;

public enum OpenMode { Hold, Click }

/// <summary>Where a ring appears: at the mouse pointer, or in the middle of the screen (the pointer moves there).</summary>
public enum OpenAt { Pointer, Center }

/// <summary>How rings are drawn.</summary>
public enum RingStyle { Classic, Soft, Clean, Neon, Labels }

public sealed class AppConfig
{
    public int Version { get; set; } = 1;
    public GlobalSettings Settings { get; set; } = new();
    public List<Ring> Rings { get; set; } = new();
    public List<AiConnection> AiConnections { get; set; } = new();

    public Ring? Find(Guid id) => Rings.FirstOrDefault(r => r.Id == id);
    public AiConnection? FindConnection(Guid id) => AiConnections.FirstOrDefault(c => c.Id == id);
}

public sealed class GlobalSettings
{
    public string Theme { get; set; } = "system";     // system | dark | light
    public string Accent { get; set; } = "#4CC2FF";
    public RingStyle RingStyle { get; set; } = RingStyle.Classic;
    public double Scale { get; set; } = 1.0;          // 0.5 .. 2.0
    public bool Animations { get; set; } = true;
    public bool SpiralClose { get; set; } = true;
    public bool InstantPointer { get; set; }
    /// <summary>A soft sound when a ring opens and when a slice is chosen. Off unless the user turns it on.</summary>
    public bool Sounds { get; set; }
    public double SoundVolume { get; set; } = 0.5;
    /// <summary>Copy config.json into backups\ whenever RingDeck starts as a new version.</summary>
    public bool BackupOnUpdate { get; set; } = true;
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
    public OpenAt OpenAt { get; set; } = OpenAt.Pointer;
    public double Radius { get; set; } = 120;
    public string Color { get; set; } = "#1E1E24";
    public string Accent { get; set; } = "";          // empty = global accent
    /// <summary>Empty: the style chosen in Settings.</summary>
    public RingStyle? Style { get; set; }
    public RingStyle StyleIn(GlobalSettings settings) => Style ?? settings.RingStyle;
    public bool ShowLabels { get; set; } = true;
    public bool Enabled { get; set; } = true;
    /// <summary>Programs (exe names) this ring is for. Empty: everywhere.</summary>
    public List<string> OnlyIn { get; set; } = new();
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
[JsonDerivedType(typeof(AiChatAction), "ai")]
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
[JsonDerivedType(typeof(PasteStep), "paste")]
[JsonDerivedType(typeof(LaunchStep), "open")]
[JsonDerivedType(typeof(FocusStep), "focus")]
[JsonDerivedType(typeof(ClipboardStep), "clipboard")]
public abstract class MacroStep { }

public sealed class KeyStep : MacroStep { public string Combo { get; set; } = ""; }
public sealed class TextStep : MacroStep { public string Text { get; set; } = ""; }
public sealed class DelayStep : MacroStep { public int Ms { get; set; } = 100; }
/// <summary>Pastes text through the clipboard (placeholders expanded, clipboard restored).</summary>
public sealed class PasteStep : MacroStep { public string Text { get; set; } = ""; }
/// <summary>Opens an app, file, folder or link.</summary>
public sealed class LaunchStep : MacroStep { public string Target { get; set; } = ""; public string Arguments { get; set; } = ""; }
/// <summary>Brings a window to the front: by program (exe name), by part of its title, or both.</summary>
public sealed class FocusStep : MacroStep { public string Program { get; set; } = ""; public string Title { get; set; } = ""; }
/// <summary>Puts text on the clipboard and leaves it there.</summary>
public sealed class ClipboardStep : MacroStep { public string Text { get; set; } = ""; }

public enum AiFormat { OpenAi, Anthropic, Gemini, Custom }

public enum AiInput { Selection, Clipboard, None }

/// <summary>An AI service a slice can talk to. Secret values (the key, secret headers) are stored encrypted,
/// as "dpapi:..." strings; see ISecretProtector.</summary>
public sealed class AiConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New connection";
    public AiFormat Format { get; set; } = AiFormat.OpenAi;
    public string Endpoint { get; set; } = "";
    public string Model { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public List<AiHeader> Headers { get; set; } = new();
    public double? Temperature { get; set; }
    public int? MaxTokens { get; set; }
    /// <summary>Custom format only: the request body, with {{model}} {{prompt}} {{system}} {{api_key}} {{messages}}.</summary>
    public string BodyTemplate { get; set; } = "";
    /// <summary>Custom format only: where the answer is in the response, e.g. choices[0].message.content.</summary>
    public string ResponsePath { get; set; } = "";
}

public sealed class AiHeader
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public bool Secret { get; set; }
}

/// <summary>Opens the chat panel on a connection, optionally starting from the selected text and a preset instruction.</summary>
public sealed class AiChatAction : SliceAction
{
    public Guid ConnectionId { get; set; }
    public string Instruction { get; set; } = "";
    public string SystemPrompt { get; set; } = "";
    public AiInput Input { get; set; } = AiInput.Selection;
    public bool SendImmediately { get; set; } = true;
    /// <summary>False: no chat window; the answer goes to the clipboard and a notification says so.</summary>
    public bool ShowChat { get; set; } = true;
}
