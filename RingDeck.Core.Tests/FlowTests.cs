namespace RingDeck.Core.Tests;

public class FlowTests
{
    /// <summary>Records what runs. Delays really yield, so branches interleave the way they would for real.</summary>
    sealed class Desk : IInputSender, IClipboard, ILauncher, INotifier
    {
        readonly object _gate = new();
        public List<string> Log = new();
        public List<string> Notes = new();
        public Exception? LaunchError;
        void Add(string s) { lock (_gate) Log.Add(s); }
        public bool CanSendToForeground() => true;
        public void ReleaseModifiers() { }
        public void RestoreModifiers() { }
        public void SendCombo(Shortcut combo) => Add("combo " + combo);
        public void SendText(string text) => Add("text " + text);
        public void SendKey(int vk, bool up) => Add((up ? "up " : "down ") + KeyNames.NameOf(vk));
        public void SendMouseButton(ClickButton button, bool up) => Add("mouse " + button);
        public void Scroll(ScrollDirection direction, int notches) => Add("scroll");
        public void MoveMouse(int x, int y) => Add("move");
        public string? GetText() => "";
        public bool TrySetText(string text) { Add("clip " + text); return true; }
        public object? Capture() => null;
        public void Restore(object? snapshot) { }
        public void PutScreenshot(ScreenshotArea area) => Add("screenshot " + area);
        public void Launch(string target, string arguments, string workingFolder)
        {
            if (LaunchError is not null) throw LaunchError;
            Add("open " + target);
        }
        public void Notify(string title, string message) { lock (_gate) Notes.Add(title + ": " + message); }
        public async Task Delay(int ms, CancellationToken ct)
        {
            await Task.Delay(Math.Min(ms, 5), ct);
            ct.ThrowIfCancellationRequested();
        }
    }

    static (ActionRunner, Desk) Make()
    {
        var d = new Desk();
        return (new ActionRunner(d, d, d, d, () => new DateTime(2026, 10, 19), d.Delay), d);
    }

    static FlowNode Card(string title, params MacroStep[] steps) => new() { Title = title, Steps = steps.ToList() };

    static (FlowAction Flow, FlowNode Start, FlowNode Stop) Empty()
    {
        var flow = FlowAction.New();
        return (flow, flow.Nodes.Single(n => n.Kind == FlowNodeKind.Start), flow.Nodes.Single(n => n.Kind == FlowNodeKind.Stop));
    }

    static Task Run(ActionRunner r, FlowAction flow) => r.RunAsync(new Slice { Label = "Flow", Action = flow }, default);

    [Fact]
    public void LinksFollowTheRules()
    {
        var (flow, start, stop) = Empty();
        var a = Card("A");
        var b = Card("B");
        flow.Nodes.AddRange(new[] { a, b });
        Assert.True(FlowGraph.Link(flow, start.Id, a.Id));
        Assert.True(FlowGraph.Link(flow, a.Id, b.Id));
        Assert.False(FlowGraph.CanLink(flow, a.Id, b.Id));        // already linked
        Assert.False(FlowGraph.CanLink(flow, b.Id, a.Id));        // would loop
        Assert.False(FlowGraph.CanLink(flow, a.Id, a.Id));        // to itself
        Assert.False(FlowGraph.CanLink(flow, a.Id, start.Id));    // into Start
        Assert.False(FlowGraph.CanLink(flow, stop.Id, a.Id));     // out of Stop
        Assert.True(FlowGraph.CanLink(flow, b.Id, stop.Id));
    }

    [Fact]
    public void ProblemsSayWhatIsMissing()
    {
        var (flow, start, stop) = Empty();
        Assert.Equal("Link Start to the first card.", FlowGraph.Problem(flow));
        var a = Card("A");
        flow.Nodes.Add(a);
        FlowGraph.Link(flow, start.Id, a.Id);
        Assert.Equal("At least one branch must end at a Stop.", FlowGraph.Problem(flow));
        FlowGraph.Link(flow, a.Id, stop.Id);
        Assert.Null(FlowGraph.Problem(flow));
        FlowGraph.Remove(flow, start);                            // Start can't be removed
        Assert.Contains(start, flow.Nodes);
        FlowGraph.Remove(flow, a);
        Assert.Empty(flow.Wires);
    }

