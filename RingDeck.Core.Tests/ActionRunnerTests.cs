namespace RingDeck.Core.Tests;

public class ActionRunnerTests
{
    sealed class Fakes : IInputSender, IClipboard, ILauncher, INotifier, IWindowFocuser
    {
        public bool FocusWorks = true;
        public bool Focus(string program, string title) { Log.Add($"focus {program}|{title}"); return FocusWorks; }
        public List<string> Log = new();
        public List<string> Notes = new();
        public List<int> Delays = new();
        public string? Clip = "old";
        public int SetFailures;
        public bool Elevated;
        public Exception? LaunchError;
        public CancellationTokenSource? CancelOnDelay;

        public bool CanSendToForeground() => !Elevated;
        public void ReleaseModifiers() => Log.Add("release");
        public void RestoreModifiers() => Log.Add("restore-mods");
        public void SendCombo(Shortcut combo) => Log.Add("combo " + combo);
        public void SendText(string text) => Log.Add("text " + text);
        public string? GetText() => Clip;
        public bool TrySetText(string text) { if (SetFailures-- > 0) return false; Clip = text; Log.Add("set " + text); return true; }
        public object? Capture() => Clip;
        public void Restore(object? snapshot) { Clip = (string?)snapshot; Log.Add("restore"); }
        public void Launch(string target, string arguments, string workingFolder)
        { if (LaunchError is not null) throw LaunchError; Log.Add($"launch {target}|{arguments}|{workingFolder}"); }
        public void Notify(string title, string message) => Notes.Add(title + ": " + message);
        public Task Delay(int ms, CancellationToken ct)
        { Delays.Add(ms); CancelOnDelay?.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    }

    static (ActionRunner, Fakes) Make()
    {
        var f = new Fakes();
        return (new ActionRunner(f, f, f, f, () => new DateTime(2026, 10, 13), f.Delay, f), f);
    }

    static Slice S(SliceAction a) => new() { Label = "My slice", Action = a };

    [Fact]
    public async Task PasteSetsClipboardSendsCtrlVAndRestores()
    {
        var (r, f) = Make();
        await r.RunAsync(S(new PasteTextAction { Text = "on {date:yyyy} was {clipboard}" }), default);
        Assert.Equal(new[] { "set on 2026 was old", "release", "combo Ctrl+V", "restore", "restore-mods" }, f.Log);
        Assert.Equal("old", f.Clip);
        Assert.Empty(f.Notes);
    }

    [Fact]
    public async Task PasteWithoutRestoreLeavesTextOnClipboard()
    {
        var (r, f) = Make();
        await r.RunAsync(S(new PasteTextAction { Text = "x", RestoreClipboard = false }), default);
        Assert.DoesNotContain("restore", f.Log);
        Assert.Equal("x", f.Clip);
    }

    [Fact]
    public async Task PasteRetriesBusyClipboardThenSucceeds()
    {
        var (r, f) = Make();
        f.SetFailures = 3;
        await r.RunAsync(S(new PasteTextAction { Text = "x" }), default);
        Assert.Contains("combo Ctrl+V", f.Log);
    }

    [Fact]
    public async Task PasteGivesUpAfterHalfASecondAndLeavesClipboardAlone()
    {
        var (r, f) = Make();
        f.SetFailures = 1000;
        await r.RunAsync(S(new PasteTextAction { Text = "x" }), default);
        Assert.Empty(f.Log);
        Assert.Equal("old", f.Clip);
        Assert.InRange(f.Delays.Sum(), 450, 550);
        Assert.Contains("My slice", Assert.Single(f.Notes));
    }

    [Fact]
    public async Task RawPasteKeepsBracesAndPlaceholdersAsTheyAre()
    {
        var (r, f) = Make();
        await r.PasteTextAsync("x = {date} {{clipboard}}", default);
        Assert.Equal(new[] { "set x = {date} {{clipboard}}", "release", "combo Ctrl+V", "restore", "restore-mods" }, f.Log);
        Assert.Equal("old", f.Clip);
    }

