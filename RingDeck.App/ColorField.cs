using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RingDeck.App;

/// <summary>A hex colour box with a row of preset swatches.</summary>
public sealed class ColorField : StackPanel
{
    static readonly string[] Presets =
    {
        "#1E1E24", "#2D2D3A", "#3B4252", "#4CC2FF", "#3FB950", "#F2C94C", "#F2994A", "#EB5757", "#BB6BD9", "#F2F2F5",
    };

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(string),
        typeof(ColorField), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, e) => ((ColorField)d).Sync()));

    readonly TextBox _hex = new() { Width = 96, MaxLength = 9 };
    readonly Border _swatch = new() { Width = 28, Height = 28, CornerRadius = new CornerRadius(6), Margin = new Thickness(8, 0, 8, 0) };

    public ColorField()
    {
        Orientation = Orientation.Horizontal;
        Children.Add(_hex);
        Children.Add(_swatch);
        foreach (var preset in Presets)
        {
            var button = new Button
            {
                Width = 20, Height = 20, Margin = new Thickness(2, 0, 0, 0), Padding = new Thickness(0), ToolTip = preset,
                Background = Theme.Frozen(Theme.Parse(preset, Colors.Gray)),
            };
            button.Click += (_, _) => Value = preset;
            Children.Add(button);
        }
        _hex.TextChanged += (_, _) => { if (_hex.Text != Value) Value = _hex.Text; };
    }

    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    void Sync()
    {
        if (_hex.Text != Value) _hex.Text = Value ?? "";
        _swatch.Background = Theme.Frozen(Theme.Parse(Value, Colors.Transparent));
    }
}
