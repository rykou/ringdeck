namespace RingDeck.Core.Tests;

public class ActionRunnerTests
{
    sealed class Fakes : IInputSender, IClipboard, ILauncher, INotifier
    {
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
        return (new ActionRunner(f, f, f, f, () => new DateTime(2026, 10, 13), f.Delay), f);
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
}
