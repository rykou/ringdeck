using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RingDeck.Core;

namespace RingDeck.Windows;

/// <summary>Makes a picture fit in a byte budget: smaller and as a JPEG, trying the largest size first.</summary>
public static class ImageShrinker
{
    static readonly int[] Widths = [1280, 1024, 800, 640, 512, 400, 320, 256, 200, 160];
    static readonly int[] Qualities = [80, 65, 50, 38];

    public static ClipImage? Fit(ClipImage image, int maxBytes)
    {
        if (maxBytes < 1_500) return null;
        try
        {
            using var input = new MemoryStream(image.Data);
            var frame = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            BitmapSource source = new FormatConvertedBitmap(frame, PixelFormats.Bgr24, null, 0);   // JPEG has no transparency
            foreach (var width in Widths.Where(w => w < frame.PixelWidth).Prepend(Math.Min(frame.PixelWidth, Widths[0])).Distinct())
            {
                var scale = (double)width / frame.PixelWidth;
                BitmapSource sized = scale >= 1 ? source : new TransformedBitmap(source, new ScaleTransform(scale, scale));
                sized.Freeze();
                foreach (var quality in Qualities)
                {
                    var encoder = new JpegBitmapEncoder { QualityLevel = quality };
                    encoder.Frames.Add(BitmapFrame.Create(sized));
                    using var output = new MemoryStream();
                    encoder.Save(output);
                    if (output.Length <= maxBytes) return new ClipImage(output.ToArray(), Path.ChangeExtension(image.FileName, ".jpg"), "image/jpeg");
                }
            }
        }
        catch (Exception e) when (e is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException or OutOfMemoryException) { }
        return null;
    }
}