    [Fact]
    public async Task BranchesRunAndAJoinWaitsForAllOfThem()
    {
        // Start → open → (screenshot → recorded → Stop) and (send-like card that just ends); plus a join.
        var (flow, start, stop) = Empty();
        var open = Card("Open", new LaunchStep { Target = "browser" });
        var left = Card("Left", new DelayStep { Ms = 30 }, new LaunchStep { Target = "left" });
        var right = Card("Right", new LaunchStep { Target = "right" });
        var join = Card("Join", new LaunchStep { Target = "join" });
        var loose = Card("Never linked", new LaunchStep { Target = "never" });
        flow.Nodes.AddRange(new[] { open, left, right, join, loose });
        FlowGraph.Link(flow, start.Id, open.Id);
        FlowGraph.Link(flow, open.Id, left.Id);
        FlowGraph.Link(flow, open.Id, right.Id);
        FlowGraph.Link(flow, left.Id, join.Id);
        FlowGraph.Link(flow, right.Id, join.Id);
        FlowGraph.Link(flow, join.Id, stop.Id);
        var (r, d) = Make();
        await Run(r, flow);
        Assert.Empty(d.Notes);
        Assert.Equal("open browser", d.Log[0]);
        Assert.Equal("open join", d.Log[^1]);                    // after both branches, once
        Assert.Equal(new[] { "open browser", "open join", "open left", "open right" }, d.Log.Order());
    }

    [Fact]
    public async Task CardsUsingTheKeyboardTakeTurnsInsteadOfMixingUp()
    {
        var (flow, start, stop) = Empty();
        var a = Card("A", new TextStep { Text = "a1" }, new DelayStep { Ms = 20 }, new TextStep { Text = "a2" });
        var b = Card("B", new TextStep { Text = "b1" }, new DelayStep { Ms = 20 }, new TextStep { Text = "b2" });
        flow.Nodes.AddRange(new[] { a, b });
        FlowGraph.Link(flow, start.Id, a.Id);
        FlowGraph.Link(flow, start.Id, b.Id);
        FlowGraph.Link(flow, a.Id, stop.Id);
        var (r, d) = Make();
        await Run(r, flow);
        var typed = string.Join(",", d.Log.Where(l => l.StartsWith("text ")).Select(l => l[5..]));
        Assert.True(typed is "a1,a2,b1,b2" or "b1,b2,a1,a2", typed);
    }

    [Fact]
    public async Task AFailingCardStopsTheFlowAndIsNamed()
    {
        var (flow, start, stop) = Empty();
        var bad = Card("Open the report", new LaunchStep { Target = "x" });
        var slow = Card("Slow", new DelayStep { Ms = 50 }, new DelayStep { Ms = 50 }, new TextStep { Text = "too late" });
        flow.Nodes.AddRange(new[] { bad, slow });
        FlowGraph.Link(flow, start.Id, bad.Id);
        FlowGraph.Link(flow, start.Id, slow.Id);
        FlowGraph.Link(flow, bad.Id, stop.Id);
        var (r, d) = Make();
        d.LaunchError = new InvalidOperationException("no such file");
        await Run(r, flow);
        Assert.DoesNotContain("text too late", d.Log);
        var note = Assert.Single(d.Notes);
        Assert.Contains("\"Open the report\"", note);
        Assert.Contains("no such file", note);
    }

    [Fact]
    public async Task AFlowWithoutAStopDoesNotRun()
    {
        var (flow, start, _) = Empty();
        var a = Card("A", new LaunchStep { Target = "x" });
        flow.Nodes.Add(a);
        FlowGraph.Link(flow, start.Id, a.Id);
        var (r, d) = Make();
        await Run(r, flow);
        Assert.Empty(d.Log);
        Assert.Contains("must end at a Stop", Assert.Single(d.Notes));
    }

    [Fact]
    public async Task ScreenshotStepPutsAPictureOnTheClipboard()
    {
        var (flow, start, stop) = Empty();
        var shot = Card("", new ScreenshotStep { Area = ScreenshotArea.ActiveWindow });
        flow.Nodes.Add(shot);
        FlowGraph.Link(flow, start.Id, shot.Id);
        FlowGraph.Link(flow, shot.Id, stop.Id);
        var (r, d) = Make();
        await Run(r, flow);
        Assert.Equal(new[] { "screenshot ActiveWindow" }, d.Log);
        Assert.Equal("Screenshot", FlowText.Title(shot));
        Assert.Equal("The window in front, to the clipboard", FlowText.Summary(shot));
    }