    [Fact]
    public async Task RawPasteReportsFailureInsteadOfThrowing()
    {
        var (r, f) = Make();
        f.Elevated = true;
        await r.PasteTextAsync("x", default);
        Assert.Empty(f.Log);
        Assert.Contains("administrator", Assert.Single(f.Notes));
    }

    [Fact]
    public async Task EmptyPasteDoesNothing()
    {
        var (r, f) = Make();
        await r.RunAsync(S(new PasteTextAction { Text = "" }), default);
        Assert.Empty(f.Log);
    }

    [Fact]
    public async Task ElevatedForegroundBlocksPasteAndMacroWithNotice()
    {
        var (r, f) = Make();
        f.Elevated = true;
        await r.RunAsync(S(new PasteTextAction { Text = "x" }), default);
        await r.RunAsync(S(new MacroAction { Steps = { new TextStep { Text = "x" } } }), default);
        Assert.Empty(f.Log);
        Assert.Equal(2, f.Notes.Count);
        Assert.Contains("administrator", f.Notes[0]);
    }

    [Fact]
    public async Task MacroRunsStepsInOrder()
    {
        var (r, f) = Make();
        await r.RunAsync(S(new MacroAction { Steps =
        {
            new KeyStep { Combo = "Ctrl+A" }, new DelayStep { Ms = 40 }, new TextStep { Text = "y{date:yyyy}" }, new KeyStep { Combo = "Enter" },
        } }), default);
        Assert.Equal(new[] { "release", "combo Ctrl+A", "text y2026", "combo Enter", "restore-mods" }, f.Log);
        Assert.Contains(40, f.Delays);
    }

    [Fact]
    public async Task MacroStopsAtBadComboAndNamesIt()
    {
        var (r, f) = Make();
        await r.RunAsync(S(new MacroAction { Steps = { new KeyStep { Combo = "Ctrl+Nope" }, new TextStep { Text = "never" } } }), default);
        Assert.DoesNotContain("text never", f.Log);
        Assert.Contains("Ctrl+Nope", Assert.Single(f.Notes));
    }

    [Fact]
    public async Task CancelledMacroStopsQuietly()
    {
        var (r, f) = Make();
        using var cts = new CancellationTokenSource();
        f.CancelOnDelay = cts;
        await r.RunAsync(S(new MacroAction { Steps = { new TextStep { Text = "a" }, new DelayStep { Ms = 500 }, new TextStep { Text = "b" } } }), cts.Token);
        Assert.Contains("text a", f.Log);
        Assert.DoesNotContain("text b", f.Log);
        Assert.Equal("restore-mods", f.Log[^1]);          // held modifiers are given back even when cancelled
        Assert.Empty(f.Notes);
    }

    [Fact]
    public async Task CancelledPasteStillRestoresClipboard()
    {
        var (r, f) = Make();
        using var cts = new CancellationTokenSource();
        f.CancelOnDelay = cts;
        await r.RunAsync(S(new PasteTextAction { Text = "x" }), cts.Token);
        Assert.Equal("old", f.Clip);
    }

    [Fact]
    public async Task LaunchPassesArgumentsAndReportsFailure()
    {
        var (r, f) = Make();
        await r.RunAsync(S(new LaunchAction { Target = "notepad.exe", Arguments = "a.txt", WorkingFolder = "C:\\" }), default);
        Assert.Equal(new[] { "launch notepad.exe|a.txt|C:\\" }, f.Log);
        f.LaunchError = new FileNotFoundException("The system cannot find the file specified.");
        await r.RunAsync(S(new LaunchAction { Target = "nope.exe" }), default);
        Assert.Contains("cannot find the file", Assert.Single(f.Notes));
    }

    [Fact]
    public async Task SliceWithNoActionOrNestedRingIsANoOp()
    {
        var (r, f) = Make();
        await r.RunAsync(new Slice(), default);
        await r.RunAsync(S(new NestedRingAction()), default);
        Assert.Empty(f.Log);
        Assert.Empty(f.Notes);
    }

