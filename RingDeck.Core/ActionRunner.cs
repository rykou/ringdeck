namespace RingDeck.Core;

/// <summary>Runs a slice's action against the focused window. Never throws: failures are reported to the notifier.</summary>
public sealed class ActionRunner
{
    const int ClipboardRetryMs = 50, ClipboardGiveUpMs = 500, RestoreAfterMs = 250, StepGapMs = 10, FocusSettleMs = 150;
    public const int MaxDelayMs = 60_000, MaxScrollNotches = 100;
    static readonly Shortcut PasteCombo = new(Mods.Ctrl, 0x56);

    readonly IInputSender _input;
    readonly IClipboard _clipboard;
    readonly ILauncher _launcher;
    readonly INotifier _notifier;
    readonly Func<DateTime> _now;
    readonly Func<int, CancellationToken, Task> _delay;
    readonly IWindowFocuser? _focuser;
    readonly WebhookClient? _webhooks;
    readonly Func<string, string> _reveal;
    readonly Func<CancellationToken, Task<string?>>? _selection;
    readonly Func<ClipImage, int, ClipImage?>? _shrink;
    readonly Func<long> _clock;
    /// <summary>Keys and mouse buttons a running macro pressed and has not let go yet.</summary>
    readonly List<int> _heldKeys = new();
    readonly List<ClickButton> _heldButtons = new();

    /// <param name="webhooks">Sends webhook slices and steps; without it they report that they can't run.</param>
    /// <param name="reveal">Decrypts the stored webhook addresses and tokens.</param>
    /// <param name="selection">Reads the text selected in the focused app, for {selection}.</param>
    /// <param name="shrink">Makes a picture fit in a number of bytes (smaller JPEG), or returns null; for Teams.</param>
    /// <param name="clockMs">A millisecond clock, for playing recorded mouse paths in time.</param>
    public ActionRunner(IInputSender input, IClipboard clipboard, ILauncher launcher, INotifier notifier,
        Func<DateTime>? now = null, Func<int, CancellationToken, Task>? delay = null, IWindowFocuser? focuser = null,
        WebhookClient? webhooks = null, Func<string, string>? reveal = null, Func<CancellationToken, Task<string?>>? selection = null,
        Func<ClipImage, int, ClipImage?>? shrink = null, Func<long>? clockMs = null)
    {
        (_input, _clipboard, _launcher, _notifier) = (input, clipboard, launcher, notifier);
        _now = now ?? (() => DateTime.Now);
        _delay = delay ?? ((ms, ct) => Task.Delay(ms, ct));
        _focuser = focuser;
        _webhooks = webhooks;
        _reveal = reveal ?? (s => s);
        _selection = selection;
        _shrink = shrink;
        _clock = clockMs ?? (() => System.Diagnostics.Stopwatch.GetTimestamp() * 1000 / System.Diagnostics.Stopwatch.Frequency);
    }

