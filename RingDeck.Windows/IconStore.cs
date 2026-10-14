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
    /// <summary>Where a .lnk shortcut points (its program), or the path itself when it isn't a resolvable shortcut.
    /// Icons are read from the target so they don't carry the shortcut arrow.</summary>
    public static string ResolveShortcut(string path)
    {
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return path;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return path;
            dynamic shell = Activator.CreateInstance(shellType)!;
            try
            {
                string target = shell.CreateShortcut(path).TargetPath;
                return !string.IsNullOrEmpty(target) && File.Exists(target) ? target : path;
            }
            finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
        }
        catch (Exception) { return path; }
    }

    /// <summary>The picture a file would give: an image file's own bytes, or a program's / shortcut's icon as PNG.</summary>
    public static byte[] PictureBytes(string sourcePath)
    {
        sourcePath = ResolveShortcut(sourcePath);
        if (ImageTypes.Contains(Path.GetExtension(sourcePath).ToLowerInvariant()) || sourcePath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
            return File.ReadAllBytes(sourcePath);
        using var bitmap = ExtractLarge(sourcePath) ?? Icon.ExtractAssociatedIcon(sourcePath)?.ToBitmap()
            ?? throw new InvalidOperationException("That file has no icon RingDeck can use.");
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    /// <summary>Stores a PNG made by the crop editor. Round crops are named *.fill.png: they fill the whole slice.</summary>
    public string ImportPng(byte[] png, bool fillsSlice)
    {
        Directory.CreateDirectory(_dir);
        var name = Guid.NewGuid().ToString("N") + (fillsSlice ? ".fill.png" : ".png");
        File.WriteAllBytes(Path.Combine(_dir, name), png);
        return "file:" + name;
    }

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
