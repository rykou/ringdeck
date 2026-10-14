using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using RingDeck.Core;
using RingDeck.Windows;

namespace RingDeck.App;

public partial class EditorWindow : Window
{
    readonly App _app = App.Instance;
    readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    Ring? _ring;
    Slice? _slice;
    Slice? _dragging;
    bool _loading, _touchQueued;

    AppConfig Config => _app.Config;

    int _touchCount;

    /// <summary>Developer aid for `--shot-editor`: a picture of the connection dialog (opened without blocking, then closed).</summary>
    internal async Task SaveConnectionShot(AiConnection connection, string path)
    {
        var dialog = new ConnectionWindow(connection) { Owner = this, Left = 40, Top = 40, WindowStartupLocation = WindowStartupLocation.Manual };
        dialog.Show();
        await Task.Delay(2500);                            // let it list the service's models
        dialog.UpdateLayout();
        var root = (FrameworkElement)dialog.Content;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)root.ActualWidth + 36, (int)root.ActualHeight + 36, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        var backdrop = new System.Windows.Media.DrawingVisual();
        using (var dc = backdrop.RenderOpen())
            dc.DrawRectangle(Theme.Frozen(System.Windows.Media.Color.FromRgb(0x20, 0x20, 0x20)), null, new Rect(0, 0, root.ActualWidth + 36, root.ActualHeight + 36));
        bitmap.Render(backdrop);
        bitmap.Render(root);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var file = File.Create(path)) encoder.Save(file);
        dialog.Close();
    }

    /// <summary>Developer aid for `--shot-editor`: opens the first drop-down of the ring panel and reports, after a
    /// moment, whether it stayed open and how many edits the editor thought happened (should be none).</summary>
    internal void ProbeDropDown(string path, Action done)
    {
        ShowSlice(null);
        UpdateLayout();
        var combo = ringPanel.Children.OfType<ComboBox>().First();
        var before = _touchCount;
        combo.IsDropDownOpen = true;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            File.WriteAllText(path, $"dropdown open after 1.5s: {combo.IsDropDownOpen}; edits registered: {_touchCount - before}\n");
            combo.IsDropDownOpen = false;
            done();
        };
        timer.Start();
    }

    /// <summary>Developer aid for `--shot-editor`: saves the window's content as a PNG, optionally with a slice selected.</summary>
    internal void SaveShot(string path, bool selectSlice)
    {
        if (selectSlice && (_ring?.Slices.FirstOrDefault(s => s.Action is AiChatAction) ?? _ring?.Slices.FirstOrDefault(s => s.Action is MacroAction)) is { } slice) ShowSlice(slice);
        UpdateLayout();
        var root = (FrameworkElement)Content;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)root.ActualWidth + 32, (int)root.ActualHeight + 32, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        var backdrop = new System.Windows.Media.DrawingVisual();
        using (var dc = backdrop.RenderOpen())
            dc.DrawRectangle(Theme.Frozen(Theme.IsDark ? System.Windows.Media.Color.FromRgb(0x20, 0x20, 0x20) : System.Windows.Media.Color.FromRgb(0xF3, 0xF3, 0xF3)), null, new Rect(0, 0, root.ActualWidth + 32, root.ActualHeight + 32));
        bitmap.Render(backdrop);
        bitmap.Render(root);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    public EditorWindow()
    {
        InitializeComponent();
        preview.FindRing = id => Config.Find(id);
        _saveTimer.Tick += (_, _) => Flush();
        Closing += (_, _) => { StopRecording(); _app.Controller.Capture = null; Flush(); };
        settingsPanel.DataContext = Config.Settings;
        excludedBox.Text = string.Join(Environment.NewLine, Config.Settings.ExcludedApps);
        connectionList.ItemsSource = Config.AiConnections;
        runningBox.ItemsSource = new[] { RunningPrompt };
        runningBox.SelectedIndex = 0;
        autostartBox.IsChecked = Autostart.IsEnabled();
        RefreshRings(Config.Rings.FirstOrDefault());
    }

    // ----- ring list -----

    void RefreshRings(Ring? select)
    {
        _loading = true;
        var filter = search.Text.Trim();
        ringList.ItemsSource = Config.Rings.Where(r => r.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)).ToList();
        ringList.SelectedItem = select;
        _loading = false;
        SelectRing(select);
    }

    void OnSearch(object sender, TextChangedEventArgs e) { if (IsLoaded) RefreshRings(_ring); }

    void OnRingSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading) SelectRing(ringList.SelectedItem as Ring);
        e.Handled = true;
    }

    void SelectRing(Ring? ring)
    {
        _ring = ring;
        ShowSlice(null);
    }

    void OnAddRing(object sender, RoutedEventArgs e)
    {
        var ring = new Ring { Slices = { new Slice { Label = "First slice", Action = new PasteTextAction { Text = "Hello" } } } };
        Config.Rings.Add(ring);
        search.Text = "";
        RefreshRings(ring);
        Touch();
    }

    void OnDuplicateRing(object sender, RoutedEventArgs e)
    {
        if (_ring is null) return;
        var copy = ConfigTransfer.Duplicate(_ring);
        Config.Rings.Insert(Config.Rings.IndexOf(_ring) + 1, copy);
        RefreshRings(copy);
        Touch();
    }

    void OnDeleteRing(object sender, RoutedEventArgs e)
    {
        if (_ring is null) return;
        var answer = MessageBox.Show(this, $"Delete the ring \"{_ring.Name}\" and its {_ring.Slices.Count} slices?",
            "RingDeck", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.OK) return;
        Config.Rings.Remove(_ring);
        RefreshRings(Config.Rings.FirstOrDefault());
        Touch();
    }

    // ----- panels -----

    /// <summary>Shows the slice panel for a slice, or the ring panel when null.</summary>
    internal void ShowSlice(Slice? slice)
    {
        _loading = true;
        _slice = slice;
        ringPanel.DataContext = null;
        ringPanel.DataContext = _ring;
        ringPanel.Visibility = _ring is not null && slice is null ? Visibility.Visible : Visibility.Collapsed;
        slicePanel.Visibility = slice is not null ? Visibility.Visible : Visibility.Collapsed;
        settingsPanel.Visibility = Visibility.Collapsed;
        LoadSlicePanel();
        _loading = false;
        UpdateWarnings();
        RefreshPreview();
    }

    void OnShowSettings(object sender, RoutedEventArgs e)
    {
        connectionList.Items.Refresh();
        ringPanel.Visibility = slicePanel.Visibility = Visibility.Collapsed;
        settingsPanel.Visibility = Visibility.Visible;
    }

    void OnAutostart(object sender, RoutedEventArgs e) => Autostart.Set(autostartBox.IsChecked == true);

    // ----- AI connections -----

    void OnNewConnection(object sender, RoutedEventArgs e)
    {
        var c = new AiConnection { Name = "AI " + (Config.AiConnections.Count + 1) };
        if (!EditConnection(c)) return;
        Config.AiConnections.Add(c);
        connectionList.Items.Refresh();
        Touch();
    }

    void OnEditConnection(object sender, RoutedEventArgs e)
    {
        if (connectionList.SelectedItem is AiConnection c && EditConnection(c)) { connectionList.Items.Refresh(); Touch(); }
    }

    void OnDeleteConnection(object sender, RoutedEventArgs e)
    {
        if (connectionList.SelectedItem is not AiConnection c) return;
        var users = Config.Rings.SelectMany(r => r.Slices).Count(s => s.Action is AiChatAction a && a.ConnectionId == c.Id);
        var question = users == 0 ? $"Delete the connection \"{c.Name}\"?" : $"Delete \"{c.Name}\"? {users} slice(s) use it and will need another connection.";
        if (MessageBox.Show(this, question, "RingDeck", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        Config.AiConnections.Remove(c);
        connectionList.Items.Refresh();
        Touch();
    }

    // ----- programs where rings are off -----

    const string RunningPrompt = "Add a program that is open now…";

    void OnExcludedChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        Config.Settings.ExcludedApps = excludedBox.Text.Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    void OnRunningOpened(object sender, EventArgs e)
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in System.Diagnostics.Process.GetProcesses())
        {
            try
            {
                if (process.MainWindowHandle != IntPtr.Zero && process.Id != Environment.ProcessId) names.Add(process.ProcessName + ".exe");
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            finally { process.Dispose(); }
        }
        _loading = true;
        runningBox.ItemsSource = new[] { RunningPrompt }.Concat(names).ToList();
        runningBox.SelectedIndex = 0;
        _loading = false;
    }

    void OnRunningPicked(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || !IsLoaded || runningBox.SelectedIndex <= 0 || runningBox.SelectedItem is not string name) return;
        if (!Config.Settings.ExcludedApps.Contains(name, StringComparer.OrdinalIgnoreCase))
            excludedBox.Text = (excludedBox.Text.TrimEnd() + Environment.NewLine + name).Trim();
        _loading = true;
        runningBox.SelectedIndex = 0;
        _loading = false;
    }

    // ----- preview -----

    internal void RefreshPreview()
    {
        preview.Ring = _ring;
        if (_ring is not null)
        {
            preview.Radius = RingGeometry.EffectiveRadius(_ring.Radius, _ring.Slices.Count, RingSession.SliceSize);
            preview.Width = preview.Height = (preview.Radius + RingSession.SliceSize + 44) * 2;   // tighter than the overlay: nothing animates here
            preview.Animator.ShowStatic(_ring.Slices.Count);
            preview.Selected = _slice is null ? -1 : _ring.Slices.IndexOf(_slice);
            if (preview.Selected >= 0)
                preview.Animator.Tick(0.001, RingGeometry.SliceAngle(preview.Selected, _ring.Slices.Count), preview.Selected, false, true, false);
        }
        preview.InvalidateVisual();
    }

    Slice? SliceUnder(MouseEventArgs e)
    {
        if (_ring is null) return null;
        var p = e.GetPosition(preview);
        var index = RingGeometry.SliceAt(p.X - preview.ActualWidth / 2, p.Y - preview.ActualHeight / 2, _ring.Slices.Count, 44);
        return index < 0 ? null : _ring.Slices[index];
    }

    void OnPreviewDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = SliceUnder(e);
        preview.CaptureMouse();
    }

    void OnPreviewUp(object sender, MouseButtonEventArgs e)
    {
        preview.ReleaseMouseCapture();
        var target = SliceUnder(e);
        if (_ring is not null && _dragging is not null && target is not null && !ReferenceEquals(target, _dragging))
        {
            var to = _ring.Slices.IndexOf(target);
            _ring.Slices.Remove(_dragging);
            _ring.Slices.Insert(to, _dragging);
            ShowSlice(_dragging);
            Touch();
        }
        else ShowSlice(target);
        _dragging = null;
    }

    void OnAddSlice(object sender, RoutedEventArgs e)
    {
        if (_ring is null) return;
        var slice = new Slice { Action = new PasteTextAction() };
        _ring.Slices.Add(slice);
        ShowSlice(slice);
        Touch();
    }

    // ----- change tracking -----

    void OnEdited(object sender, RoutedEventArgs e)
    {
        if (_loading || !IsLoaded) return;
        // A drop-down opens and closes through a toggle button inside its template. That is not an edit: treating
        // it as one re-applied the theme, which closed the drop-down, which counted as another edit, in a loop.
        if (e.OriginalSource is System.Windows.Controls.Primitives.ToggleButton and not CheckBox) return;
        Touch();
    }

    /// <summary>Something changed: refresh what depends on it and schedule a save. Deferred so bindings settle first.</summary>
    internal void Touch()
    {
        _touchCount++;
        if (_touchQueued) return;
        _touchQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _touchQueued = false;
            _loading = true;                 // refreshing the list must not count as the user picking another ring
            ringList.Items.Refresh();
            ringList.SelectedItem = _ring;
            _loading = false;
            UpdateWarnings();
            RefreshPreview();
            _app.ConfigChanged();
            _saveTimer.Stop();
            _saveTimer.Start();
        });
    }

    void Flush()
    {
        _saveTimer.Stop();
        try { _app.Store.Save(Config); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _app.Tray.Notify("RingDeck could not save your rings", ex.Message);
        }
    }

    void UpdateWarnings()
    {
        var message = "";
        if (_ring is not null && _ring.Shortcut != "")
        {
            var clash = Config.Rings.FirstOrDefault(r => r != _ring && r.Enabled && string.Equals(r.Shortcut, _ring.Shortcut, StringComparison.OrdinalIgnoreCase));
            if (clash is not null) message = $"\"{clash.Name}\" already uses this shortcut. Only the first ring in the list will open.";
        }
        shortcutWarning.Text = message;
        shortcutWarning.Visibility = message == "" ? Visibility.Collapsed : Visibility.Visible;
    }

    // ----- import / export -----

    void OnExport(object sender, RoutedEventArgs e)
    {
        var all = _ring is null || MessageBox.Show(this, $"Export only \"{_ring.Name}\" (and rings it opens)?\n\nChoose No to export every ring.",
            "RingDeck", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.No;
        var dialog = new SaveFileDialog { Filter = "RingDeck rings (*.json)|*.json", FileName = all ? "ringdeck-rings.json" : _ring!.Name + ".json" };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, ConfigStore.ToJson(all ? ConfigTransfer.ExportAll(Config) : ConfigTransfer.Export(Config, _ring!.Id))); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "RingDeck", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    void OnImport(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "RingDeck rings (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        AppConfig? incoming = null;
        try { incoming = ConfigStore.FromJson(File.ReadAllText(dialog.FileName)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        if (incoming is null || incoming.Rings.Count == 0)
        {
            MessageBox.Show(this, "That file does not contain any RingDeck rings.", "RingDeck", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var added = ConfigTransfer.Import(Config, incoming);
        search.Text = "";
        RefreshRings(added[0]);
        Touch();
    }
}
