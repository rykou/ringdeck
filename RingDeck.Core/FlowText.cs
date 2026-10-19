namespace RingDeck.Core;

/// <summary>What a flow card is, for its icon and colour.</summary>
public enum FlowCardKind { Start, Stop, Steps, Open, Type, Paste, Keys, Click, Move, Scroll, Wait, Clipboard, Screenshot, Focus, Web }

/// <summary>The words on a flow card: a title, and a one-line summary of what it does.</summary>
public static class FlowText
{
    public static FlowCardKind Kind(FlowNode node) => node.Kind switch
    {
        FlowNodeKind.Start => FlowCardKind.Start,
        FlowNodeKind.Stop => FlowCardKind.Stop,
        _ when node.Steps.Count != 1 => FlowCardKind.Steps,
        _ => node.Steps[0] switch
        {
            LaunchStep => FlowCardKind.Open,
            TextStep => FlowCardKind.Type,
            PasteStep => FlowCardKind.Paste,
            KeyStep or KeyDownStep or KeyUpStep => FlowCardKind.Keys,
            ClickStep or MouseDownStep or MouseUpStep => FlowCardKind.Click,
            MouseMoveStep => FlowCardKind.Move,
            ScrollStep => FlowCardKind.Scroll,
            DelayStep => FlowCardKind.Wait,
            ClipboardStep => FlowCardKind.Clipboard,
            ScreenshotStep => FlowCardKind.Screenshot,
            FocusStep => FlowCardKind.Focus,
            WebhookStep => FlowCardKind.Web,
            _ => FlowCardKind.Steps,
        },
    };

    public static string Title(FlowNode node)
    {
        if (node.Kind == FlowNodeKind.Start) return "Start";
        if (node.Kind == FlowNodeKind.Stop) return "Stop";
        if (node.Title.Trim().Length > 0) return node.Title.Trim();
        return Kind(node) switch
        {
            FlowCardKind.Open => "Open", FlowCardKind.Type => "Type text", FlowCardKind.Paste => "Paste text",
            FlowCardKind.Keys => "Key press", FlowCardKind.Click => "Click", FlowCardKind.Move => "Mouse move",
            FlowCardKind.Scroll => "Scroll", FlowCardKind.Wait => "Wait", FlowCardKind.Clipboard => "Set clipboard",
            FlowCardKind.Screenshot => "Screenshot", FlowCardKind.Focus => "Focus window",
            FlowCardKind.Web => "Send to " + Webhooks.Name(((WebhookStep)node.Steps[0]).Request.Service),
            _ => "Steps",
        };
    }

    public static string Summary(FlowNode node)
    {
        if (node.Kind == FlowNodeKind.Start) return "Runs when the slice is chosen";
        if (node.Kind == FlowNodeKind.Stop) return "The end of a branch";
        return node.Steps.Count switch
        {
            0 => "Empty: add steps",
            1 => Step(node.Steps[0]),
            var n => $"{n} steps · {MacroView.TotalMs(node.Steps) / 1000.0:0.0} s",
        };
    }

    static string Line(string? text, string empty)
    {
        var t = (text ?? "").ReplaceLineEndings(" ").Trim();
        return t.Length == 0 ? empty : t.Length > 48 ? t[..47] + "…" : t;
    }

    public static string Step(MacroStep step) => step switch
    {
        LaunchStep s => Line(s.Target, "Nothing to open yet"),
        TextStep s => Line(s.Text, "No text yet"),
        PasteStep s => Line(s.Text, "No text yet"),
        KeyStep s => Line(s.Combo, "No key yet"),
        KeyDownStep s => Line(s.Key, "?") + " down",
        KeyUpStep s => Line(s.Key, "?") + " up",
        ClickStep s => $"{s.Button} button",
        MouseDownStep s => $"{s.Button} button down",
        MouseUpStep s => $"{s.Button} button up",
        MouseMoveStep s => MousePath.Describe(s.Path),
        ScrollStep s => $"{s.Direction}, {s.Notches} notches",
        DelayStep s => $"{s.Ms} ms",
        ClipboardStep s => Line(s.Text, "No text yet"),
        ScreenshotStep s => s.Area switch
        {
            ScreenshotArea.AllScreens => "All screens, to the clipboard",
            ScreenshotArea.ActiveWindow => "The window in front, to the clipboard",
            _ => "The screen with the pointer, to the clipboard",
        },
        FocusStep s => Line(string.Join(" · ", new[] { s.Program.Trim(), s.Title.Trim() }.Where(x => x.Length > 0)), "No window set yet"),
        WebhookStep s => Line(s.Request.Message, "No message yet")
            + (s.Request.SendImage && Webhooks.SupportsImages(s.Request.Service) ? " + picture" : ""),
        _ => "",
    };
}
