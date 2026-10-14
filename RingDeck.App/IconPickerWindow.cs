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
        _emojiTab.Click += (_, _) => SwitchTo(emoji: true);
        _iconTab.Click += (_, _) => SwitchTo(emoji: false);

        var tabs = new StackPanel { Orientation = Orientation.Horizontal };
        tabs.Children.Add(_emojiTab);
        tabs.Children.Add(_iconTab);

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

        Loaded += (_, _) => { SwitchTo(emoji: true); _search.Focus(); };
    }

    bool EmojiMode => _emojiTab.IsChecked == true;

    void SwitchTo(bool emoji)
    {
        _emojiTab.IsChecked = emoji;
        _iconTab.IsChecked = !emoji;
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
        var source = EmojiMode ? IconAssets.Emoji : IconAssets.Icons;
        var found = IconCatalog.Search(source, _search.Text, EmojiMode ? _group : null, MaxShown + 1);
        _grid.Children.Clear();
        foreach (var entry in found.Take(MaxShown)) _grid.Children.Add(Cell(entry));
        _count.Text = found.Count == 0 ? "Nothing found. Try another word."
            : found.Count > MaxShown ? $"Showing the first {MaxShown}. Type to narrow it down."
            : $"{found.Count} {(EmojiMode ? "emoji" : "icons")}";
        _scroller.ScrollToTop();
    }

    Button Cell(IconEntry entry)
    {
        object content = entry.Id.StartsWith("emoji:", StringComparison.Ordinal)
            ? new Image { Source = IconAssets.EmojiPicture(entry.Id), Width = 32, Height = 32 }
            : new TextBlock { Text = IconCatalog.IconText(entry.Id), FontFamily = IconAssets.IconFont, FontSize = 24, HorizontalAlignment = HorizontalAlignment.Center };
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
