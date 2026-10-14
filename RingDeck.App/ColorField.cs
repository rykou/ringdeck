using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using RingDeck.Core;

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

    readonly TextBox _hex = new() { Width = 92, MaxLength = 9 };
    readonly Border _swatch = new() { Width = 28, Height = 28, CornerRadius = new CornerRadius(6), Margin = new Thickness(6, 0, 4, 0) };

    readonly Popup _picker = new() { StaysOpen = false, Placement = PlacementMode.Bottom, AllowsTransparency = true };
    readonly ColorWheel _wheel = new() { Width = 200, Height = 200 };
    readonly Slider _brightness = new() { Minimum = 0, Maximum = 1, Margin = new Thickness(0, 10, 0, 0) };
    bool _syncing;

    public ColorField()
    {
        Orientation = Orientation.Horizontal;
        Children.Add(_hex);
        Children.Add(_swatch);
        BuildPicker();
        foreach (var preset in Presets)
        {
            var button = new Button
            {
                Width = 18, Height = 18, MinWidth = 0, MinHeight = 0, Margin = new Thickness(2, 0, 0, 0), Padding = new Thickness(0), ToolTip = preset,
                Background = Theme.Frozen(Theme.Parse(preset, Colors.Gray)),
            };
            button.Click += (_, _) => Value = preset;
            Children.Add(button);
        }
        _hex.TextChanged += (_, _) => { if (_hex.Text != Value) Value = _hex.Text; };
    }

    /// <summary>Clicking the swatch opens a colour wheel with a brightness slider; changes apply as you drag.</summary>
    void BuildPicker()
    {
        _swatch.Cursor = System.Windows.Input.Cursors.Hand;
        _swatch.ToolTip = "Pick a colour";
        _swatch.MouseLeftButtonUp += (_, _) =>
        {
            _syncing = true;
            var current = Theme.Parse(Value, Color.FromRgb(0x2D, 0x2D, 0x3A));
            _wheel.Show(current);
            _brightness.Value = _wheel.Brightness;
            _syncing = false;
            _picker.IsOpen = true;
        };
        _wheel.ColorChanged += c => { if (!_syncing) Value = ColorMath.ToHex(c.R, c.G, c.B); };
        _brightness.ValueChanged += (_, e) =>
        {
            if (_syncing) return;
            _wheel.Brightness = e.NewValue;
        };
        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(_wheel);
        panel.Children.Add(new TextBlock { Text = "Brightness", Margin = new Thickness(0, 10, 0, 0), Foreground = (Brush)Application.Current.Resources["SubtleBrush"] });
        panel.Children.Add(_brightness);
        _picker.PlacementTarget = _swatch;
        _picker.Child = new Border
        {
            Child = panel, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1),
            BorderBrush = Theme.Frozen(Color.FromRgb(0x3A, 0x44, 0x52)), Background = (Brush)Application.Current.Resources["StageBrush"],
        };
        Children.Add(_picker);
    }

    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    void Sync()
    {
        if (_hex.Text != Value) _hex.Text = Value ?? "";
        _swatch.Background = Theme.Frozen(Theme.Parse(Value, Colors.Transparent));
    }
}
