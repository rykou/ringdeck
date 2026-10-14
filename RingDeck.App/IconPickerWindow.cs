using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using RingDeck.Core;

namespace RingDeck.App;

/// <summary>Searchable grid of the bundled emoji and icons. Picking one closes the window with its id in Result.</summary>
public sealed class IconPickerWindow : Window
{
    const int MaxShown = 360;
    readonly TextBox _search = new() { Margin = new Thickness(0, 0, 0, 10) };
    readonly ToggleButton _emojiTab = new() { Content = "Emoji", IsChecked = true, Padding = new Thickness(16, 6, 16, 6) };
    readonly ToggleButton _iconTab = new() { Content = "Icons", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(6, 0, 0, 0) };
    readonly ToggleButton _brandTab = new() { Content = "Brands", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(6, 0, 0, 0) };
    enum Tab { Emoji, Icons, Brands }
    Tab _tab = Tab.Emoji;
    readonly WrapPanel _groups = new() { Margin = new Thickness(0, 10, 0, 6) };
    readonly WrapPanel _grid = new();
    readonly TextBlock _count = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly ScrollViewer _scroller;
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(140) };
    string? _group;

    public string? Result { get; private set; }

    public IconPickerWindow()
    {
        Title = "Choose an icon";
        Width = 660;
        Height = 600;
        MinWidth = 420;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _search.ToolTip = "Search by name, e.g. rocket, mail, heart, folder";
        _search.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Fill(); };
        _emojiTab.Click += (_, _) => SwitchTo(Tab.Emoji);
        _iconTab.Click += (_, _) => SwitchTo(Tab.Icons);
        _brandTab.Click += (_, _) => SwitchTo(Tab.Brands);

        var tabs = new StackPanel { Orientation = Orientation.Horizontal };
        tabs.Children.Add(_emojiTab);
        tabs.Children.Add(_iconTab);
        tabs.Children.Add(_brandTab);

        var none = new Button { Content = "No icon (first letter)" };
        none.Click += (_, _) => Pick("");
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(none);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        footer.Children.Add(_count);

        _scroller = new ScrollViewer { Content = _grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var root = new DockPanel { Margin = new Thickness(16) };
        foreach (var top in new UIElement[] { _search, tabs, _groups }) { DockPanel.SetDock(top, Dock.Top); root.Children.Add(top); }
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(_scroller);
        Content = root;

        Loaded += (_, _) => { SwitchTo(Tab.Emoji); _search.Focus(); };
    }

    bool EmojiMode => _tab == Tab.Emoji;

    /// <summary>Developer aid for --shot-editor.</summary>
    internal void ShowBrands() => SwitchTo(Tab.Brands);

    void SwitchTo(Tab tab)
    {
        _tab = tab;
        var emoji = tab == Tab.Emoji;
        _emojiTab.IsChecked = tab == Tab.Emoji;
        _iconTab.IsChecked = tab == Tab.Icons;
        _brandTab.IsChecked = tab == Tab.Brands;
        _group = null;
        _groups.Children.Clear();
        _groups.Visibility = emoji ? Visibility.Visible : Visibility.Collapsed;
        if (emoji)
            foreach (var g in new string?[] { null }.Concat(IconCatalog.Groups(IconAssets.Emoji)))
            {
                var chip = new ToggleButton { Content = g ?? "All", IsChecked = g is null, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 3, 10, 3), Tag = g };
                chip.Click += (_, _) =>
                {
                    _group = (string?)chip.Tag;
                    foreach (ToggleButton other in _groups.Children) other.IsChecked = ReferenceEquals(other, chip);
                    Fill();
                };
                _groups.Children.Add(chip);
            }
        Fill();
    }

    void Fill()
    {
        var source = _tab switch { Tab.Emoji => IconAssets.Emoji, Tab.Brands => IconAssets.Brands, _ => IconAssets.Icons };
        var found = IconCatalog.Search(source, _search.Text, EmojiMode ? _group : null, MaxShown + 1);
        _grid.Children.Clear();
        foreach (var entry in found.Take(MaxShown)) _grid.Children.Add(Cell(entry));
        _count.Text = found.Count == 0 ? "Nothing found. Try another word."
            : found.Count > MaxShown ? $"Showing the first {MaxShown}. Type to narrow it down."
            : $"{found.Count} {_tab.ToString().ToLowerInvariant()}" + (_tab == Tab.Brands ? " · logos are trademarks of their owners" : "");
        _scroller.ScrollToTop();
    }

    Button Cell(IconEntry entry)
    {
        object content;
        if (entry.Id.StartsWith("emoji:", StringComparison.Ordinal))
            content = new Image { Source = IconAssets.EmojiPicture(entry.Id), Width = 32, Height = 32 };
        else if (entry.Id.StartsWith("brand:", StringComparison.Ordinal))
        {
            // white logo shape, painted in the brand colour (or light grey when the brand colour is too dark here)
            var colour = IconAssets.BrandColor(entry.Id) ?? Colors.White;
            if (0.2126 * colour.R + 0.7152 * colour.G + 0.0722 * colour.B < 70) colour = Color.FromRgb(0xE6, 0xED, 0xF3);
            content = new System.Windows.Shapes.Rectangle
            {
                Width = 28, Height = 28, Fill = Theme.Frozen(colour),
                OpacityMask = new ImageBrush(IconAssets.EmojiPicture(entry.Id)),
            };
        }
        else
            content = new TextBlock { Text = IconCatalog.IconText(entry.Id), FontFamily = IconAssets.IconFont, FontSize = 24, HorizontalAlignment = HorizontalAlignment.Center };
        var button = new Button
        {
            Content = content, Width = 52, Height = 52, Margin = new Thickness(2), Padding = new Thickness(0), MinWidth = 0,
            ToolTip = entry.Name, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
        };
        button.Click += (_, _) => Pick(entry.Id);
        return button;
    }

    void Pick(string id)
    {
        Result = id;
        DialogResult = true;
    }
}
