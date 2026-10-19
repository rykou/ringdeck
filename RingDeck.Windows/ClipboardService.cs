using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using RingDeck.Core;

namespace RingDeck.Windows;

/// <summary>Clipboard access through WPF. Must be called on the UI (STA) thread.</summary>
public sealed class ClipboardService : IClipboard
{
    static readonly object CaptureFailed = new();

    public string? GetText()
    {
        try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
        catch (ExternalException) { return null; }
    }

    const long MaxImageBytes = 50 * 1024 * 1024;
    static readonly Dictionary<string, string> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".gif"] = "image/gif", [".webp"] = "image/webp",
    };

    /// <summary>A copied image file (from Explorer) as it is, or a copied picture (a screenshot) as PNG.</summary>
    public ClipImage? GetImage()
    {
        try
        {
            if (Clipboard.ContainsFileDropList())
                foreach (var path in Clipboard.GetFileDropList())
                    if (path is not null && ImageTypes.TryGetValue(Path.GetExtension(path), out var type) && File.Exists(path) && new FileInfo(path).Length <= MaxImageBytes)
                        return new ClipImage(File.ReadAllBytes(path), Path.GetFileName(path), type);
            // Snipping Tool and browsers also offer the picture as a PNG file in memory: use it when it's there.
            if (Clipboard.GetData("PNG") is MemoryStream png && png.Length > 0) return new ClipImage(png.ToArray(), "image.png", "image/png");
            if (!Clipboard.ContainsImage() || Clipboard.GetImage() is not { } bitmap) return null;
            // The clipboard's bitmap often has an empty alpha channel, which would make the PNG see-through: drop it.
            var opaque = new System.Windows.Media.Imaging.FormatConvertedBitmap(bitmap, System.Windows.Media.PixelFormats.Bgr24, null, 0);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(opaque));
            using var output = new MemoryStream();
            encoder.Save(output);
            return new ClipImage(output.ToArray(), "image.png", "image/png");
        }
        catch (Exception e) when (e is ExternalException or IOException or UnauthorizedAccessException or NotSupportedException or OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>Takes a screenshot and puts it on the clipboard, as a picture and as PNG (which most apps prefer).</summary>
    public void PutScreenshot(ScreenshotArea area)
    {
        var (x, y) = Screens.Cursor();
        var (left, top, width, height) = area switch
        {
            ScreenshotArea.AllScreens => Screens.VirtualBounds(),
            ScreenshotArea.ActiveWindow => Screens.ForegroundBounds() ?? throw new InvalidOperationException("no window is in front."),
            _ => Screens.MonitorBounds(x, y),
        };
        PutPicture(ScreenGrab.Grab(left, top, width, height));
    }

    /// <summary>Puts a picture on the clipboard, as a bitmap and as PNG.</summary>
    public static void PutPicture(System.Windows.Media.Imaging.BitmapSource picture)
    {
        var png = new MemoryStream();
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(picture));
        encoder.Save(png);
        var data = new DataObject();
        data.SetImage(picture);
        data.SetData("PNG", png);
        for (var tries = 0; ; tries++)
        {
            try { Clipboard.SetDataObject(data, true); return; }
            catch (ExternalException) when (tries < 10) { Thread.Sleep(50); }
            catch (ExternalException) { throw new InvalidOperationException("the clipboard is in use by another program."); }
        }
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
        catch (ExternalException) { return CaptureFailed; }   // unknown contents: leave them alone on restore
    }

    public void Restore(object? snapshot)
    {
        if (ReferenceEquals(snapshot, CaptureFailed)) return;
        try
        {
            if (snapshot is DataObject data && data.GetFormats(false).Length > 0) Clipboard.SetDataObject(data, true);
            else Clipboard.Clear();
        }
        catch (ExternalException) { }
    }
}