    [Fact]
    public void CardsGetTitlesAndSummariesFromTheirSteps()
    {
        Assert.Equal(("Open", "https://example.com"), (FlowText.Title(Card("", new LaunchStep { Target = "https://example.com" })), FlowText.Summary(Card("", new LaunchStep { Target = "https://example.com" }))));
        var recorded = Card("Recorded steps", new KeyStep { Combo = "A" }, new DelayStep { Ms = 1500 });
        Assert.Equal(("Recorded steps", "2 steps · 1.5 s"), (FlowText.Title(recorded), FlowText.Summary(recorded)));
        Assert.Equal(FlowCardKind.Steps, FlowText.Kind(recorded));
        Assert.Equal("Send to Discord", FlowText.Title(Card("", new WebhookStep())));
        Assert.Equal("Empty: add steps", FlowText.Summary(Card("")));
    }

    [Fact]
    public void AWebCardAfterAScreenshotThatDoesNotSendThePictureIsSpotted()
    {
        var (flow, start, stop) = Empty();
        var shot = Card("", new ScreenshotStep());
        var wait = Card("", new DelayStep { Ms = 100 });
        var teams = Card("", new WebhookStep { Request = { Service = WebService.Teams, Message = "test" } });
        var slack = Card("", new WebhookStep { Request = { Service = WebService.Slack, Message = "test" } });   // can't take pictures
        flow.Nodes.AddRange(new[] { shot, wait, teams, slack });
        FlowGraph.Link(flow, start.Id, shot.Id);
        FlowGraph.Link(flow, shot.Id, wait.Id);
        FlowGraph.Link(flow, wait.Id, teams.Id);
        FlowGraph.Link(flow, wait.Id, slack.Id);
        FlowGraph.Link(flow, teams.Id, stop.Id);
        Assert.Single(FlowGraph.ScreenshotNotSent(flow, teams));
        Assert.Empty(FlowGraph.ScreenshotNotSent(flow, slack));
        Assert.Equal("test", FlowText.Summary(teams));
        ((WebhookStep)teams.Steps[0]).Request.SendImage = true;
        Assert.Empty(FlowGraph.ScreenshotNotSent(flow, teams));
        Assert.Equal("test + picture", FlowText.Summary(teams));
        var lone = Card("", new WebhookStep { Request = { Service = WebService.Teams } });
        flow.Nodes.Add(lone);
        Assert.Empty(FlowGraph.ScreenshotNotSent(flow, lone));                    // no screenshot before it
    }

    [Fact]
    public void ExportsLeaveOutWebhookSecretsInsideFlows()
    {
        var flow = FlowAction.New();
        flow.Nodes.Add(Card("Send", new WebhookStep { Request = { Url = "https://discord.com/api/webhooks/1/SECRET", Token = "t" } }));
        var config = new AppConfig { Rings = { new Ring { Slices = { new Slice { Action = flow } } } } };
        var copy = ConfigTransfer.ExportAll(config);
        var request = ((WebhookStep)((FlowAction)copy.Rings[0].Slices[0].Action!).Nodes[2].Steps[0]).Request;
        Assert.Equal(("", ""), (request.Url, request.Token));
    }

    [Fact]
    public void LoadingRepairsABrokenFlow()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var json = $$"""
        { "rings": [ { "slices": [ { "action": { "type": "flow",
          "nodes": [ { "id": "{{a}}", "steps": [ { "type": "key", "combo": "A" } ] }, { "id": "{{b}}" } ],
          "wires": [ { "from": "{{a}}", "to": "{{b}}" }, { "from": "{{b}}", "to": "{{a}}" }, { "from": "{{a}}", "to": "{{Guid.NewGuid()}}" } ] } } ] } ] }
        """;
        var flow = (FlowAction)ConfigStore.FromJson(json)!.Rings[0].Slices[0].Action!;
        Assert.Single(flow.Nodes, n => n.Kind == FlowNodeKind.Start);   // a Start is added
        var wire = Assert.Single(flow.Wires);                            // the loop and the dangling wire are dropped
        Assert.Equal((a, b), (wire.From, wire.To));
        var back = (FlowAction)ConfigStore.FromJson(ConfigStore.ToJson(new AppConfig { Rings = { new Ring { Slices = { new Slice { Action = flow } } } } }))!.Rings[0].Slices[0].Action!;
        Assert.Equal(flow.Nodes.Select(n => (n.Id, n.Kind)), back.Nodes.Select(n => (n.Id, n.Kind)));
        Assert.Equal("A", Assert.IsType<KeyStep>(back.Nodes.Single(n => n.Id == a).Steps[0]).Combo);
    }
}
