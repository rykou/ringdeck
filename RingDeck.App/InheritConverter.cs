using System.Globalization;
using System.Windows.Data;

namespace RingDeck.App;

/// <summary>Lets a ComboBox show "same as Settings" for an empty (null) setting: null is shown as the item tagged
/// "inherit", and choosing that item stores null again.</summary>
public sealed class InheritConverter : IValueConverter
{
    public const string Inherit = "inherit";
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value ?? Inherit;
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value as string == Inherit ? null : value;
}
