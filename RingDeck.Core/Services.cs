namespace RingDeck.Core;

public interface IInputSender
{
    /// <summary>False when the focused window runs as administrator and this process does not.</summary>
    bool CanSendToForeground();
    /// <summary>Releases modifier keys the user is still physically holding.</summary>
    void ReleaseModifiers();
    /// <summary>Presses again the modifiers ReleaseModifiers let go, if the user is still holding them.</summary>
    void RestoreModifiers();
    void SendCombo(Shortcut combo);
    void SendText(string text);
    /// <summary>Presses (up: false) or lets go of (up: true) one key.</summary>
    void SendKey(int vk, bool up);
    /// <summary>Presses or lets go of a mouse button where the pointer is.</summary>
    void SendMouseButton(ClickButton button, bool up);
    /// <summary>Turns the mouse wheel one notch at a time where the pointer is.</summary>
    void Scroll(ScrollDirection direction, int notches);
    /// <summary>Puts the pointer at a screen position (physical pixels).</summary>
    void MoveMouse(int x, int y);
}

public interface IClipboard
{
    string? GetText();
    /// <summary>False when another program has the clipboard locked.</summary>
    bool TrySetText(string text);
    object? Capture();
    void Restore(object? snapshot);
    /// <summary>The picture on the clipboard (a copied image, or a copied image file), or null.</summary>
    ClipImage? GetImage() => null;
}

public sealed record ClipImage(byte[] Data, string FileName, string ContentType);

public interface ILauncher
{
    void Launch(string target, string arguments, string workingFolder);
}

public interface IWindowFocuser
{
    /// <summary>Brings the first window matching the program (exe name) and/or a part of its title to the front.
    /// Returns false when no window matches.</summary>
    bool Focus(string program, string title);
}

public interface INotifier
{
    void Notify(string title, string message);
}
