using System.Globalization;
using System.Text;

namespace RingDeck.Core;

/// <summary>One point of a recorded pointer path: screen pixels, and milliseconds since the path started.</summary>
public readonly record struct PathPoint(int X, int Y, int Ms);

/// <summary>Recorded pointer paths, stored as compact text ("x,y,ms;x,y,ms") so long paths stay one line in config.json.</summary>
public static class MousePath
{
    public const double MinSpeed = 1, MaxSpeed = 3;

    public static List<PathPoint> Parse(string? text)
    {
        var points = new List<PathPoint>();
        if (string.IsNullOrWhiteSpace(text)) return points;
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var n = part.Split(',');
            if (n.Length == 3 && Int(n[0], out var x) && Int(n[1], out var y) && Int(n[2], out var ms))
                points.Add(new PathPoint(x, y, Math.Max(0, ms)));
        }
        return points;
    }

    static bool Int(string s, out int value) => int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    public static string Format(IEnumerable<PathPoint> points)
    {
        var sb = new StringBuilder();
        foreach (var p in points)
        {
            if (sb.Length > 0) sb.Append(';');
            sb.Append(CultureInfo.InvariantCulture, $"{p.X},{p.Y},{p.Ms}");
        }
        return sb.ToString();
    }

    public static double ClampSpeed(double speed) => double.IsFinite(speed) ? Math.Clamp(speed, MinSpeed, MaxSpeed) : 1;

    /// <summary>How long playing the path takes at this speed.</summary>
    public static int DurationMs(IReadOnlyList<PathPoint> points, double speed) =>
        points.Count == 0 ? 0 : (int)Math.Round(points[^1].Ms / ClampSpeed(speed));

    /// <summary>A short description for the macro editor.</summary>
    public static string Describe(string? text)
    {
        var points = Parse(text);
        if (points.Count == 0) return "Not recorded yet";
        if (points.Count == 1) return $"Jump to {points[0].X}, {points[0].Y}";
        return string.Create(CultureInfo.InvariantCulture, $"Path {points[^1].Ms / 1000.0:0.0#} s \u2192 {points[^1].X}, {points[^1].Y}");
    }
}
