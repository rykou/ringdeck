namespace RingDeck.Core.Tests;

public class MacroTimingTests
{
    /// <summary>Records what the runner sends; delays move a fake clock forward.</summary>
    sealed class Desk : IInputSender, IClipboard, ILauncher, INotifier
    {
        public long Now;
        public List<string> Log = new();
        public List<string> Notes = new();
        public CancellationTokenSource? CancelAfterDelays;
        public int DelaysBeforeCancel;
        public bool CanSendToForeground() => true;
        public void ReleaseModifiers() { }
        public void RestoreModifiers() { }
        public void SendCombo(Shortcut combo) => Log.Add("combo " + combo);
        public void SendText(string text) => Log.Add("text " + text);
        public void SendKey(int vk, bool up) => Log.Add((up ? "up " : "down ") + KeyNames.NameOf(vk));
        public void SendMouseButton(ClickButton button, bool up) => Log.Add((up ? "mouseup " : "mousedown ") + button);
        public void MoveMouse(int x, int y) => Log.Add($"move {x},{y} @{Now}");
        public void Scroll(ScrollDirection direction, int notches) => Log.Add($"scroll {direction} {notches}");
        public string? GetText() => "";
        public bool TrySetText(string text) => true;
        public object? Capture() => null;
        public void Restore(object? snapshot) { }
        public void Launch(string target, string arguments, string workingFolder) => Log.Add("open " + target);
        public void Notify(string title, string message) => Notes.Add(title + ": " + message);
        public Task Delay(int ms, CancellationToken ct)
        {
            Log.Add($"wait {ms}");
            Now += ms;
            if (CancelAfterDelays is not null && --DelaysBeforeCancel <= 0) CancelAfterDelays.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    static (ActionRunner, Desk) Make()
    {
        var d = new Desk();
        return (new ActionRunner(d, d, d, d, () => new DateTime(2026, 10, 19), d.Delay, clockMs: () => d.Now), d);
    }

    static Task Run(ActionRunner r, params MacroStep[] steps) =>
        r.RunAsync(new Slice { Label = "M", Action = new MacroAction { Steps = steps.ToList() } }, default);

    [Fact]
    public async Task KeyDownUpClickAndWaitsRunWithExactMilliseconds()
    {
        var (r, d) = Make();
        await Run(r,
            new LaunchStep { Target = "https://example.com" }, new DelayStep { Ms = 1500 },
            new MouseMoveStep { Path = "100,200,0" }, new DelayStep { Ms = 50 },
            new ClickStep { Button = ClickButton.Left }, new DelayStep { Ms = 200 },
            new KeyDownStep { Key = "Shift" }, new KeyDownStep { Key = "A" }, new KeyUpStep { Key = "A" }, new KeyUpStep { Key = "Shift" });
        Assert.Equal(new[]
        {
            "open https://example.com", "wait 1500",          // no extra 10 ms gap when a wait follows
            "move 100,200 @1500", "wait 50",
            "mousedown Left", "mouseup Left", "wait 200",
            "down Shift", "wait 10", "down A", "wait 10", "up A", "wait 10", "up Shift", "wait 10",
        }, d.Log);
        Assert.Empty(d.Notes);
    }

    [Fact]
    public async Task KeysAndButtonsLeftDownAreReleasedAtTheEnd()
    {
        var (r, d) = Make();
        await Run(r, new KeyDownStep { Key = "Ctrl" }, new MouseDownStep { Button = ClickButton.Right });
        Assert.Equal(new[] { "down Ctrl", "wait 10", "mousedown Right", "wait 10", "up Ctrl", "mouseup Right" }, d.Log);
    }

    [Fact]
    public async Task StoppingAMacroReleasesWhatItHolds()
    {
        var (r, d) = Make();
        using var cts = new CancellationTokenSource();
        d.CancelAfterDelays = cts;
        d.DelaysBeforeCancel = 1;
        await r.RunAsync(new Slice { Label = "M", Action = new MacroAction { Steps =
            { new KeyDownStep { Key = "Shift" }, new DelayStep { Ms = 5000 }, new TextStep { Text = "never" } } } }, cts.Token);
        Assert.Equal(new[] { "down Shift", "wait 5000", "up Shift" }, d.Log);
        Assert.Empty(d.Notes);
    }

    [Fact]
    public async Task UnknownKeyStopsTheMacroAndSaysWhich()
    {
        var (r, d) = Make();
        await Run(r, new KeyDownStep { Key = "Nope" }, new TextStep { Text = "never" });
        Assert.DoesNotContain("text never", d.Log);
        Assert.Contains("Step 1 (key down)", Assert.Single(d.Notes));
    }

    [Fact]
    public async Task MousePathPlaysInTimeAndFasterWithSpeed()
    {
        var (r, d) = Make();
        await Run(r, new MouseMoveStep { Path = "0,0,0;10,0,100;20,0,300" });
        Assert.Equal(new[] { "move 0,0 @0", "wait 100", "move 10,0 @100", "wait 200", "move 20,0 @300", "wait 10" }, d.Log);

        (r, d) = Make();
        await Run(r, new MouseMoveStep { Path = "0,0,0;10,0,100;20,0,300", Speed = 2 });
        Assert.Equal(new[] { "move 0,0 @0", "wait 50", "move 10,0 @50", "wait 100", "move 20,0 @150", "wait 10" }, d.Log);
    }

    [Fact]
    public async Task UnrecordedMouseMoveIsReported()
    {
        var (r, d) = Make();
        await Run(r, new MouseMoveStep());
        Assert.Contains("not been recorded", Assert.Single(d.Notes));
    }

    [Fact]
    public void RecorderKeepsDownUpAndTheTimeBetween()
    {
        long now = 1000;
        var rec = new MacroRecorder(() => now);
        rec.OnKey(0xA0, true);                 // Shift down
        now += 120; rec.OnKey(0x41, true);      // A down
        now += 5; rec.OnKey(0x41, true);        // auto-repeat: ignored
        now += 80; rec.OnKey(0x41, false);      // A up
        now += 40; rec.OnKey(0xA0, false);      // Shift up
        rec.OnKey(0x42, false);                 // up without a down (held before recording): ignored
        Assert.Equal(new[] { "down Shift", "wait 120", "down A", "wait 85", "up A", "wait 40", "up Shift" }, Describe(rec.Finish()));
    }

    [Fact]
    public void RecorderJumpsToEachClickAndLetsGoOfHeldKeysAtTheEnd()
    {
        long now = 0;
        var rec = new MacroRecorder(() => now);
        rec.OnMouse(ClickButton.Left, true, 300, 400);
        now += 90; rec.OnMouse(ClickButton.Left, false, 300, 400);
        now += 500; rec.OnMouse(ClickButton.Left, true, 300, 400);   // same spot: no second jump
        now += 70; rec.OnMouse(ClickButton.Left, false, 300, 400);
        now += 10; rec.OnKey(0xA2, true);                              // Ctrl still down when recording stops
        Assert.Equal(new[]
        {
            "jump 300,400", "mousedown Left", "wait 90", "mouseup Left", "wait 500",
            "mousedown Left", "wait 70", "mouseup Left", "wait 10", "down Ctrl", "up Ctrl",
        }, Describe(rec.Finish()));
    }

    static IEnumerable<string> Describe(IEnumerable<MacroStep> steps) => steps.Select(s => s switch
    {
        KeyDownStep k => "down " + k.Key, KeyUpStep k => "up " + k.Key, DelayStep w => "wait " + w.Ms,
        MouseDownStep m => "mousedown " + m.Button, MouseUpStep m => "mouseup " + m.Button,
        MouseMoveStep m when MousePath.Parse(m.Path).Count == 1 => "jump " + string.Join(";", MousePath.Parse(m.Path).Select(p => $"{p.X},{p.Y}")),
        MouseMoveStep m => "path " + m.Path,
        ScrollStep sc => $"scroll {sc.Direction} {sc.Notches}",
        _ => s.GetType().Name,
    });

    [Fact]
    public void PathTextRoundTripsAndSpeedIsKeptInRange()
    {
        var points = new[] { new PathPoint(10, 10, 0), new PathPoint(15, 12, 20), new PathPoint(20, 14, 35) };
        var text = MousePath.Format(points);
        Assert.Equal("10,10,0;15,12,20;20,14,35", text);
        Assert.Equal(points, MousePath.Parse(text));
        Assert.Equal(35, MousePath.DurationMs(points, 1));
        Assert.Equal(12, MousePath.DurationMs(points, 3));
        Assert.Equal(35, MousePath.DurationMs(points, 0.2));      // speed is kept between x1 and x3
        Assert.Empty(MousePath.Parse("garbage;1,2"));
    }

    [Fact]
    public void RecordingMovementClicksAndKeysMakesOneTimeline()
    {
        long now = 0;
        var rec = new MacroRecorder(() => now);
        void At(long t, int x, int y) { now = t; rec.OnPointer(x, y); }
        At(0, 100, 100);                         // F9: recording starts with the pointer resting here
        At(500, 100, 100);
        At(508, 110, 105);                       // starts moving
        At(516, 130, 115);
        At(524, 150, 120);
        now = 600; rec.OnMouse(ClickButton.Left, true, 150, 120);   // click where the move ended: no jump
        now = 680; rec.OnMouse(ClickButton.Left, false, 150, 120);
        At(700, 150, 120);
        At(708, 160, 120);                       // moves again
        At(716, 170, 125);
        At(1000, 170, 125);                      // rests long enough: that move ends
        now = 1400; rec.OnKey(0x0D, true);       // Enter
        now = 1460; rec.OnKey(0x0D, false);
        Assert.Equal(new[]
        {
            "path 100,100,0;110,105,8;130,115,16;150,120,24", "wait 76",      // no wait before the first step
            "mousedown Left", "wait 80", "mouseup Left", "wait 20",
            "path 150,120,0;160,120,8;170,125,16", "wait 684",
            "down Enter", "wait 60", "up Enter",
        }, Describe(rec.Finish()));
    }

    [Fact]
    public void AClickWhoseReleaseWasMissedDoesNotBlockLaterClicks()
    {
        long now = 0;
        var rec = new MacroRecorder(() => now);
        rec.OnMouse(ClickButton.Left, true, 10, 10);          // its release never reaches the recorder
        now = 300; rec.OnMouse(ClickButton.Left, true, 10, 10);
        now = 380; rec.OnMouse(ClickButton.Left, false, 10, 10);
        Assert.Equal(new[] { "jump 10,10", "mousedown Left", "mouseup Left", "wait 300", "mousedown Left", "wait 80", "mouseup Left" },
            Describe(rec.Finish()));
    }

    [Fact]
    public void WheelNotchesInARowBecomeOneScrollStep()
    {
        long now = 0;
        var rec = new MacroRecorder(() => now);
        rec.OnPointer(50, 50);
        now = 100; rec.OnWheel(-120, false, 50, 50);       // three notches down, close together
        now = 140; rec.OnWheel(-120, false, 50, 50);
        now = 180; rec.OnWheel(-120, false, 50, 50);
        now = 220; rec.OnWheel(120, false, 50, 50);        // turned back up: a new step
        now = 900; rec.OnWheel(120, false, 50, 50);        // much later: a new step after a wait
        now = 950; rec.OnWheel(30, true, 50, 50);          // a precise wheel tilted right: still at least one notch
        now = 960; rec.OnWheel(30, true, 50, 50);
        Assert.Equal(new[] { "scroll Down 3", "wait 40", "scroll Up 1", "wait 680", "scroll Up 1", "wait 50", "scroll Right 1" },
            Describe(rec.Finish()));
    }

    [Fact]
    public void AScrollAfterTheMacroLastSawThePointerJumpsThereFirst()
    {
        long now = 0;
        var rec = new MacroRecorder(() => now);
        rec.OnWheel(-240, false, 300, 200);
        Assert.Equal(new[] { "jump 300,200", "scroll Down 2" }, Describe(rec.Finish()));
    }

    [Fact]
    public async Task ScrollStepTurnsTheWheel()
    {
        var (r, d) = Make();
        await Run(r, new ScrollStep { Direction = ScrollDirection.Up, Notches = 3 }, new ScrollStep { Notches = 0 });
        Assert.Equal(new[] { "scroll Up 3", "wait 10" }, d.Log);
    }

    [Fact]
    public void AMoveStillGoingWhenRecordingStopsIsKept()
    {
        long now = 0;
        var rec = new MacroRecorder(() => now);
        rec.OnPointer(0, 0);
        now = 8; rec.OnPointer(5, 0);
        Assert.Equal(1, rec.Count);
        Assert.Equal(new[] { "path 0,0,0;5,0,8" }, Describe(rec.Finish()));
    }

    static List<MacroStep> Sample() => new()
    {
        new KeyDownStep { Key = "A" }, new KeyUpStep { Key = "A" },                                  // press
        new DelayStep { Ms = 100 },
        new MouseDownStep { Button = ClickButton.Left }, new DelayStep { Ms = 80 }, new MouseUpStep { Button = ClickButton.Left }, // held click
        new KeyDownStep { Key = "Shift" }, new TextStep { Text = "x" }, new KeyUpStep { Key = "Shift" },  // held across a step: stays apart
    };

    [Fact]
    public void JoinedViewGroupsPressesAndSplitViewShowsEveryStep()
    {
        var steps = Sample();
        var joined = MacroView.Rows(steps, split: false);
        Assert.Equal(new[] { MacroRowKind.KeyPress, MacroRowKind.Step, MacroRowKind.ButtonPress, MacroRowKind.Step, MacroRowKind.Step, MacroRowKind.Step },
            joined.Select(r => r.Kind));
        Assert.Null(joined[0].Hold(steps));
        Assert.Equal(80, joined[2].Hold(steps)!.Ms);
        Assert.Equal(steps.Count, MacroView.Rows(steps, split: true).Count);
        Assert.Equal(180, MacroView.TotalMs(steps));
    }

    [Fact]
    public void MovingAndRemovingARowTakesTheWholePress()
    {
        var steps = Sample();
        var rows = MacroView.Rows(steps, split: false);
        MacroView.Move(steps, rows, 2, -1);                   // the held click goes above the wait
        Assert.IsType<MouseDownStep>(steps[2]);
        Assert.IsType<MouseUpStep>(steps[4]);
        Assert.Equal(100, Assert.IsType<DelayStep>(steps[5]).Ms);

        rows = MacroView.Rows(steps, split: false);
        MacroView.Move(steps, rows, 0, +1);                   // the key press goes below the click
        Assert.IsType<MouseDownStep>(steps[0]);
        Assert.IsType<KeyDownStep>(steps[3]);

        rows = MacroView.Rows(steps, split: false);
        MacroView.Remove(steps, rows[0]);
        Assert.Equal(6, steps.Count);
        Assert.IsType<KeyDownStep>(steps[0]);
    }

    [Fact]
    public void DraggingARowMovesItsWholePressAnywhere()
    {
        // rows: 0 key press A, 1 wait, 2 held click, 3 Shift down, 4 type x, 5 Shift up
        var steps = Sample();
        MacroView.MoveTo(steps, MacroView.Rows(steps, split: false), 2, 0);       // held click to the top
        Assert.Equal(new[] { "mousedown Left", "wait 80", "mouseup Left", "down A", "up A", "wait 100" }, Describe(steps).Take(6));

        steps = Sample();
        MacroView.MoveTo(steps, MacroView.Rows(steps, split: false), 0, 6);       // key press to the end
        Assert.Equal(new[] { "down A", "up A" }, Describe(steps).TakeLast(2));
        Assert.Equal(9, steps.Count);

        steps = Sample();
        var before = Describe(steps).ToList();
        var rows = MacroView.Rows(steps, split: false);
        MacroView.MoveTo(steps, rows, 2, 2);                                      // dropped on itself
        MacroView.MoveTo(steps, rows, 2, 3);                                      // or just below itself: no change
        MacroView.MoveTo(steps, rows, 9, 0);                                      // nonsense: ignored
        Assert.Equal(before, Describe(steps));
    }

    [Fact]
    public void WaitsCanBeRemovedOrSetAllAtOnce()
    {
        var steps = Sample();
        Assert.Equal(2, MacroView.SetWaits(steps, 999_999));
        Assert.All(steps.OfType<DelayStep>(), w => Assert.Equal(ActionRunner.MaxDelayMs, w.Ms));
        Assert.Equal(2, MacroView.RemoveWaits(steps));
        Assert.Equal(7, steps.Count);
        Assert.DoesNotContain(steps, s => s is DelayStep);
    }

    [Fact]
    public void NewStepsSurviveSaveAndLoad()
    {
        var config = new AppConfig { Settings = { SplitKeyEvents = true }, Rings = { new Ring { Slices = { new Slice { Action = new MacroAction { Steps =
        {
            new KeyDownStep { Key = "Shift" }, new KeyUpStep { Key = "Shift" }, new ClickStep { Button = ClickButton.Right },
            new MouseDownStep { Button = ClickButton.Middle }, new MouseUpStep { Button = ClickButton.Middle },
            new MouseMoveStep { Path = "1,2,0;3,4,16", Speed = 2.5 },
            new ScrollStep { Direction = ScrollDirection.Left, Notches = 4 },
        } } } } } } };
        var back = ConfigStore.FromJson(ConfigStore.ToJson(config))!;
        Assert.True(back.Settings.SplitKeyEvents);
        var steps = Assert.IsType<MacroAction>(back.Rings[0].Slices[0].Action).Steps;
        Assert.Equal("Shift", Assert.IsType<KeyDownStep>(steps[0]).Key);
        Assert.Equal("Shift", Assert.IsType<KeyUpStep>(steps[1]).Key);
        Assert.Equal(ClickButton.Right, Assert.IsType<ClickStep>(steps[2]).Button);
        Assert.Equal(ClickButton.Middle, Assert.IsType<MouseDownStep>(steps[3]).Button);
        Assert.Equal(ClickButton.Middle, Assert.IsType<MouseUpStep>(steps[4]).Button);
        var move = Assert.IsType<MouseMoveStep>(steps[5]);
        Assert.Equal(("1,2,0;3,4,16", 2.5), (move.Path, move.Speed));
        var scroll = Assert.IsType<ScrollStep>(steps[6]);
        Assert.Equal((ScrollDirection.Left, 4), (scroll.Direction, scroll.Notches));
    }

    [Fact]
    public void ModifierNamesWorkAsKeysButShortcutsStillParseAsBefore()
    {
        Assert.True(KeyNames.TryGetVk("Shift", out var shift));
        Assert.Equal(0xA0, shift);
        Assert.True(Shortcut.TryParse("Ctrl+Shift+S", out var s));
        Assert.Equal(new Shortcut(Mods.Ctrl | Mods.Shift, 0x53), s);
        Assert.False(Shortcut.TryParse("Ctrl", out _));
    }
}
