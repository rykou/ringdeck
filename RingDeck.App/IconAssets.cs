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
    static ZipArchive? _emojiZip, _brandZip;
    static List<IconEntry>? _emoji, _icons, _brands;
    static FontFamily? _font;
    static readonly Dictionary<string, ImageSource?> Pictures = new();

    static Stream Resource(string name) => Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/{name}"))!.Stream;

    static ZipArchive EmojiZip => _emojiZip ??= new ZipArchive(Resource("emoji.zip"), ZipArchiveMode.Read);
    static ZipArchive BrandZip => _brandZip ??= new ZipArchive(Resource("brands.zip"), ZipArchiveMode.Read);

    /// <summary>Brand logos (Simple Icons, public domain; the logos are their owners' trademarks).</summary>
    public static IReadOnlyList<IconEntry> Brands
    {
        get
        {
            lock (Gate)
            {
                if (_brands is not null) return _brands;
                using var reader = new StreamReader(BrandZip.GetEntry("brands.json")!.Open());
                return _brands = IconCatalog.ParseBrands(reader.ReadToEnd());
            }
        }
    }

    /// <summary>A brand's own colour.</summary>
    public static Color? BrandColor(string icon) =>
        IconCatalog.BrandColor(Brands, icon) is { } hex && ColorMath.TryParseHex(hex, out var c) ? Color.FromRgb(c.R, c.G, c.B) : null;

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

    /// <summary>The picture for an "emoji:" or "brand:" id (brands are white shapes, tinted when drawn), or null.</summary>
    public static ImageSource? EmojiPicture(string icon)
    {
        lock (Gate)
        {
            if (Pictures.TryGetValue(icon, out var cached)) return cached;
            ImageSource? picture = null;
            var entry = IconCatalog.Split(icon) switch
            {
                ("emoji", var code) => EmojiZip.GetEntry(code + ".png"),
                ("brand", var slug) => BrandZip.GetEntry(slug + ".png"),
                _ => null,
            };
            if (entry is not null)
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
