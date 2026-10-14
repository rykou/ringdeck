using System.Globalization;

namespace RingDeck.Core;

/// <summary>Colour conversions for the colour wheel: hue in degrees (0..360), saturation and value 0..1.</summary>
public static class ColorMath
{
    public static (byte R, byte G, byte B) HsvToRgb(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        s = Math.Clamp(s, 0, 1);
        v = Math.Clamp(v, 0, 1);
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;
        var (r, g, b) = (int)(h / 60) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        static byte To(double f) => (byte)Math.Round(Math.Clamp(f, 0, 1) * 255);
        return (To(r + m), To(g + m), To(b + m));
    }

    public static (double H, double S, double V) RgbToHsv(byte r, byte g, byte b)
    {
        double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
        var max = Math.Max(rf, Math.Max(gf, bf));
        var min = Math.Min(rf, Math.Min(gf, bf));
        var d = max - min;
        var h = d == 0 ? 0
            : max == rf ? 60 * (((gf - bf) / d) % 6)
            : max == gf ? 60 * ((bf - rf) / d + 2)
            : 60 * ((rf - gf) / d + 4);
        if (h < 0) h += 360;
        return (h, max == 0 ? 0 : d / max, max);
    }

    /// <summary>Accepts #RGB, #RRGGBB and #AARRGGBB (alpha ignored), with or without the #.</summary>
    public static bool TryParseHex(string? text, out (byte R, byte G, byte B) rgb)
    {
        rgb = default;
        var hex = (text ?? "").Trim().TrimStart('#');
        if (hex.Length == 3) hex = string.Concat(hex.Select(c => new string(c, 2)));
        if (hex.Length == 8) hex = hex[2..];
        if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n)) return false;
        rgb = ((byte)(n >> 16), (byte)(n >> 8), (byte)n);
        return true;
    }

    public static string ToHex(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";
}
