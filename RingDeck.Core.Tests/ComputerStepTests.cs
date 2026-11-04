namespace RingDeck.Core.Tests;

public class ComputerStepTests
{
    sealed class Desk : IInputSender, IClipboard, ILauncher, INotifier
    {
        public string? Text = "before";
        public List<string> Notes = new();
        public bool CanSendToForeground() => true;
        public void ReleaseModifiers() { }
        public void RestoreModifiers() { }
        public void SendCombo(Shortcut combo) { }
        public void SendText(string text) { }
        public void SendKey(int vk, bool up) { }
        public void SendMouseButton(ClickButton button, bool up) { }
        public void Scroll(ScrollDirection direction, int notches) { }
        public void MoveMouse(int x, int y) { }
        public string? GetText() => Text;
        public ClipImage? GetImage() => null;
        public bool TrySetText(string text) { Text = text; return true; }
        public object? Capture() => null;
        public void Restore(object? snapshot) { }
        public void Launch(string target, string arguments, string workingFolder) { }
        public void Notify(string title, string message) => Notes.Add(title + ": " + message);
    }

    static FlowAction Flow(params MacroStep[] steps)
    {
        var flow = FlowAction.New();
        var previous = flow.Nodes[0];
        foreach (var step in steps)
        {
            var node = new FlowNode { Steps = { step } };
            flow.Nodes.Add(node);
            flow.Wires.Add(new FlowWire { From = previous.Id, To = node.Id });
            previous = node;
        }
        flow.Wires.Add(new FlowWire { From = previous.Id, To = flow.Nodes[1].Id });
        return flow;
    }

    [Fact]
    public async Task ItsTextGoesOnTheClipboardAndTheFlowCarriesOn()
    {
        var d = new Desk();
        string? clipboardWhenNextRan = null;
        var runner = new ActionRunner(d, d, d, d, delay: (_, _) => Task.CompletedTask,
            useComputer: (_, _) => Task.FromResult("Failed at step 3 (Click Inbox): not signed in."),
            askAi: (_, text, _, _) => { clipboardWhenNextRan = text; return Task.FromResult("sent"); });
        await runner.RunAsync(new Slice { Label = "Morning", Action = Flow(new ComputerStep { Instruction = "x" }, new AskAiStep { Reads = AiReads.Text }) }, default);
        Assert.Empty(d.Notes);
        Assert.Equal("Failed at step 3 (Click Inbox): not signed in.", clipboardWhenNextRan);
    }

    [Fact]
    public void OnlyRealInputTakesTheKeyboardTurn()
    {
        Assert.False(ActionRunner.UsesInput(new ComputerStep()));
        Assert.True(ActionRunner.UsesInput(new ComputerStep { RealInput = true }));
    }

    [Fact]
    public void SavesAndLoadsWithItsSettings()
    {
        var connection = new AiConnection();
        var config = new AppConfig { AiConnections = { connection }, Settings = { AgentDesktopPath = " \"C:\\tools\\agent-desktop.exe\" " } };
        config.Rings.Add(new Ring { Slices = { new Slice { Action = Flow(new ComputerStep { ConnectionId = connection.Id, Instruction = "1. Open Edge", MaxActions = 9999, RealInput = true }) } } });
        var json = ConfigStore.ToJson(config);
        Assert.Matches("\"type\":\\s*\"computer\"", json);
        var back = ConfigStore.FromJson(json)!;
        var step = AskAiStep.In(back.Rings[0].Slices[0].Action).OfType<ComputerStep>().Single();
        Assert.Equal((connection.Id, "1. Open Edge", 500, true), (step.ConnectionId, step.Instruction, step.MaxActions, step.RealInput));
        Assert.Equal("C:\\tools\\agent-desktop.exe", back.Settings.AgentDesktopPath);
    }

    [Fact]
    public void ExportKeepsTheConnectionItUses()
    {
        var used = new AiConnection { Name = "used", ApiKey = "dpapi:secret" };
        var config = new AppConfig { AiConnections = { used, new AiConnection { Name = "other" } } };
        var ring = new Ring { Slices = { new Slice { Action = Flow(new ComputerStep { ConnectionId = used.Id }) } } };
        config.Rings.Add(ring);
        var exported = ConfigTransfer.Export(config, ring.Id);
        Assert.Equal("used", exported.AiConnections.Single().Name);
        Assert.Equal("", exported.AiConnections.Single().ApiKey);
    }

    [Fact]
    public void FlowCardSaysWhatItIs()
    {
        var node = new FlowNode { Steps = { new ComputerStep { Instruction = "1. Open Edge" } } };
        Assert.Equal(FlowCardKind.Computer, FlowText.Kind(node));
        Assert.Equal("Use the computer", FlowText.Title(node));
        Assert.Equal("1. Open Edge", FlowText.Summary(node));
    }
}
