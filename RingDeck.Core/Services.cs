namespace RingDeck.Core;

public interface IInputSender
{
    /// <summary>False when the focused window runs as administrator and this process does not.</summary>
    bool CanSendToForeground();
    /// <summary>Releases modifier keys the user is still physically holding.</summary>
    void ReleaseModifiers();
    void SendCombo(Shortcut combo);
    void SendText(string text);
}

public interface IClipboard
{
    string? GetText();
    /// <summary>False when another program has the clipboard locked.</summary>
    bool TrySetText(string text);
    object? Capture();
    void Restore(object? snapshot);
}

public interface ILauncher
{
    void Launch(string target, string arguments, string workingFolder);
}

public interface INotifier
{
    void Notify(string title, string message);
}