    [Fact]
    public async Task WorkflowRunsEveryKindOfStepInOrder()
    {
        var (r, f) = Make();
        await r.RunAsync(S(new MacroAction { Steps =
        {
            new FocusStep { Program = "outlook.exe", Title = "Inbox" },
            new KeyStep { Combo = "Ctrl+N" },
            new DelayStep { Ms = 300 },
            new PasteStep { Text = "Report for {date:yyyy-MM-dd}" },
            new KeyStep { Combo = "Tab" },
            new TextStep { Text = "typed" },
            new LaunchStep { Target = "notepad.exe", Arguments = "a.txt" },
            new ClipboardStep { Text = "kept {date:yyyy}" },
        } }), default);
        Assert.Equal(new[]
        {
            "release", "focus outlook.exe|Inbox", "combo Ctrl+N", "set Report for 2026-10-13", "combo Ctrl+V", "restore",
            "combo Tab", "text typed", "launch notepad.exe|a.txt|", "set kept 2026", "restore-mods",
        }, f.Log);
        Assert.Equal("kept 2026", f.Clip);                 // a clipboard step leaves its text on the clipboard
        Assert.Contains(300, f.Delays);
        Assert.Empty(f.Notes);
    }

    [Fact]
    public async Task MissingWindowStopsTheWorkflowAndNamesTheStep()
    {
        var (r, f) = Make();
        f.FocusWorks = false;
        await r.RunAsync(S(new MacroAction { Steps = { new KeyStep { Combo = "Ctrl+A" }, new FocusStep { Program = "excel" }, new TextStep { Text = "never" } } }), default);
        Assert.DoesNotContain("text never", f.Log);
        var note = Assert.Single(f.Notes);
        Assert.Contains("Step 2", note);
        Assert.Contains("excel", note);
        Assert.Equal("restore-mods", f.Log[^1]);
    }

    [Fact]
    public async Task PasteStepDoesNotReleaseModifiersAgain()
    {
        var (r, f) = Make();
        await r.RunAsync(S(new MacroAction { Steps = { new PasteStep { Text = "a" }, new PasteStep { Text = "b" } } }), default);
        Assert.Equal(1, f.Log.Count(l => l == "release"));
        Assert.Equal(1, f.Log.Count(l => l == "restore-mods"));
        Assert.Equal("old", f.Clip);
    }

    [Fact]
    public async Task EmptyStepsAreSkippedQuietly()
    {
        var (r, f) = Make();
        await r.RunAsync(S(new MacroAction { Steps = { new PasteStep { Text = "" }, new FocusStep(), new LaunchStep(), new KeyStep { Combo = "Enter" } } }), default);
        Assert.Contains("combo Enter", f.Log);
        Assert.DoesNotContain(f.Log, l => l.StartsWith("focus") || l.StartsWith("launch"));
        Assert.Empty(f.Notes);
    }

    [Fact]
    public void NewStepsRoundTrip()
    {
        var config = new AppConfig { Rings = { new Ring { Slices = { new Slice { Action = new MacroAction { Steps =
        {
            new PasteStep { Text = "p" }, new LaunchStep { Target = "t", Arguments = "a" }, new FocusStep { Program = "x", Title = "y" }, new ClipboardStep { Text = "c" },
        } } } } } } };
        var json = ConfigStore.ToJson(config);
        var steps = ((MacroAction)ConfigStore.FromJson(json)!.Rings[0].Slices[0].Action!).Steps;
        Assert.IsType<PasteStep>(steps[0]);
        Assert.Equal("a", Assert.IsType<LaunchStep>(steps[1]).Arguments);
        Assert.Equal("y", Assert.IsType<FocusStep>(steps[2]).Title);
        Assert.Equal("c", Assert.IsType<ClipboardStep>(steps[3]).Text);
        foreach (var type in new[] { "\"paste\"", "\"open\"", "\"focus\"", "\"clipboard\"" }) Assert.Contains(type, json);
    }
}
