using System.Globalization;
using System.Windows.Data;
using RingDeck.Core;

namespace RingDeck.App;

/// <summary>The line under a ring's name in the editor list: its shortcut, and the programs it is limited to.</summary>
public sealed class RingSubtitle : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not Ring ring) return "";
        var shortcut = ring.Shortcut.Length > 0 ? ring.Shortcut : "no shortcut";
        if (ring.OnlyIn.Count == 0) return shortcut;
        var programs = string.Join(", ", ring.OnlyIn.Take(3).Select(p => ProgramNames.Normalize(p).ToLowerInvariant()));
        return $"{shortcut} · {programs}{(ring.OnlyIn.Count > 3 ? "…" : "")}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
