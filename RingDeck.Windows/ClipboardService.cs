using System.Runtime.InteropServices;
using System.Windows;
using RingDeck.Core;

namespace RingDeck.Windows;

/// <summary>Clipboard access through WPF. Must be called on the UI (STA) thread.</summary>
public sealed class ClipboardService : IClipboard
{
    public string? GetText()
    {
        try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
        catch (ExternalException) { return null; }
    }

    public bool TrySetText(string text)
    {
        try { Clipboard.SetDataObject(text, true); return true; }
        catch (ExternalException) { return false; }
    }

    /// <summary>Copies every format currently on the clipboard (text, images, files) so it can be put back.</summary>
    public object? Capture()
    {
        try
        {
            if (Clipboard.GetDataObject() is not { } current) return null;
            var copy = new DataObject();
            foreach (var format in current.GetFormats(false))
            {
                try { if (current.GetData(format, false) is { } data) copy.SetData(format, data); }
                catch (Exception e) when (e is ExternalException or OutOfMemoryException or NotSupportedException) { }
            }
            return copy;
        }
        catch (ExternalException) { return null; }
    }

    public void Restore(object? snapshot)
    {
        try
        {
            if (snapshot is DataObject data && data.GetFormats(false).Length > 0) Clipboard.SetDataObject(data, true);
            else Clipboard.Clear();
        }
        catch (ExternalException) { }
    }
}