    public async Task RunAsync(Slice slice, CancellationToken ct)
    {
        try
        {
            switch (slice.Action)
            {
                case PasteTextAction paste: await Paste(paste, ct); break;
                case MacroAction macro: await Macro(macro, ct); break;
                case FlowAction flow: await Flow(flow, ct); break;
                case LaunchAction launch: _launcher.Launch(launch.Target, launch.Arguments, launch.WorkingFolder); break;
                case WebhookAction hook:
                    await SendWebhook(hook.Request, ct);
                    _notifier.Notify($"\"{slice.Label}\" sent", "To " + Webhooks.Name(hook.Request.Service) + ".");
                    break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _notifier.Notify($"\"{slice.Label}\" did not run", ex.Message); }
    }

    void RequireSendable()
    {
        if (!_input.CanSendToForeground())
            throw new InvalidOperationException(
                "The active window is running as administrator, so Windows blocks RingDeck from sending input to it.");
    }

    /// <summary>Pastes text exactly as given (no placeholder expansion), restoring the clipboard afterwards.
    /// Used for AI replies. Never throws: failures are reported to the notifier.</summary>
    public async Task PasteTextAsync(string text, CancellationToken ct)
    {
        try { await PasteRaw(text, restoreClipboard: true, ct); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _notifier.Notify("The reply could not be pasted", ex.Message); }
    }

    Task Paste(PasteTextAction paste, CancellationToken ct) =>
        PasteRaw(Placeholders.Expand(paste.Text ?? "", _now(), _clipboard.GetText), paste.RestoreClipboard, ct);

    async Task PasteRaw(string text, bool restoreClipboard, CancellationToken ct, bool insideMacro = false)
    {
        if (text.Length == 0) return;
        if (!insideMacro) RequireSendable();
        var saved = restoreClipboard ? _clipboard.Capture() : null;
        var waited = 0;
        while (!_clipboard.TrySetText(text))
        {
            if (waited >= ClipboardGiveUpMs) throw new InvalidOperationException("The clipboard is in use by another program.");
            await _delay(ClipboardRetryMs, ct);
            waited += ClipboardRetryMs;
        }
        try
        {
            if (!insideMacro) _input.ReleaseModifiers();     // a macro has already let go of them, once
            _input.SendCombo(PasteCombo);
            if (restoreClipboard) await _delay(RestoreAfterMs, ct);
        }
        finally
        {
            if (restoreClipboard) _clipboard.Restore(saved);
            if (!insideMacro) _input.RestoreModifiers();
        }
    }

    async Task Macro(MacroAction macro, CancellationToken ct)
    {
        if (macro.Steps.Count == 0) return;
        RequireSendable();
        _input.ReleaseModifiers();
        try { await RunSteps(macro.Steps, ct); }
        finally
        {
            ReleaseHeld();
            _input.RestoreModifiers();
        }
    }

    /// <summary>Set while a flow runs: cards that use the keyboard, mouse or clipboard take turns through it.</summary>
    SemaphoreSlim? _inputTurn;
    /// <summary>True inside the card that has the turn (so a web request in it doesn't wait for the turn again).</summary>
    readonly AsyncLocal<bool> _hasTurn = new();

    /// <summary>Steps that need the keyboard, mouse or clipboard to themselves. A web request only needs them for a
    /// moment (to read the selection or the clipboard picture), and takes its turn just for that.</summary>
    public static bool UsesInput(MacroStep step) => step is not (DelayStep or LaunchStep or WebhookStep);

    /// <summary>Runs a flow: every card reached from Start, each once all the cards wired into it are done, so
    /// branches run at the same time. A failing card stops the whole flow, like Esc.</summary>
    async Task Flow(FlowAction flow, CancellationToken outer)
    {
        if (FlowGraph.Problem(flow) is { } problem) throw new InvalidOperationException(problem);
        var reachable = FlowGraph.Reachable(flow);
        var cards = flow.Nodes.Where(n => reachable.Contains(n.Id)).ToList();
        if (cards.Any(n => n.Steps.Any(UsesInput))) RequireSendable();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(outer);
        var ct = stop.Token;
        var tasks = new Dictionary<Guid, Task>();
        _inputTurn = new SemaphoreSlim(1, 1);
        _input.ReleaseModifiers();

        Task Card(FlowNode node)
        {
            if (tasks.TryGetValue(node.Id, out var task)) return task;
            var before = flow.Wires.Where(w => w.To == node.Id && reachable.Contains(w.From))
                .Select(w => FlowGraph.Find(flow, w.From)).OfType<FlowNode>().Select(Card).ToList();
            return tasks[node.Id] = RunCard(node, before);
        }

        async Task RunCard(FlowNode node, List<Task> before)
        {
            await Task.WhenAll(before);
            if (node.Kind != FlowNodeKind.Action || node.Steps.Count == 0) return;
            var turn = node.Steps.Any(UsesInput);
            if (turn) await _inputTurn!.WaitAsync(ct);
            try
            {
                _hasTurn.Value = turn;
                await RunSteps(node.Steps, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                stop.Cancel();
                throw new InvalidOperationException($"\"{FlowText.Title(node)}\": {ex.Message}", ex);
            }
            finally
            {
                if (turn)
                {
                    ReleaseHeld();
                    _hasTurn.Value = false;
                    _inputTurn!.Release();
                }
            }
        }

        var all = cards.Select(Card).ToList();
        try { await Task.WhenAll(all); }
        catch
        {
            // Report the card that failed, not the branches it stopped.
            var failure = all.Where(t => t.IsFaulted).SelectMany(t => t.Exception!.InnerExceptions)
                .FirstOrDefault(e => e is not OperationCanceledException);
            if (failure is not null) throw failure;
            throw;
        }
        finally
        {
            ReleaseHeld();
            _input.RestoreModifiers();
            _inputTurn = null;
        }
    }

    /// <summary>Runs steps in order. Throws on the first that fails, naming it.</summary>
    async Task RunSteps(IList<MacroStep> steps, CancellationToken ct)
    {
        for (var i = 0; i < steps.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var step = steps[i];
            try
            {
                if (await RunStep(step, ct)) continue;      // a wait needs no gap after it
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidOperationException($"Step {i + 1} ({StepName(step)}): {ex.Message}", ex);
            }
            // A short gap lets the app keep up; when a wait comes next, its own milliseconds are the gap.
            if (i + 1 < steps.Count && steps[i + 1] is DelayStep) continue;
            await _delay(StepGapMs, ct);
        }
    }

    /// <summary>Lets go of every key and button the macro still holds: it ended, failed or was stopped with Esc.</summary>
    void ReleaseHeld()
    {
        foreach (var vk in Enumerable.Reverse(_heldKeys))
            try { _input.SendKey(vk, up: true); } catch (Exception) { }
        foreach (var button in Enumerable.Reverse(_heldButtons))
            try { _input.SendMouseButton(button, up: true); } catch (Exception) { }
        _heldKeys.Clear();
        _heldButtons.Clear();
    }

    static int KeyVk(string? name)
    {
        var text = (name ?? "").Trim();
        if (text.Length == 0) throw new InvalidOperationException("no key is set.");
        if (!KeyNames.TryGetVk(text, out var vk) || vk < 0x08)
            throw new InvalidOperationException($"\"{text}\" is not a key RingDeck understands.");
        return vk;
    }

    /// <summary>Plays a recorded path: each point at its time (divided by the speed). Points that fall behind are
    /// still visited, so the pointer always ends where the recording ended.</summary>
    async Task MovePath(MouseMoveStep move, CancellationToken ct)
    {
        var points = MousePath.Parse(move.Path);
        if (points.Count == 0) throw new InvalidOperationException("the mouse move has not been recorded yet.");
        var speed = MousePath.ClampSpeed(move.Speed);
        var start = _clock();
        foreach (var point in points)
        {
            var due = (long)(point.Ms / speed) - (_clock() - start);
            if (due > 0) await _delay((int)Math.Min(due, MaxDelayMs), ct);
            ct.ThrowIfCancellationRequested();
            _input.MoveMouse(point.X, point.Y);
        }
    }

    /// <summary>Runs one macro step. Returns true when no gap should follow (a wait, or a step with nothing to do).</summary>
    async Task<bool> RunStep(MacroStep step, CancellationToken ct)
    {
        string Expand(string? text) => Placeholders.Expand(text ?? "", _now(), _clipboard.GetText);
        switch (step)
        {
            case KeyStep key:
                if (!Shortcut.TryParse(key.Combo, out var combo))
                    throw new InvalidOperationException($"\"{key.Combo}\" is not a key combination RingDeck understands.");
                _input.SendCombo(combo);
                return false;
            case TextStep text:
                _input.SendText(Expand(text.Text));
                return false;
            case DelayStep wait:
                await _delay(Math.Clamp(wait.Ms, 0, MaxDelayMs), ct);
                return true;
            case PasteStep paste:
                var pasted = Expand(paste.Text);
                if (pasted.Length == 0) return true;
                await PasteRaw(pasted, restoreClipboard: true, ct, insideMacro: true);
                return false;
            case LaunchStep launch:
                if (string.IsNullOrWhiteSpace(launch.Target)) return true;
                _launcher.Launch(launch.Target, launch.Arguments, "");
                return false;
            case FocusStep focus:
                if (string.IsNullOrWhiteSpace(focus.Program) && string.IsNullOrWhiteSpace(focus.Title)) return true;
                if (_focuser is null || !_focuser.Focus(focus.Program.Trim(), focus.Title.Trim()))
                    throw new InvalidOperationException($"no open window matches {Describe(focus)}.");
                await _delay(FocusSettleMs, ct);                    // let the window come to the front before typing into it
                return false;
            case ClipboardStep clip:
                var text2 = Expand(clip.Text);
                for (var waited = 0; !_clipboard.TrySetText(text2); waited += ClipboardRetryMs)
                {
                    if (waited >= ClipboardGiveUpMs) throw new InvalidOperationException("the clipboard is in use by another program.");
                    await _delay(ClipboardRetryMs, ct);
                }
                return false;
            case WebhookStep hook:
                await SendWebhook(hook.Request, ct);
                return false;
            case KeyDownStep down:
                var downVk = KeyVk(down.Key);
                _input.SendKey(downVk, up: false);
                if (!_heldKeys.Contains(downVk)) _heldKeys.Add(downVk);
                return false;
            case KeyUpStep up:
                var upVk = KeyVk(up.Key);
                _input.SendKey(upVk, up: true);
                _heldKeys.Remove(upVk);
                return false;
            case ClickStep click:
                _input.SendMouseButton(click.Button, up: false);
                _input.SendMouseButton(click.Button, up: true);
                return false;
            case MouseDownStep mouseDown:
                _input.SendMouseButton(mouseDown.Button, up: false);
                if (!_heldButtons.Contains(mouseDown.Button)) _heldButtons.Add(mouseDown.Button);
                return false;
            case MouseUpStep mouseUp:
                _input.SendMouseButton(mouseUp.Button, up: true);
                _heldButtons.Remove(mouseUp.Button);
                return false;
            case MouseMoveStep move:
                await MovePath(move, ct);
                return false;
            case ScreenshotStep shot:
                _clipboard.PutScreenshot(shot.Area);
                return false;
            case ScrollStep scroll:
                if (scroll.Notches <= 0) return true;
                _input.Scroll(scroll.Direction, Math.Min(scroll.Notches, MaxScrollNotches));
                return false;
            default:
                return true;
        }
    }

    /// <summary>Sends one webhook. The clipboard image is read first, because reading the selection goes through the
    /// clipboard too. Throws <see cref="WebhookException"/> with a message fit to show.</summary>
    public async Task SendWebhook(WebRequest request, CancellationToken ct)
    {
        if (_webhooks is null) throw new WebhookException("Web requests are not available here.");
        // In a flow, reading the clipboard and the selection waits for the input turn, but sending doesn't.
        var turn = _inputTurn is { } t && !_hasTurn.Value ? t : null;
        if (turn is not null) await turn.WaitAsync(ct);
        ClipImage? image;
        string? selection = null;
        DateTime now;
        Dictionary<string, Func<string?>> extra;
        string message;
        try
        {
            image = request.SendImage && Webhooks.SupportsImages(request.Service) ? _clipboard.GetImage() : null;
            var usesSelection = Placeholders.Uses(request.Message, "selection") || (request.Service == WebService.Custom
                && (Placeholders.Uses(request.Body, "selection") || Placeholders.Uses(_reveal(request.Url), "selection")));
            if (usesSelection && _selection is not null && _input.CanSendToForeground()) selection = await _selection(ct);
            now = _now();
            extra = new Dictionary<string, Func<string?>> { ["selection"] = () => selection };
            message = Placeholders.Expand(request.Message, now, _clipboard.GetText, extra);
            extra["message"] = () => message;
        }
        finally { turn?.Release(); }
        if (image is not null && request.Service == WebService.Teams) image = FitForTeams(image, message, request.Title.Trim(), _shrink);
        using var http = Webhooks.Build(request, message, image, _reveal, (text, escape) => Placeholders.Expand(text, now, _clipboard.GetText, extra, escape));
        await _webhooks.SendAsync(http, Webhooks.Secrets(request, _reveal), ct);
    }

    /// <summary>Teams takes a picture only inside a small message: shrink it to what's left after the text.</summary>
    public static ClipImage FitForTeams(ClipImage image, string message, string title, Func<ClipImage, int, ClipImage?>? shrink)
    {
        var budget = Webhooks.TeamsImageBudget(message, title);
        if (image.Data.Length <= budget) return image;
        return shrink?.Invoke(image, budget)
            ?? throw new WebhookException("The picture is too big for a Teams message (about 28 KB in all) and could not be made smaller.");
    }

    static string Describe(FocusStep f) =>
        string.Join(" and ", new[] { f.Program.Trim().Length > 0 ? $"program \"{f.Program.Trim()}\"" : "", f.Title.Trim().Length > 0 ? $"title \"{f.Title.Trim()}\"" : "" }.Where(x => x.Length > 0));

    static string StepName(MacroStep step) => step switch
    {
        KeyStep => "keys", TextStep => "type text", DelayStep => "wait", PasteStep => "paste",
        KeyDownStep => "key down", KeyUpStep => "key up", ClickStep => "click", MouseDownStep => "mouse down",
        MouseUpStep => "mouse up", MouseMoveStep => "mouse move", ScrollStep => "scroll", ScreenshotStep => "screenshot",
        LaunchStep => "open", FocusStep => "focus window", ClipboardStep => "set clipboard",
        WebhookStep w => "send to " + Webhooks.Name(w.Request.Service), _ => "step",
    };
}
