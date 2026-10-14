using System.IO.Compression;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RingDeck.Core;

namespace RingDeck.App;

/// <summary>The bundled Fluent emoji pictures and Fluent icon font (both MIT, see THIRD-PARTY-NOTICES.md).
/// Everything loads on first use and is cached.</summary>
public static class IconAssets
{
    static readonly object Gate = new();
    static ZipArchive? _emojiZip;
    static List<IconEntry>? _emoji, _icons;
    static FontFamily? _font;
    static readonly Dictionary<string, ImageSource?> Pictures = new();

    static Stream Resource(string name) => Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/{name}"))!.Stream;

    static ZipArchive EmojiZip => _emojiZip ??= new ZipArchive(Resource("emoji.zip"), ZipArchiveMode.Read);

    public static IReadOnlyList<IconEntry> Emoji
    {
        get
        {
            lock (Gate)
            {
                if (_emoji is not null) return _emoji;
                using var reader = new StreamReader(EmojiZip.GetEntry("emoji.json")!.Open());
                return _emoji = IconCatalog.ParseEmoji(reader.ReadToEnd());
            }
        }
    }

    public static IReadOnlyList<IconEntry> Icons
    {
        get
        {
            lock (Gate)
            {
                if (_icons is not null) return _icons;
                using var reader = new StreamReader(Resource("icons.json"));
                return _icons = IconCatalog.ParseIcons(reader.ReadToEnd());
            }
        }
    }

    public static FontFamily IconFont =>
        _font ??= Fonts.GetFontFamilies(new Uri("pack://application:,,,/Assets/")).FirstOrDefault() ?? new FontFamily("Segoe Fluent Icons");

    /// <summary>The picture for an "emoji:" id, or null when it isn't in the pack.</summary>
    public static ImageSource? EmojiPicture(string icon)
    {
        lock (Gate)
        {
            if (Pictures.TryGetValue(icon, out var cached)) return cached;
            ImageSource? picture = null;
            if (IconCatalog.Split(icon) is ("emoji", var code) && EmojiZip.GetEntry(code + ".png") is { } entry)
            {
                using var stream = new MemoryStream();
                using (var source = entry.Open()) source.CopyTo(stream);
                stream.Position = 0;
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                picture = bitmap;
            }
            return Pictures[icon] = picture;
        }
    }
}
