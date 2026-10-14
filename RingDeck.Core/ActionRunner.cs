namespace RingDeck.Core;

/// <summary>Runs a slice's action against the focused window. Never throws: failures are reported to the notifier.</summary>
public sealed class ActionRunner
{
    const int ClipboardRetryMs = 50, ClipboardGiveUpMs = 500, RestoreAfterMs = 250, StepGapMs = 10, MaxDelayMs = 60_000, FocusSettleMs = 150;
    static readonly Shortcut PasteCombo = new(Mods.Ctrl, 0x56);

    readonly IInputSender _input;
    readonly IClipboard _clipboard;
    readonly ILauncher _launcher;
    readonly INotifier _notifier;
    readonly Func<DateTime> _now;
    readonly Func<int, CancellationToken, Task> _delay;
    readonly IWindowFocuser? _focuser;

    public ActionRunner(IInputSender input, IClipboard clipboard, ILauncher launcher, INotifier notifier,
        Func<DateTime>? now = null, Func<int, CancellationToken, Task>? delay = null, IWindowFocuser? focuser = null)
    {
        (_input, _clipboard, _launcher, _notifier) = (input, clipboard, launcher, notifier);
        _now = now ?? (() => DateTime.Now);
        _delay = delay ?? ((ms, ct) => Task.Delay(ms, ct));
        _focuser = focuser;
    }

    public async Task RunAsync(Slice slice, CancellationToken ct)
    {
        try
        {
            switch (slice.Action)
            {
                case PasteTextAction paste: await Paste(paste, ct); break;
                case MacroAction macro: await Macro(macro, ct); break;
                case LaunchAction launch: _launcher.Launch(launch.Target, launch.Arguments, launch.WorkingFolder); break;
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
        try
        {
            for (var i = 0; i < macro.Steps.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var step = macro.Steps[i];
                try
                {
                    if (await RunStep(step, ct)) continue;      // a wait needs no gap after it
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    throw new InvalidOperationException($"Step {i + 1} ({StepName(step)}): {ex.Message}", ex);
                }
                await _delay(StepGapMs, ct);
            }
        }
        finally { _input.RestoreModifiers(); }
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
            default:
                return true;
        }
    }

    static string Describe(FocusStep f) =>
        string.Join(" and ", new[] { f.Program.Trim().Length > 0 ? $"program \"{f.Program.Trim()}\"" : "", f.Title.Trim().Length > 0 ? $"title \"{f.Title.Trim()}\"" : "" }.Where(x => x.Length > 0));

    static string StepName(MacroStep step) => step switch
    {
        KeyStep => "keys", TextStep => "type text", DelayStep => "wait", PasteStep => "paste",
        LaunchStep => "open", FocusStep => "focus window", ClipboardStep => "set clipboard", _ => "step",
    };
}
