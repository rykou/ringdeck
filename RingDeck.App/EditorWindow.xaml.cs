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

    /// <summary>Developer aid for `--shot-editor`: pictures of the icon picker and of a colour wheel.</summary>
    internal async Task SavePickerShots(string prefix)
    {
        var picker = new IconPickerWindow { Owner = this, Left = 60, Top = 60, WindowStartupLocation = WindowStartupLocation.Manual };
        picker.Show();
        await Task.Delay(1200);
        Shot((FrameworkElement)picker.Content, prefix + "-picker.png");
        picker.ShowBrands();
        await Task.Delay(800);
        Shot((FrameworkElement)picker.Content, prefix + "-brands.png");
        picker.Close();
        var apps = new AppPickerWindow { Owner = this, Left = 60, Top = 60, WindowStartupLocation = WindowStartupLocation.Manual };
        apps.Show();
        await Task.Delay(2500);
        Shot((FrameworkElement)apps.Content, prefix + "-apps.png");
        apps.Close();
        var wheel = new ColorWheel { Width = 220, Height = 220 };
        wheel.Show(System.Windows.Media.Color.FromRgb(0x4C, 0xC2, 0xFF));
        var host = new Border { Child = wheel, Padding = new Thickness(14), Background = Theme.Frozen(System.Windows.Media.Color.FromRgb(0x14, 0x14, 0x1A)) };
        host.Measure(new Size(260, 260));
        host.Arrange(new Rect(0, 0, 248, 248));
        Shot(host, prefix + "-wheel.png");
        if (Environment.GetEnvironmentVariable("RINGDECK_SHOT_IMAGE") is { Length: > 0 } image && File.Exists(image))
        {
            var crop = new ImageCropWindow(File.ReadAllBytes(image)) { Owner = this, Left = 60, Top = 60, WindowStartupLocation = WindowStartupLocation.Manual };
            crop.Show();
            await Task.Delay(500);
            var round = crop.ZoomAndCrop(2.2, 250, 120);
            await Task.Delay(300);
            Shot((FrameworkElement)crop.Content, prefix + "-crop.png");
            File.WriteAllBytes(prefix + "-crop-result.png", round);
            crop.Close();
        }
    }

    static void Shot(FrameworkElement root, string path)
    {
        root.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)root.ActualWidth + 32, (int)root.ActualHeight + 32, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        var backdrop = new System.Windows.Media.DrawingVisual();
        using (var dc = backdrop.RenderOpen())
            dc.DrawRectangle(Theme.Frozen(System.Windows.Media.Color.FromRgb(0x20, 0x20, 0x20)), null, new Rect(0, 0, root.ActualWidth + 32, root.ActualHeight + 32));
        bitmap.Render(backdrop);
        bitmap.Render(root);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

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
        if (selectSlice && (_ring?.Slices.FirstOrDefault(s => s.Action is WebhookAction) ?? _ring?.Slices.FirstOrDefault(s => s.Action is AiChatAction) ?? _ring?.Slices.FirstOrDefault(s => s.Action is MacroAction)) is { } slice) ShowSlice(slice);
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
        Closing += (_, _) => { _app.Controller.Capture = null; Flush(); };
        settingsPanel.DataContext = Config.Settings;
        excludedBox.Text = string.Join(Environment.NewLine, Config.Settings.ExcludedApps);
        connectionList.ItemsSource = Config.AiConnections;
        runningBox.ItemsSource = new[] { RunningPrompt };
        runningBox.SelectedIndex = 0;
        onlyInRunning.ItemsSource = new[] { RunningPrompt };
        onlyInRunning.SelectedIndex = 0;
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
        onlyInBox.Text = _ring is null ? "" : string.Join(Environment.NewLine, _ring.OnlyIn);
        ringIconHost.Content = _ring is not { } ring ? null : IconRow(() => ring.Icon, v => ring.Icon = v, ring.Color, ring.Name, () => ShowSlice(null));
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

    void OnAutostart(object sender, RoutedEventArgs e)
    {
        if (Autostart.Set(autostartBox.IsChecked == true)) return;
        autostartBox.IsChecked = Autostart.IsEnabled();
        MessageBox.Show(this, "Windows has RingDeck's start-up switched off. Turn it on in Settings > Apps > Startup.", "RingDeck",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    void OnOpenBackups(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_app.Store.BackupsDir);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_app.Store.BackupsDir) { UseShellExecute = true })?.Dispose();
    }

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

    static List<string> ParsePrograms(string text) =>
        text.Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    void OnExcludedChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        Config.Settings.ExcludedApps = ParsePrograms(excludedBox.Text);
    }

    void OnOnlyInChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || !IsLoaded || _ring is null) return;
        _ring.OnlyIn = ParsePrograms(onlyInBox.Text);
    }

    /// <summary>Fills a "program that is open now" drop-down (Game mode list or a ring's program list).</summary>
    void OnRunningOpened(object sender, EventArgs e)
    {
        if (sender is not ComboBox box) return;
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
        box.ItemsSource = new[] { RunningPrompt }.Concat(names).ToList();
        box.SelectedIndex = 0;
        _loading = false;
    }

    /// <summary>Adds the picked program to the list box that belongs to this drop-down.</summary>
    void OnRunningPicked(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || !IsLoaded || sender is not ComboBox box || box.SelectedIndex <= 0 || box.SelectedItem is not string name) return;
        var list = ReferenceEquals(box, onlyInRunning) ? onlyInBox : excludedBox;
        if (!ParsePrograms(list.Text).Contains(name, StringComparer.OrdinalIgnoreCase))
            list.Text = (list.Text.TrimEnd() + Environment.NewLine + name).Trim();
        _loading = true;
        box.SelectedIndex = 0;
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

    public const string PreviewHintText =
        "Click a slice to edit it, or the centre for ring settings. Drag a slice to reorder it, or onto a ring in the list to move it there (Ctrl copies).";
    const string SliceFormat = "RingDeck.Slice";
    Point _dragStart;
    bool _pressed;                       // the mouse went down on the preview and no drag has started
    ListBoxItem? _dropTarget;
    DispatcherTimer? _hintTimer;

    Slice? SliceAt(Point p)
    {
        if (_ring is null) return null;
        var index = RingGeometry.SliceAt(p.X - preview.ActualWidth / 2, p.Y - preview.ActualHeight / 2, _ring.Slices.Count, 44);
        return index < 0 ? null : _ring.Slices[index];
    }

    void OnPreviewDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(preview);
        _dragging = SliceAt(_dragStart);
        _pressed = true;
    }

    /// <summary>Pressing on a slice and moving starts a real drag, so the slice can leave the preview for the ring list.</summary>
    void OnPreviewMove(object sender, MouseEventArgs e)
    {
        if (_dragging is null || e.LeftButton != MouseButtonState.Pressed || _ring is null) return;
        var d = e.GetPosition(preview) - _dragStart;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance * 2 && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance * 2) return;
        var slice = _dragging;
        _dragging = null;
        _pressed = false;
        // While the drag lasts, the slice fades in the preview and a picture of it follows the pointer.
        var ghost = new DragGhost(slice);
        void Follow(object? s, GiveFeedbackEventArgs a) => ghost.FollowPointer();
        preview.Faded = _ring.Slices.IndexOf(slice);
        preview.InvalidateVisual();
        ghost.Show();
        ghost.FollowPointer();
        preview.GiveFeedback += Follow;
        try { DragDrop.DoDragDrop(preview, new DataObject(SliceFormat, slice.Id.ToString()), DragDropEffects.Move | DragDropEffects.Copy); }
        finally
        {
            preview.GiveFeedback -= Follow;
            ghost.Close();
            preview.Faded = -1;
            ClearDropTarget();
            RefreshPreview();
        }
    }

    void OnPreviewUp(object sender, MouseButtonEventArgs e)
    {
        if (_pressed) ShowSlice(SliceAt(e.GetPosition(preview)));   // a click (on a slice, or the centre for ring settings)
        _pressed = false;
        _dragging = null;
    }

    Slice? DraggedSlice(DragEventArgs e) =>
        e.Data.GetDataPresent(SliceFormat) && Guid.TryParse(e.Data.GetData(SliceFormat) as string, out var id)
            ? _ring?.Slices.FirstOrDefault(s => s.Id == id) : null;

    static bool CopyKey(DragEventArgs e) => (e.KeyStates & DragDropKeyStates.ControlKey) != 0;

    // ----- dropping inside the preview: reorder -----

    void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DraggedSlice(e) is not null ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    void OnPreviewDrop(object sender, DragEventArgs e)
    {
        if (_ring is null || DraggedSlice(e) is not { } slice) return;
        var target = SliceAt(e.GetPosition(preview));
        if (target is not null && !ReferenceEquals(target, slice))
        {
            var to = _ring.Slices.IndexOf(target);
            _ring.Slices.Remove(slice);
            _ring.Slices.Insert(to, slice);
            Touch();
        }
        ShowSlice(slice);
        e.Handled = true;
    }

    // ----- dropping on the ring list: move (or copy with Ctrl) to that ring -----

    ListBoxItem? RingItemUnder(DragEventArgs e) =>
        ItemsControl.ContainerFromElement(ringList, (DependencyObject)e.OriginalSource) as ListBoxItem;

    void OnRingListDragOver(object sender, DragEventArgs e)
    {
        var item = RingItemUnder(e);
        var ring = item?.DataContext as Ring;
        var ok = DraggedSlice(e) is not null && ring is not null && (CopyKey(e) || !ReferenceEquals(ring, _ring));
        e.Effects = !ok ? DragDropEffects.None : CopyKey(e) ? DragDropEffects.Copy : DragDropEffects.Move;
        if (!ReferenceEquals(item, _dropTarget))
        {
            ClearDropTarget();
            if (ok && item is not null) { _dropTarget = item; item.Background = Theme.Frozen(Theme.Accent, 0.28); }
        }
        e.Handled = true;
    }

    void OnRingListDragLeave(object sender, DragEventArgs e) => ClearDropTarget();

    void ClearDropTarget()
    {
        if (_dropTarget is null) return;
        _dropTarget.ClearValue(BackgroundProperty);
        _dropTarget = null;
    }

    void OnRingListDrop(object sender, DragEventArgs e)
    {
        ClearDropTarget();
        e.Handled = true;
        if (_ring is null || DraggedSlice(e) is not { } slice || RingItemUnder(e)?.DataContext is not Ring target) return;
        var copy = CopyKey(e);
        if (!copy && ReferenceEquals(target, _ring)) return;
        if (SliceMover.Transfer(Config, _ring, slice, target, copy) is { } refused)
        {
            MessageBox.Show(this, refused, "RingDeck", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        ShowSlice(copy ? _slice : null);
        Touch();
        Hint($"{(copy ? "Copied" : "Moved")} \"{slice.Label}\" to \"{target.Name}\".");
    }

    /// <summary>Shows a message where the preview hint is, for a few seconds.</summary>
    void Hint(string text)
    {
        previewHint.Text = text;
        _hintTimer?.Stop();
        _hintTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _hintTimer.Tick += (_, _) => { _hintTimer?.Stop(); previewHint.Text = PreviewHintText; };
        _hintTimer.Start();
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
            if (RingPicker.Clash(Config, _ring) is { } clash)
                message = _ring.OnlyIn.Count == 0
                    ? $"\"{clash.Name}\" already uses this shortcut everywhere. Only the first of them in the list opens; give one of them programs to tell them apart."
                    : $"\"{clash.Name}\" uses this shortcut in the same program. Only the first of them in the list opens there.";
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
