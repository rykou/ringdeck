using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using RingDeck.Core;
using RingDeck.Windows;

namespace RingDeck.App;

public partial class EditorWindow
{
    static readonly string[] ActionNames = { "Nothing", "Paste text", "Keystroke macro", "Open app, file or link", "Open another ring" };
    MacroRecorder? _recorder;

    static int ActionIndex(SliceAction? action) => action switch
    {
        PasteTextAction => 1, MacroAction => 2, LaunchAction => 3, NestedRingAction => 4, _ => 0,
    };

    static TextBlock Caption(string text) => new() { Text = text, Style = (Style)Application.Current.Resources["Caption"] };

    static T Bound<T>(T element, DependencyProperty property, object source, string path) where T : FrameworkElement
    {
        element.SetBinding(property, new Binding(path) { Source = source, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        return element;
    }

    void LoadSlicePanel()
    {
        StopRecording();
        slicePanel.Children.Clear();
        if (_slice is not { } slice || _ring is null) return;
        var add = slicePanel.Children;

        var header = new DockPanel();
        var delete = new Button { Content = "Delete slice" };
        delete.Click += (_, _) => { _ring.Slices.Remove(slice); ShowSlice(null); Touch(); };
        DockPanel.SetDock(delete, Dock.Right);
        header.Children.Add(delete);
        header.Children.Add(new TextBlock { Text = "Slice", Style = (Style)Application.Current.Resources["Heading"] });
        add.Add(header);

        add.Add(Caption("Label"));
        add.Add(Bound(new TextBox(), TextBox.TextProperty, slice, nameof(Slice.Label)));
        add.Add(Caption("Icon"));
        add.Add(IconRow(slice));
        add.Add(Caption("Colour"));
        add.Add(Bound(new ColorField(), ColorField.ValueProperty, slice, nameof(Slice.Color)));

        add.Add(Caption("When chosen"));
        var kind = new ComboBox { ItemsSource = ActionNames, SelectedIndex = ActionIndex(slice.Action) };
        kind.SelectionChanged += (_, e) =>
        {
            e.Handled = true;
            if (_loading || kind.SelectedIndex == ActionIndex(slice.Action)) return;
            slice.Action = kind.SelectedIndex switch
            {
                1 => new PasteTextAction(), 2 => new MacroAction(), 3 => new LaunchAction(), 4 => new NestedRingAction(), _ => null,
            };
            ShowSlice(slice);
            Touch();
        };
        add.Add(kind);

        switch (slice.Action)
        {
            case PasteTextAction paste: PasteEditor(paste); break;
            case MacroAction macro: MacroEditor(macro); break;
            case LaunchAction launch: LaunchEditor(launch); break;
            case NestedRingAction nested: NestedEditor(nested); break;
        }
    }

    // ----- icon -----

    UIElement IconRow(Slice slice)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var custom = new TextBox { Width = 60, MaxLength = 4, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Emoji, Segoe UI"), ToolTip = "Type any character or emoji", Margin = new Thickness(0, 0, 8, 0),
            Text = slice.Icon.StartsWith("glyph:", StringComparison.Ordinal) ? slice.Icon[6..] : "" };
        custom.TextChanged += (_, _) => { if (!_loading) slice.Icon = custom.Text.Length > 0 ? "glyph:" + custom.Text : ""; };
        var grid = new UniformGrid { Columns = 10, Margin = new Thickness(8) };
        var popup = new Popup { StaysOpen = false, Placement = PlacementMode.Bottom,
            Child = new Border { Background = (Brush)Application.Current.Resources["StageBrush"], CornerRadius = new CornerRadius(8), Child = grid } };
        foreach (var glyph in Glyphs.All)
        {
            var pick = new Button { Content = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 18,
                Width = 36, Height = 36, Margin = new Thickness(2), Padding = new Thickness(0) };
            pick.Click += (_, _) => { popup.IsOpen = false; slice.Icon = "glyph:" + glyph; ShowSlice(slice); Touch(); };
            grid.Children.Add(pick);
        }
        var symbols = new Button { Content = "Symbols", Margin = new Thickness(0, 0, 8, 0) };
        popup.PlacementTarget = symbols;
        symbols.Click += (_, _) => popup.IsOpen = true;
        var image = new Button { Content = "Image or app", ToolTip = "Use a picture, or the icon of a program or shortcut" };
        image.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = "Images and programs|*.png;*.jpg;*.jpeg;*.ico;*.exe;*.lnk|All files|*.*" };
            if (dialog.ShowDialog(this) != true) return;
            try { slice.Icon = _app.Icons.Import(dialog.FileName); ShowSlice(slice); Touch(); }
            catch (Exception ex) when (ex is System.IO.IOException or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
            {
                MessageBox.Show(this, ex.Message, "RingDeck", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        row.Children.Add(custom);
        row.Children.Add(symbols);
        row.Children.Add(image);
        row.Children.Add(popup);
        return row;
    }

    // ----- action editors -----

    void PasteEditor(PasteTextAction paste)
    {
        slicePanel.Children.Add(Caption("Text to paste"));
        slicePanel.Children.Add(Bound(new TextBox { AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap, MinHeight = 120, MaxHeight = 260,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, TextBox.TextProperty, paste, nameof(PasteTextAction.Text)));
        slicePanel.Children.Add(new TextBlock { Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["SubtleBrush"],
            Text = "Placeholders: {date}  {time}  {datetime}  {clipboard}. Formats work too, like {date:yyyy-MM-dd}. Type {{ for a real brace." });
        slicePanel.Children.Add(Bound(new CheckBox { Content = "Put my clipboard back afterwards", Margin = new Thickness(0, 12, 0, 0) },
            ToggleButton.IsCheckedProperty, paste, nameof(PasteTextAction.RestoreClipboard)));
    }

    void LaunchEditor(LaunchAction launch)
    {
        slicePanel.Children.Add(Caption("App, file, folder or web address"));
        var row = new DockPanel();
        var browse = new Button { Content = "Browse", Margin = new Thickness(6, 0, 0, 0) };
        DockPanel.SetDock(browse, Dock.Right);
        var target = Bound(new TextBox(), TextBox.TextProperty, launch, nameof(LaunchAction.Target));
        browse.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = "All files|*.*" };
            if (dialog.ShowDialog(this) == true) target.Text = dialog.FileName;
        };
        row.Children.Add(browse);
        row.Children.Add(target);
        slicePanel.Children.Add(row);
        slicePanel.Children.Add(Caption("Arguments (optional)"));
        slicePanel.Children.Add(Bound(new TextBox(), TextBox.TextProperty, launch, nameof(LaunchAction.Arguments)));
        slicePanel.Children.Add(Caption("Start in folder (optional)"));
        slicePanel.Children.Add(Bound(new TextBox(), TextBox.TextProperty, launch, nameof(LaunchAction.WorkingFolder)));
    }

    void NestedEditor(NestedRingAction nested)
    {
        slicePanel.Children.Add(Caption("Ring to open"));
        var choices = Config.Rings.Where(r => !RingGraph.WouldCycle(Config, _ring!.Id, r.Id)).ToList();
        var combo = new ComboBox { ItemsSource = choices, DisplayMemberPath = nameof(Ring.Name), SelectedItem = choices.FirstOrDefault(r => r.Id == nested.RingId) };
        combo.SelectionChanged += (_, _) => { if (!_loading && combo.SelectedItem is Ring ring) nested.RingId = ring.Id; };
        slicePanel.Children.Add(combo);
        if (choices.Count == 0)
            slicePanel.Children.Add(new TextBlock { Text = "Create another ring first.", Margin = new Thickness(0, 6, 0, 0),
                Foreground = (Brush)Application.Current.Resources["SubtleBrush"] });
        else if (combo.SelectedItem is null && nested.RingId != Guid.Empty)
            slicePanel.Children.Add(new TextBlock { Text = "The ring this slice opened was deleted. Pick another.", Foreground = Brushes.Orange,
                Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });
    }

    void MacroEditor(MacroAction macro)
    {
        slicePanel.Children.Add(Caption("Steps, played top to bottom. Esc stops a running macro."));
        for (var i = 0; i < macro.Steps.Count; i++) slicePanel.Children.Add(StepRow(macro, i));

        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        void AddButton(string text, Func<MacroStep> make)
        {
            var button = new Button { Content = text, Margin = new Thickness(0, 0, 6, 6) };
            button.Click += (_, _) => { macro.Steps.Add(make()); ShowSlice(_slice); Touch(); };
            buttons.Children.Add(button);
        }
        AddButton("Add keys", () => new KeyStep());
        AddButton("Add text", () => new TextStep());
        AddButton("Add wait", () => new DelayStep());
        var record = new Button { Content = "Record", Margin = new Thickness(0, 0, 6, 6), ToolTip = "Records the keys you press in any window, without their timing" };
        record.Click += (_, _) =>
        {
            if (_recorder is null)
            {
                _recorder = new MacroRecorder();
                record.Content = "Stop recording";
                _app.Controller.Capture = (vk, down) =>
                {
                    if (down) _recorder?.OnKeyDown(vk, InputSender.CurrentMods());
                    return false;                       // keys still reach the app being recorded
                };
            }
            else
            {
                var steps = _recorder.Steps;
                StopRecording();
                macro.Steps.AddRange(steps);
                ShowSlice(_slice);
                Touch();
            }
        };
        buttons.Children.Add(record);
        slicePanel.Children.Add(buttons);
    }

    void StopRecording()
    {
        if (_recorder is null) return;
        _recorder = null;
        _app.Controller.Capture = null;
    }

    UIElement StepRow(MacroAction macro, int index)
    {
        var step = macro.Steps[index];
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        void Tool(string text, string tip, Action act, bool enabled = true)
        {
            var button = new Button { Content = text, ToolTip = tip, Width = 30, Padding = new Thickness(0), Margin = new Thickness(4, 0, 0, 0), IsEnabled = enabled };
            button.Click += (_, _) => { act(); ShowSlice(_slice); Touch(); };
            DockPanel.SetDock(button, Dock.Right);
            row.Children.Add(button);
        }
        Tool("\u2715", "Remove", () => macro.Steps.RemoveAt(index));
        Tool("\u2193", "Move down", () => { macro.Steps.RemoveAt(index); macro.Steps.Insert(index + 1, step); }, index < macro.Steps.Count - 1);
        Tool("\u2191", "Move up", () => { macro.Steps.RemoveAt(index); macro.Steps.Insert(index - 1, step); }, index > 0);
        var label = new TextBlock { Width = 44, VerticalAlignment = VerticalAlignment.Center,
            Text = step switch { KeyStep => "Keys", TextStep => "Text", _ => "Wait" } };
        DockPanel.SetDock(label, Dock.Left);
        row.Children.Add(label);
        row.Children.Add(step switch
        {
            KeyStep key => Bound(new ShortcutBox { AllowBare = true }, ShortcutBox.ValueProperty, key, nameof(KeyStep.Combo)),
            TextStep text => Bound(new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap }, TextBox.TextProperty, text, nameof(TextStep.Text)),
            DelayStep wait => Bound(new TextBox { ToolTip = "Milliseconds" }, TextBox.TextProperty, wait, nameof(DelayStep.Ms)),
            _ => new TextBlock(),
        });
        return row;
    }
}
