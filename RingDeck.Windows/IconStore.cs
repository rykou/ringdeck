using System.Drawing;
using System.Drawing.Imaging;
using static RingDeck.Windows.NativeMethods;

namespace RingDeck.Windows;

/// <summary>Keeps slice images inside the config folder so rings survive the original file moving.</summary>
public sealed class IconStore
{
    static readonly string[] ImageTypes = { ".png", ".jpg", ".jpeg" };
    readonly string _dir;

    public IconStore(string dir) => _dir = dir;

    /// <summary>Copies an image, or extracts the icon of a program, shortcut or .ico, into the store.</summary>
    public string Import(string sourcePath)
    {
        Directory.CreateDirectory(_dir);
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (ImageTypes.Contains(extension))
        {
            var name = Guid.NewGuid().ToString("N") + extension;
            File.Copy(sourcePath, Path.Combine(_dir, name));
            return "file:" + name;
        }
        var png = Guid.NewGuid().ToString("N") + ".png";
        using var bitmap = ExtractLarge(sourcePath) ?? Icon.ExtractAssociatedIcon(sourcePath)?.ToBitmap()
            ?? throw new InvalidOperationException("That file has no icon RingDeck can use.");
        bitmap.Save(Path.Combine(_dir, png), ImageFormat.Png);
        return "file:" + png;
    }

    public string? PathOf(string icon)
    {
        if (!icon.StartsWith("file:", StringComparison.Ordinal)) return null;
        var path = Path.Combine(_dir, Path.GetFileName(icon[5..]));
        return File.Exists(path) ? path : null;
    }

    static Bitmap? ExtractLarge(string path)
    {
        var handles = new IntPtr[1];
        if (PrivateExtractIcons(path, 0, 128, 128, handles, null, 1, 0) is 0 or uint.MaxValue || handles[0] == IntPtr.Zero)
            return null;
        try { using var icon = Icon.FromHandle(handles[0]); return icon.ToBitmap(); }
        finally { DestroyIcon(handles[0]); }
    }
}
