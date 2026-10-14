using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RingDeck.App;

/// <summary>Lists the apps in the Start menu (their shortcuts) with icons; picking one closes with it in Result.</summary>
public sealed class AppPickerWindow : Window
{
    public sealed record InstalledApp(string Name, string Path);

    static readonly string[] Noise = { "uninstall", "readme", "read me", "help", "website", "documentation", "license", "release notes", "manual" };
    readonly TextBox _search = new() { Margin = new Thickness(0, 0, 0, 10), ToolTip = "Search apps" };
    readonly ListBox _list = new();
    readonly List<(InstalledApp App, Image Icon, ListBoxItem Item)> _rows = new();

    public InstalledApp? Result { get; private set; }

    public AppPickerWindow()
    {
        Title = "Choose an installed app";
        Width = 460;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        foreach (var app in InstalledApps())
        {
            var icon = new Image { Width = 24, Height = 24, Margin = new Thickness(0, 0, 10, 0) };
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(icon);
            line.Children.Add(new TextBlock { Text = app.Name, VerticalAlignment = VerticalAlignment.Center });
            var item = new ListBoxItem { Content = line, Tag = app, ToolTip = app.Path };
            _rows.Add((app, icon, item));
            _list.Items.Add(item);
        }
        _search.TextChanged += (_, _) =>
        {
            var q = _search.Text.Trim();
            foreach (var (app, _, item) in _rows)
                item.Visibility = q.Length == 0 || app.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        };
        _list.MouseDoubleClick += (_, _) => Pick();
        _list.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Pick(); };

        var choose = new Button { Content = "Use this app", IsDefault = true };
        choose.Click += (_, _) => Pick();
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(choose);
        buttons.Children.Add(cancel);
        var root = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(_search, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(_search);
        root.Children.Add(buttons);
        root.Children.Add(_list);
        Content = root;
        Loaded += (_, _) => { _search.Focus(); LoadIconsInBackground(); };
    }

    void Pick()
    {
        if (_list.SelectedItem is not ListBoxItem { Tag: InstalledApp app }) return;
        Result = app;
        DialogResult = true;
    }

    /// <summary>Start menu shortcuts for all users and for this user, minus uninstallers and readmes.</summary>
    public static List<InstalledApp> InstalledApps()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        };
        var apps = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Where(Directory.Exists))
        {
            IEnumerable<string> links;
            try { links = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories).ToList(); }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException) { continue; }
            foreach (var link in links)
            {
                var name = Path.GetFileNameWithoutExtension(link);
                if (Noise.Any(n => name.Contains(n, StringComparison.OrdinalIgnoreCase))) continue;
                apps.TryAdd(name, new InstalledApp(name, link));
            }
        }
        return apps.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Icons take a moment each, so they are read on a worker thread and filled in as they arrive.</summary>
    void LoadIconsInBackground()
    {
        var work = _rows.Select(r => (r.App.Path, r.Icon)).ToList();
        Task.Run(() =>
        {
            foreach (var (path, image) in work)
            {
                ImageSource? source = null;
                try
                {
                    using var icon = System.Drawing.Icon.ExtractAssociatedIcon(RingDeck.Windows.IconStore.ResolveShortcut(path));
                    if (icon is not null)
                    {
                        var bitmap = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        bitmap.Freeze();
                        source = bitmap;
                    }
                }
                catch (Exception) { }
                if (source is not null) Dispatcher.BeginInvoke(() => image.Source = source);
            }
        });
    }
}
