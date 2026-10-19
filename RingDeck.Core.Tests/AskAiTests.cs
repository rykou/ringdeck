namespace RingDeck.Core.Tests;

public class AskAiTests
{
    sealed class Desk : IInputSender, IClipboard, ILauncher, INotifier
    {
        readonly object _gate = new();
        public List<string> Log = new();
        public List<string> Notes = new();
        public string? Text = "copied words";
        public ClipImage? Image = new([1, 2, 3], "image.png", "image/png");
        void Add(string s) { lock (_gate) Log.Add(s); }
        public bool CanSendToForeground() => true;
        public void ReleaseModifiers() { }
        public void RestoreModifiers() { }
        public void SendCombo(Shortcut combo) => Add("combo " + combo);
        public void SendText(string text) => Add("text " + text);
        public void SendKey(int vk, bool up) { }
        public void SendMouseButton(ClickButton button, bool up) { }
        public void Scroll(ScrollDirection direction, int notches) { }
        public void MoveMouse(int x, int y) { }
        public string? GetText() => Text;
        public ClipImage? GetImage() => Image;
        public bool TrySetText(string text) { Text = text; Image = null; Add("clip " + text); return true; }
        public object? Capture() => null;
        public void Restore(object? snapshot) { }
        public void Launch(string target, string arguments, string workingFolder) => Add("open " + target);
        public void Notify(string title, string message) { lock (_gate) Notes.Add(title + ": " + message); }
        public async Task Delay(int ms, CancellationToken ct) { await Task.Delay(Math.Min(ms, 5), ct); }
    }

    static (ActionRunner, Desk) Make(Func<AskAiStep, string?, ClipImage?, CancellationToken, Task<string>> ask)
    {
        var d = new Desk();
        return (new ActionRunner(d, d, d, d, () => new DateTime(2026, 10, 19), d.Delay, askAi: ask), d);
    }

    static Task Macro(ActionRunner r, params MacroStep[] steps) =>
        r.RunAsync(new Slice { Label = "M", Action = new MacroAction { Steps = steps.ToList() } }, default);

    [Fact]
    public async Task AsksWithThePictureAndPutsTheAnswerOnTheClipboard()
    {
        (string? Text, ClipImage? Image)? seen = null;
        var (r, d) = Make((_, text, image, _) => { seen = (text, image); return Task.FromResult("It says: File not found"); });
        var picture = d.Image;
        await Macro(r, new AskAiStep { Instruction = "Read the error" });
        Assert.Empty(d.Notes);
        Assert.Null(seen!.Value.Text);                          // reads the picture only
        Assert.Same(picture, seen.Value.Image);
        Assert.Equal("clip It says: File not found", d.Log[0]);
        Assert.Null(d.Image);                                    // the answer replaces the picture
        Assert.Equal("It says: File not found", d.Text);
    }

    [Fact]
    public async Task ReadsWhatItIsToldTo()
    {
        var seen = new List<(string?, ClipImage?)>();
        var (r, d) = Make((_, text, image, _) => { seen.Add((text, image)); return Task.FromResult("ok"); });
        await Macro(r, new AskAiStep { Reads = AiReads.Text, Instruction = "x" });
        d.Text = "copied words";
        await Macro(r, new AskAiStep { Reads = AiReads.Both, Instruction = "x" });
        await Macro(r, new AskAiStep { Reads = AiReads.Nothing, Instruction = "x" });
        Assert.Equal(("copied words", (ClipImage?)null), seen[0]);
        Assert.Equal(("copied words", d.Image), seen[1]);
        Assert.Equal((null, (ClipImage?)null), seen[2]);
    }

    [Fact]
    public async Task NoPictureToReadIsAClearError()
    {
        var (r, d) = Make((_, _, _, _) => Task.FromResult("never"));
        d.Image = null;
        await Macro(r, new AskAiStep { Instruction = "Read it" }, new TextStep { Text = "never" });
        Assert.DoesNotContain("text never", d.Log);
        Assert.Contains("no picture on the clipboard", Assert.Single(d.Notes));
    }

    [Fact]
    public async Task OtherBranchesKeepTypingWhileTheAiThinks()
    {
        var flow = FlowAction.New();
        var start = flow.Nodes[0];
        var stop = flow.Nodes[1];
        var ask = new FlowNode { Steps = { new AskAiStep { Instruction = "Read" } } };
        var type = new FlowNode { Steps = { new TextStep { Text = "typed meanwhile" } } };
        flow.Nodes.AddRange(new[] { ask, type });
        FlowGraph.Link(flow, start.Id, ask.Id);
        FlowGraph.Link(flow, start.Id, type.Id);
        FlowGraph.Link(flow, ask.Id, stop.Id);
        var answer = new TaskCompletionSource<string>();
        var (r, d) = Make((_, _, _, _) => answer.Task);
        var running = r.RunAsync(new Slice { Label = "F", Action = flow }, default);
        for (var i = 0; i < 100 && !d.Log.Contains("text typed meanwhile"); i++) await Task.Delay(10);
        Assert.Contains("text typed meanwhile", d.Log);        // not blocked by the AI waiting
        answer.SetResult("done");
        await running;
        Assert.Equal("clip done", d.Log[^1]);
        Assert.Empty(d.Notes);
    }

    [Fact]
    public void NoScreenshotWarningAfterAnAskAiCard()
    {
        var flow = FlowAction.New();
        var (start, stop) = (flow.Nodes[0], flow.Nodes[1]);
        var shot = new FlowNode { Steps = { new ScreenshotStep() } };
        var ask = new FlowNode { Steps = { new AskAiStep() } };
        var send = new FlowNode { Steps = { new WebhookStep { Request = { Service = WebService.Teams, Message = "{clipboard}" } } } };
        flow.Nodes.AddRange(new[] { shot, ask, send });
        FlowGraph.Link(flow, start.Id, shot.Id);
        FlowGraph.Link(flow, shot.Id, ask.Id);
        FlowGraph.Link(flow, ask.Id, send.Id);
        FlowGraph.Link(flow, send.Id, stop.Id);
        Assert.Empty(FlowGraph.ScreenshotNotSent(flow, send));      // the picture is gone by then: nothing to warn about
    }

    [Fact]
    public void CardsAndExportsKnowAboutAskAi()
    {
        var node = new FlowNode { Steps = { new AskAiStep { Instruction = "Read the error" } } };
        Assert.Equal(("Ask AI", "Read the error · reads the picture"), (FlowText.Title(node), FlowText.Summary(node)));
        Assert.Equal(FlowCardKind.Ai, FlowText.Kind(node));

        var connection = new AiConnection { Name = "Claude", ApiKey = "dpapi:SECRET" };
        var flow = FlowAction.New();
        flow.Nodes.Add(new FlowNode { Steps = { new AskAiStep { ConnectionId = connection.Id } } });
        var ring = new Ring { Slices = { new Slice { Action = flow } } };
        var config = new AppConfig { Rings = { ring }, AiConnections = { connection, new AiConnection { Name = "Unused" } } };
        var exported = ConfigTransfer.Export(config, ring.Id);
        var kept = Assert.Single(exported.AiConnections);           // the connection the step uses travels; the unused one doesn't
        Assert.Equal(("Claude", ""), (kept.Name, kept.ApiKey));        // without its key

        var target = new AppConfig();
        var imported = ConfigTransfer.Import(target, exported);
        var step = (AskAiStep)((FlowAction)imported[0].Slices[0].Action!).Nodes[2].Steps[0];
        Assert.Equal(target.AiConnections.Single().Id, step.ConnectionId);   // points at the imported copy
    }
}
