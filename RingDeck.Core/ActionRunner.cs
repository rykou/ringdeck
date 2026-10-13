namespace RingDeck.Core;

/// <summary>Runs a slice's action against the focused window. Never throws: failures are reported to the notifier.</summary>
public sealed class ActionRunner
{
    const int ClipboardRetryMs = 50, ClipboardGiveUpMs = 500, RestoreAfterMs = 250, StepGapMs = 10, MaxDelayMs = 60_000;
    static readonly Shortcut PasteCombo = new(Mods.Ctrl, 0x56);

    readonly IInputSender _input;
    readonly IClipboard _clipboard;
    readonly ILauncher _launcher;
    readonly INotifier _notifier;
    readonly Func<DateTime> _now;
    readonly Func<int, CancellationToken, Task> _delay;

    public ActionRunner(IInputSender input, IClipboard clipboard, ILauncher launcher, INotifier notifier,
        Func<DateTime>? now = null, Func<int, CancellationToken, Task>? delay = null)
    {
        (_input, _clipboard, _launcher, _notifier) = (input, clipboard, launcher, notifier);
        _now = now ?? (() => DateTime.Now);
        _delay = delay ?? ((ms, ct) => Task.Delay(ms, ct));
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

    async Task Paste(PasteTextAction paste, CancellationToken ct)
    {
        var text = Placeholders.Expand(paste.Text ?? "", _now(), _clipboard.GetText);
        if (text.Length == 0) return;
        RequireSendable();
        var saved = paste.RestoreClipboard ? _clipboard.Capture() : null;
        var waited = 0;
        while (!_clipboard.TrySetText(text))
        {
            if (waited >= ClipboardGiveUpMs) throw new InvalidOperationException("The clipboard is in use by another program.");
            await _delay(ClipboardRetryMs, ct);
            waited += ClipboardRetryMs;
        }
        try
        {
            _input.ReleaseModifiers();
            _input.SendCombo(PasteCombo);
            if (paste.RestoreClipboard) await _delay(RestoreAfterMs, ct);
        }
        finally
        {
            if (paste.RestoreClipboard) _clipboard.Restore(saved);
        }
    }

    async Task Macro(MacroAction macro, CancellationToken ct)
    {
        if (macro.Steps.Count == 0) return;
        RequireSendable();
        _input.ReleaseModifiers();
        foreach (var step in macro.Steps)
        {
            ct.ThrowIfCancellationRequested();
            switch (step)
            {
                case KeyStep key:
                    if (!Shortcut.TryParse(key.Combo, out var combo))
                        throw new InvalidOperationException($"\"{key.Combo}\" is not a key combination RingDeck understands.");
                    _input.SendCombo(combo);
                    break;
                case TextStep text:
                    _input.SendText(Placeholders.Expand(text.Text ?? "", _now(), _clipboard.GetText));
                    break;
                case DelayStep wait:
                    await _delay(Math.Clamp(wait.Ms, 0, MaxDelayMs), ct);
                    continue;
            }
            await _delay(StepGapMs, ct);
        }
    }
}
