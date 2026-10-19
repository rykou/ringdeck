using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using RingDeck.Core;
using RingDeck.Windows;

namespace RingDeck.App;

/// <summary>Where a macro's step list is shown: the slice panel, the steps window, or a flow card's side panel.</summary>
public enum MacroPlace { Slice, Window, Card }

public partial class EditorWindow
{
    static readonly string[] ActionNames = { "Nothing", "Paste text", "Macro / workflow", "Open app, file or link", "Open another ring", "AI chat", "Send to a web service (Discord, Slack, phone…)", "Flow: linked actions on a canvas" };

    static int ActionIndex(SliceAction? action) => action switch
    {
        PasteTextAction => 1, MacroAction => 2, LaunchAction => 3, NestedRingAction => 4, AiChatAction => 5, WebhookAction => 6, FlowAction => 7, _ => 0,
    };

    static TextBlock Caption(string text) => new() { Text = text, Style = (Style)Application.Current.Resources["Caption"] };

    static T Bound<T>(T element, DependencyProperty property, object source, string path) where T : FrameworkElement
    {
        element.SetBinding(property, new Binding(path) { Source = source, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        return element;
    }

    void LoadSlicePanel()
    {
        CloseMacroWindowUnlessFor(_slice);
        CloseFlowWindowUnlessFor(_slice);
        slicePanel.Children.Clear();
        if (_slice is not { } slice || _ring is null) return;
        var add = slicePanel.Children;

        var header = new DockPanel();
        var delete = new Button { Content = "Delete", ToolTip = "Delete this slice" };
        delete.Click += (_, _) => { _ring.Slices.Remove(slice); ShowSlice(null); Touch(); };
        var duplicate = new Button { Content = "Duplicate", ToolTip = "Add a copy of this slice next to it", Margin = new Thickness(0, 0, 6, 0) };
        duplicate.Click += (_, _) =>
        {
            var copy = ConfigTransfer.DuplicateSlice(slice);
            _ring.Slices.Insert(_ring.Slices.IndexOf(slice) + 1, copy);
            ShowSlice(copy);
            Touch();
        };
        DockPanel.SetDock(delete, Dock.Right);
        DockPanel.SetDock(duplicate, Dock.Right);
        header.Children.Add(delete);
        header.Children.Add(duplicate);
        header.Children.Add(new TextBlock { Text = "Slice", Style = (Style)Application.Current.Resources["Heading"] });
        add.Add(header);

        add.Add(Caption("Label"));
        add.Add(Bound(new TextBox(), TextBox.TextProperty, slice, nameof(Slice.Label)));
        add.Add(Caption("Icon"));
        add.Add(IconRow(() => slice.Icon, v => slice.Icon = v, slice.Color, slice.Label, () => ShowSlice(slice)));
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
                1 => new PasteTextAction(), 2 => new MacroAction(), 3 => new LaunchAction(), 4 => new NestedRingAction(), 5 => NewAiAction(), 6 => new WebhookAction(), 7 => FlowAction.New(), _ => null,
            };
            // Sending to other people is the action most worth guarding against a slip of the mouse.
            if (slice.Action is WebhookAction && slice.ConfirmMs == 0) slice.ConfirmMs = 1000;
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
            case AiChatAction ai: AiEditor(ai); break;
            case WebhookAction hook: WebhookEditor(hook); break;
            case FlowAction flow: FlowEditor(flow); break;
        }
        if (slice.Action is not null) ConfirmEditor(slice);
    }

    // ----- icon -----

    /// <summary>Icon buttons for a slice or a ring: a preview, the picker, an image, or a typed character.</summary>
    internal UIElement IconRow(Func<string> get, Action<string> set, string fill, string label, Action refresh)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var preview = new IconPreview { Icon = get(), Fill = fill, Label = label, Width = 40, Height = 40, Margin = new Thickness(0, 0, 10, 0) };
        var choose = new Button { Content = "Emoji & icons…", Margin = new Thickness(0, 0, 8, 0), ToolTip = "Pick from about 1,600 emoji and 2,500 icons" };
        choose.Click += (_, _) =>
        {
            var picker = new IconPickerWindow { Owner = this };
            if (picker.ShowDialog() != true || picker.Result is null) return;
            set(picker.Result);
            refresh();
            Touch();
        };
        var image = new Button { Content = "Image…", ToolTip = "Use a picture, or the icon of a program or shortcut", Margin = new Thickness(0, 0, 8, 0) };
        image.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = "Images and programs|*.png;*.jpg;*.jpeg;*.ico;*.exe;*.lnk|All files|*.*" };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                // Pictures and program icons go through the crop editor first.
                var crop = new ImageCropWindow(IconStore.PictureBytes(dialog.FileName)) { Owner = this };
                if (crop.ShowDialog() != true || crop.Result is not { } result) return;
                set(_app.Icons.ImportPng(result.Round ? result.Png : ToPng(result.Png), result.Round));
                refresh();
                Touch();
            }
            catch (Exception ex) when (ex is System.IO.IOException or InvalidOperationException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Runtime.InteropServices.COMException)
            {
                MessageBox.Show(this, ex.Message, "RingDeck", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        var custom = new TextBox
        {
            Width = 48, MaxLength = 4, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Or type any character to use as the icon",
            Text = get().StartsWith("glyph:", StringComparison.Ordinal) ? get()[6..] : "",
        };
        custom.TextChanged += (_, _) =>
        {
            if (_loading) return;
            set(custom.Text.Length > 0 ? "glyph:" + custom.Text : "");
            preview.Icon = get();
        };
        preview.VerticalAlignment = choose.VerticalAlignment = image.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(preview);
        row.Children.Add(choose);
        row.Children.Add(image);
        row.Children.Add(custom);
        return row;
    }

    static readonly (int Ms, string Text)[] ConfirmChoices =
    {
        (0, "No: it runs as soon as it's chosen"), (500, "Half a second"), (1000, "1 second"), (1500, "1.5 seconds"),
        (2000, "2 seconds"), (3000, "3 seconds"), (5000, "5 seconds"),
    };

    /// <summary>The hover wait: how long the pointer must rest on the slice before it can run.</summary>
    void ConfirmEditor(Slice slice)
    {
        slicePanel.Children.Add(Caption("Wait before it can run"));
        var known = Array.FindIndex(ConfirmChoices, c => c.Ms == slice.ConfirmMs);
        var items = ConfirmChoices.Select(c => c.Text).ToList();
        if (known < 0) items.Add($"{slice.ConfirmMs / 1000.0:0.#} seconds");
        var combo = new ComboBox { ItemsSource = items, SelectedIndex = known >= 0 ? known : items.Count - 1 };
        combo.SelectionChanged += (_, _) =>
        {
            if (_loading || combo.SelectedIndex < 0 || combo.SelectedIndex >= ConfirmChoices.Length) return;
            slice.ConfirmMs = ConfirmChoices[combo.SelectedIndex].Ms;
        };
        slicePanel.Children.Add(combo);
        slicePanel.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["SubtleBrush"],
            Text = "Guards against choosing it by mistake. A circle fills around the slice while you point at it, and it only runs once the circle is full. Let go of the shortcut too early and the ring closes without running it.",
        });
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
        var installed = new Button { Content = "Installed app…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0),
            ToolTip = "Pick from the apps in your Start menu: fills in the target, the name and the icon" };
        installed.Click += (_, _) =>
        {
            var picker = new AppPickerWindow { Owner = this };
            if (picker.ShowDialog() != true || picker.Result is not { } app || _slice is not { } slice) return;
            launch.Target = app.Path;
            if (slice.Label.Trim().Length == 0 || slice.Label == "New slice") slice.Label = app.Name;
            slice.Icon = IconForApp(app);
            ShowSlice(slice);
            Touch();
        };
        slicePanel.Children.Add(installed);
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

    MacroWindow? _macroWindow;
    FlowWindow? _flowWindow;

    /// <summary>The window that dialogs (the recorder, web request settings, questions) belong to: the one in front.</summary>
    Window TopWindow => (Window?)_flowWindow ?? (Window?)_macroWindow ?? this;

    // ----- flows -----

    void FlowEditor(FlowAction flow)
    {
        var cards = flow.Nodes.Count(n => n.Kind == FlowNodeKind.Action);
        slicePanel.Children.Add(Caption($"Flow: {cards} cards, {flow.Wires.Count} links"));
        if (FlowGraph.Problem(flow) is { } problem)
            slicePanel.Children.Add(new TextBlock { Text = problem, Foreground = Brushes.Orange, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        var open = new Button
        {
            Content = _flowWindow?.Flow == flow ? "Show the flow editor" : "Open the flow editor", HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0), ToolTip = "Link actions on a canvas: branches run at the same time",
        };
        open.Click += (_, _) => OpenFlow(flow);
        slicePanel.Children.Add(open);
        slicePanel.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["SubtleBrush"],
            Text = "Cards run from Start along their links. Several links out of a card run at the same time; at least one branch must end at a Stop.",
        });
    }

    void OpenFlow(FlowAction flow)
    {
        if (_flowWindow is { } open && open.Flow == flow) { open.WindowState = WindowState.Normal; open.Activate(); return; }
        if (_slice is not { } slice) return;
        var window = new FlowWindow(this, slice, flow);
        _flowWindow = window;
        window.Closed += (_, _) => { if (_flowWindow != window) return; _flowWindow = null; ShowSlice(_slice); };
        window.Refresh();
        window.Show();
        ShowSlice(_slice);
    }

    void CloseFlowWindowUnlessFor(Slice? slice)
    {
        if (_flowWindow is not { } open || (slice is not null && slice.Action == open.Flow)) return;
        _flowWindow = null;
        open.Close();
    }

    /// <summary>The flow's cards or links changed: save, and update the slice panel's summary.</summary>
    internal void FlowChanged()
    {
        Touch();
        ShowSlice(_slice);
    }

    /// <summary>Records steps for a flow card (the recorder belongs to <paramref name="owner"/>).</summary>
    internal List<MacroStep>? RecordFor(Window owner)
    {
        var window = new RecorderWindow(movementOnly: false) { Owner = owner };
        return window.ShowDialog() == true && window.Result is { Count: > 0 } steps ? steps : null;
    }

    /// <summary>Fills a panel without its new fields counting as edits.</summary>
    internal void BuildQuietly(Panel host, Action build)
    {
        var was = _loading;
        _loading = true;
        try { host.Children.Clear(); build(); }
        finally { _loading = was; }
    }

    /// <summary>A flow card's steps, in the flow window's side panel.</summary>
    internal void BuildMacroInto(Panel host, MacroAction steps) => BuildMacro(host, steps, MacroPlace.Card);

    /// <summary>Developer aid (--shot-editor): opens the first flow slice's canvas, saves a picture of it, closes it.</summary>
    internal async Task SaveFlowWindowShot(string path)
    {
        if (_ring?.Slices.FirstOrDefault(s => s.Action is FlowAction) is not { Action: FlowAction flow } slice) return;
        ShowSlice(slice);
        OpenFlow(flow);
        if (_flowWindow is not { } window) return;
        await Task.Delay(600);
        // One picture per card, so every card's side panel can be checked.
        var cards = window.ActionCards;
        for (var i = 0; i < cards.Count; i++)
        {
            window.SelectCard(cards[i]);
            await Task.Delay(400);
            SaveWindow(window, Path.ChangeExtension(path, null) + $"-{i + 1}-{FlowText.Kind(cards[i]).ToString().ToLowerInvariant()}.png");
        }
        window.SelectBusiestCard();
        await Task.Delay(600);
        SaveWindow(window, path);
        window.Close();
    }

    static void SaveWindow(Window window, string path)
    {
        window.UpdateLayout();
        var root = (FrameworkElement)window.Content;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var backdrop = new DrawingVisual();
        using (var dc = backdrop.RenderOpen())
            dc.DrawRectangle(Theme.Frozen(Theme.IsDark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3)), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        bitmap.Render(backdrop);
        bitmap.Render(root);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var file = File.Create(path)) encoder.Save(file);
    }

    /// <summary>The steps changed shape (added, removed, moved): rebuild every view of them.</summary>
    void RefreshMacro()
    {
        _macroWindow?.Rebuild();
        _flowWindow?.Refresh();
        ShowSlice(_slice);
    }

    void MacroEditor(MacroAction macro)
    {
        if (_macroWindow is { } open && open.Macro == macro)
        {
            // One place to edit at a time: the steps are in their own window.
            slicePanel.Children.Add(Caption($"Steps ({macro.Steps.Count})"));
            var bring = new Button { Content = "Show the steps window", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
            bring.Click += (_, _) => { open.WindowState = WindowState.Normal; open.Activate(); };
            slicePanel.Children.Add(bring);
            return;
        }
        BuildMacro(slicePanel, macro, MacroPlace.Slice);
    }

    void PopOutMacro(MacroAction macro)
    {
        if (_slice is not { } slice) return;
        var window = new MacroWindow(this, slice, macro);
        _macroWindow = window;
        // Closed by the user: the steps come back into the slice panel. (Closed by the editor, see below.)
        window.Closed += (_, _) => { if (_macroWindow != window) return; _macroWindow = null; ShowSlice(_slice); };
        window.Rebuild();
        window.Show();
        ShowSlice(_slice);
    }

    /// <summary>Developer aid (--shot-editor): opens the first macro slice's steps window, saves a picture of it, closes it.</summary>
    internal async Task SaveMacroWindowShot(string path)
    {
        if (_ring?.Slices.FirstOrDefault(s => s.Action is MacroAction) is not { Action: MacroAction macro } slice) return;
        ShowSlice(slice);
        PopOutMacro(macro);
        if (_macroWindow is not { } window) return;
        await Task.Delay(800);
        window.UpdateLayout();
        var root = (FrameworkElement)window.Content;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var backdrop = new DrawingVisual();
        using (var dc = backdrop.RenderOpen())
            dc.DrawRectangle(Theme.Frozen(Theme.IsDark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3)), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        bitmap.Render(backdrop);
        bitmap.Render(root);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var file = File.Create(path)) encoder.Save(file);
        window.Close();
    }

    /// <summary>Fills the steps window. Building counts as loading, so the new fields filling in are not edits.</summary>
    internal void FillMacroWindow(MacroWindow window, Panel host)
    {
        var was = _loading;
        _loading = true;
        try { host.Children.Clear(); BuildMacro(host, window.Macro, MacroPlace.Window); }
        finally { _loading = was; }
    }

    /// <summary>An edit inside the steps window: saved like one in the editor.</summary>
    internal void EditedInMacroWindow(object sender, RoutedEventArgs e) => OnEdited(sender, e);

    /// <summary>Closes the steps window when the slice it shows is no longer the one being edited.</summary>
    void CloseMacroWindowUnlessFor(Slice? slice)
    {
        if (_macroWindow is not { } open || (slice is not null && slice.Action == open.Macro)) return;
        _macroWindow = null;           // first, so its Closed handler doesn't rebuild the panel being built now
        open.Close();
    }

    /// <summary>The step list with its view switch, total and add buttons, in the slice panel or the steps window
    /// (<paramref name="place"/>), which also gets a toolbar for the whole list, or a flow card's side panel.</summary>
    internal void BuildMacro(Panel host, MacroAction macro, MacroPlace place)
    {
        var split = Config.Settings.SplitKeyEvents;
        var popped = place == MacroPlace.Window;
        if (popped) host.Children.Add(MacroToolbar(macro));
        var stepsCaption = Caption(place == MacroPlace.Card ? "Steps in this card, played top to bottom."
            : "Steps, played top to bottom. If a step fails the rest are skipped. Esc stops a running macro.");
        stepsCaption.TextWrapping = TextWrapping.Wrap;
        host.Children.Add(stepsCaption);
        if (place == MacroPlace.Slice)
        {
            var popOut = new Button { Content = "Pop out \u29C9", ToolTip = "Open the steps in their own, bigger window, with Clear all and other tools",
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
            popOut.Click += (_, _) => PopOutMacro(macro);
            host.Children.Add(popOut);
        }
        var splitBox = new CheckBox { Content = "Show key down and key up separately", IsChecked = split, Margin = new Thickness(0, 4, 0, 4),
            ToolTip = "Off: a key or mouse button that goes down and straight back up shows as one press. Only the view changes, never what the macro does." };
        splitBox.Click += (_, _) => { Config.Settings.SplitKeyEvents = splitBox.IsChecked == true; RefreshMacro(); };
        host.Children.Add(splitBox);

        var total = new TextBlock { Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = (Brush)Application.Current.Resources["SubtleBrush"] };
        void UpdateTotal() => total.Text = $"{macro.Steps.Count} steps \u00B7 total \u2248 {MacroView.TotalMs(macro.Steps) / 1000.0:0.0##} s (waits and mouse moves)";
        var rows = MacroView.Rows(macro.Steps, split);
        if (rows.Count == 0)
            host.Children.Add(new TextBlock { Text = "No steps yet. Add some below, or record them.", Margin = new Thickness(0, 8, 0, 0),
                Foreground = (Brush)Application.Current.Resources["SubtleBrush"] });
        for (var i = 0; i < rows.Count; i++)
        {
            var row = StepRow(macro, rows, i, UpdateTotal);
            host.Children.Add(popped ? DraggableRow(row, macro, rows, i) : row);
        }
        UpdateTotal();
        host.Children.Add(total);

        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        void AddButton(string text, Func<MacroStep> make, string? tip = null)
        {
            var button = new Button { Content = text, Margin = new Thickness(0, 0, 6, 6), ToolTip = tip };
            button.Click += (_, _) => { macro.Steps.Add(make()); RefreshMacro(); Touch(); };
            buttons.Children.Add(button);
        }
        AddButton("Key press", () => new KeyStep(), "A key or a combination such as Ctrl+C, pressed and let go");
        if (split)
        {
            AddButton("Key down", () => new KeyDownStep(), "Holds a key down until a key up step (or the end of the macro)");
            AddButton("Key up", () => new KeyUpStep());
        }
        AddButton("Click", () => new ClickStep(), "Clicks where the pointer is");
        if (split)
        {
            AddButton("Mouse down", () => new MouseDownStep());
            AddButton("Mouse up", () => new MouseUpStep());
        }
        void RecordButton(string text, string tip, bool movementOnly)
        {
            var button = new Button { Content = text, Margin = new Thickness(0, 0, 6, 6), ToolTip = tip };
            button.Click += (_, _) =>
            {
                if (Record(movementOnly) is not { } steps) return;
                macro.Steps.AddRange(steps);
                RefreshMacro();
                Touch();
            };
            buttons.Children.Add(button);
        }
        RecordButton("Mouse move", "Record a pointer path (F9 starts and stops)", movementOnly: true);
        AddButton("Scroll", () => new ScrollStep(), "Turns the mouse wheel where the pointer is");
        AddButton("Screenshot", () => new ScreenshotStep(), "Puts a screenshot on the clipboard, for a web request to send");
        AddButton("Ask AI", NewAskAi, "Asks your AI about the clipboard picture or text; the answer goes on the clipboard");
        AddButton("Screenshot area", () => new ScreenshotStep { Area = ScreenshotArea.Choose },
            "When it runs, waits for you to drag out an area of the screen, then puts that picture on the clipboard");
        AddButton("Wait", () => new DelayStep { Ms = 100 }, "A pause, in milliseconds");
        AddButton("Type text", () => new TextStep());
        AddButton("Paste text", () => new PasteStep());
        AddButton("Open", () => new LaunchStep());
        AddButton("Focus window", () => new FocusStep());
        AddButton("Set clipboard", () => new ClipboardStep());
        AddButton("Web request", () => new WebhookStep());
        RecordButton("\u23FA Record", "Record mouse movement, clicks, scrolling and keys in any window, with the time between them (F9 starts and stops)", movementOnly: false);
        host.Children.Add(buttons);
    }

    /// <summary>Actions on the whole list, for the steps window.</summary>
    UIElement MacroToolbar(MacroAction macro)
    {
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        Button Tool(string text, string tip, Action act)
        {
            var button = new Button { Content = text, ToolTip = tip, Margin = new Thickness(0, 0, 6, 6) };
            button.Click += (_, _) => act();
            bar.Children.Add(button);
            return button;
        }
        Tool("\u23FA Record", "Record steps and add them at the end (F9 starts and stops)", () =>
        {
            if (Record(movementOnly: false) is not { } steps) return;
            macro.Steps.AddRange(steps);
            RefreshMacro();
            Touch();
        });
        Tool("Clear all", "Remove every step", () =>
        {
            if (macro.Steps.Count == 0) return;
            if (MessageBox.Show(TopWindow, $"Remove all {macro.Steps.Count} steps?", "RingDeck",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            macro.Steps.Clear();
            RefreshMacro();
            Touch();
        }).IsEnabled = macro.Steps.Count > 0;
        var waits = macro.Steps.Count(s => s is DelayStep);
        Tool("Remove all waits", "Remove every wait, so the steps run one right after another", () =>
        {
            MacroView.RemoveWaits(macro.Steps);
            RefreshMacro();
            Touch();
        }).IsEnabled = waits > 0;
        var setPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 6) };
        var ms = new TextBox { Width = 64, Text = "100", ToolTip = "Milliseconds" };
        var apply = new Button { Content = "Set all waits", ToolTip = "Give every wait this length", Margin = new Thickness(0, 0, 6, 0), IsEnabled = waits > 0 };
        apply.Click += (_, _) =>
        {
            if (!int.TryParse(ms.Text.Trim(), out var value) || value < 0) { ms.Focus(); ms.SelectAll(); return; }
            MacroView.SetWaits(macro.Steps, value);
            RefreshMacro();
            Touch();
        };
        setPanel.Children.Add(apply);
        setPanel.Children.Add(new TextBlock { Text = "to", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        setPanel.Children.Add(ms);
        setPanel.Children.Add(new TextBlock { Text = "ms", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
        bar.Children.Add(setPanel);
        return bar;
    }

    /// <summary>Opens the F9 recorder; returns the recorded steps, or null when cancelled or nothing was recorded.</summary>
    List<MacroStep>? Record(bool movementOnly)
    {
        var window = new RecorderWindow(movementOnly) { Owner = TopWindow };
        return window.ShowDialog() == true && window.Result is { Count: > 0 } steps ? steps : null;
    }

    static string StepLabel(MacroStep step) => step switch
    {
        KeyStep => "Key press", TextStep => "Type", PasteStep => "Paste", DelayStep => "Wait",
        LaunchStep => "Open", FocusStep => "Focus", ClipboardStep => "Clipboard", WebhookStep => "Send",
        KeyDownStep => "Key down", KeyUpStep => "Key up", ClickStep => "Click", MouseDownStep => "Mouse down",
        MouseUpStep => "Mouse up", MouseMoveStep => "Mouse move", ScrollStep => "Scroll", ScreenshotStep => "Screenshot", AskAiStep => "Ask AI", _ => "",
    };

    UIElement StepRow(MacroAction macro, IReadOnlyList<MacroRow> rows, int index, Action updateTotal)
    {
        var steps = macro.Steps;
        var r = rows[index];
        var step = steps[r.Start];
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        void Tool(string text, string tip, Action act, bool enabled = true)
        {
            var button = new Button { Content = text, ToolTip = tip, Width = 30, Padding = new Thickness(0), Margin = new Thickness(4, 0, 0, 0), IsEnabled = enabled };
            button.Click += (_, _) => { act(); RefreshMacro(); Touch(); };
            DockPanel.SetDock(button, Dock.Right);
            row.Children.Add(button);
        }
        Tool("\u2715", "Remove", () => MacroView.Remove(steps, r));
        Tool("\u2193", "Move down", () => MacroView.Move(steps, rows, index, +1), index < rows.Count - 1);
        Tool("\u2191", "Move up", () => MacroView.Move(steps, rows, index, -1), index > 0);
        var label = new TextBlock { Width = 84, VerticalAlignment = VerticalAlignment.Center,
            Text = r.Kind switch { MacroRowKind.KeyPress => "Key press", MacroRowKind.ButtonPress => "Click", _ => StepLabel(step) } };
        DockPanel.SetDock(label, Dock.Left);
        row.Children.Add(label);

        if (step is DelayStep wait && r.Kind == MacroRowKind.Step)
        {
            // Waits are lighter and indented, so the actions stand out between them.
            row.Margin = new Thickness(18, 2, 0, 0);
            label.Width = 66;
            label.Foreground = (Brush)Application.Current.Resources["SubtleBrush"];
            row.Children.Add(MsBox(wait, updateTotal));
            return row;
        }
        row.Children.Add(r.Kind switch
        {
            MacroRowKind.KeyPress => PressEditor(KeyField((KeyDownStep)step, (KeyUpStep)steps[r.Start + r.Count - 1]), r.Hold(steps), updateTotal),
            MacroRowKind.ButtonPress => PressEditor(ButtonField(((MouseDownStep)step).Button,
                b => { ((MouseDownStep)step).Button = b; ((MouseUpStep)steps[r.Start + r.Count - 1]).Button = b; }), r.Hold(steps), updateTotal),
            _ => StepFields(macro, step, updateTotal),
        });
        return row;
    }

    /// <summary>The fields that edit one step (without its label or row buttons).</summary>
    UIElement StepFields(MacroAction macro, MacroStep step, Action updateTotal) => step switch
            {
                KeyStep key => Bound(new ShortcutBox { AllowBare = true }, ShortcutBox.ValueProperty, key, nameof(KeyStep.Combo)),
                KeyDownStep down => Bound(new ShortcutBox { SingleKey = true }, ShortcutBox.ValueProperty, down, nameof(KeyDownStep.Key)),
                KeyUpStep up => Bound(new ShortcutBox { SingleKey = true }, ShortcutBox.ValueProperty, up, nameof(KeyUpStep.Key)),
                ClickStep click => ButtonField(click.Button, b => click.Button = b),
                MouseDownStep md => ButtonField(md.Button, b => md.Button = b),
                MouseUpStep mu => ButtonField(mu.Button, b => mu.Button = b),
                MouseMoveStep move => MouseMoveEditor(macro, move, updateTotal),
                ScrollStep scroll => ScrollEditor(scroll),
                ScreenshotStep shot => ScreenshotEditor(shot),
                AskAiStep ask => AskAiEditor(ask),
                TextStep text => Bound(new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap }, TextBox.TextProperty, text, nameof(TextStep.Text)),
                PasteStep paste => Bound(new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, ToolTip = "Pasted through the clipboard. Placeholders like {date} work." },
                    TextBox.TextProperty, paste, nameof(PasteStep.Text)),
                ClipboardStep clip => Bound(new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, ToolTip = "Put on the clipboard and left there" },
                    TextBox.TextProperty, clip, nameof(ClipboardStep.Text)),
                LaunchStep launch => OpenStepEditor(launch),
                FocusStep focus => FocusStepEditor(focus),
                WebhookStep hook => WebhookStepEditor(hook),
                DelayStep wait => MsBox(wait, updateTotal),
                _ => new TextBlock(),
            };

    // ----- a flow card that does one thing: its own form -----

    static string FieldCaption(MacroStep step) => step switch
    {
        LaunchStep => "App, file, folder or web address",
        TextStep => "Text to type",
        PasteStep => "Text to paste (placeholders such as {date} and {clipboard} work)",
        ClipboardStep => "Text to put on the clipboard",
        KeyStep => "Keys to press (click the box, then press them)",
        KeyDownStep => "Key to hold down",
        KeyUpStep => "Key to let go",
        ClickStep or MouseDownStep or MouseUpStep => "Mouse button",
        MouseMoveStep => "Recorded path and speed",
        ScrollStep => "Direction and how far",
        ScreenshotStep => "What to capture",
        FocusStep => "Window to bring to the front",
        DelayStep => "How long to wait",
        _ => "",
    };

    /// <summary>The form for a card holding a single step: full-width fields with their labels above them.</summary>
    internal void BuildCardForm(Panel host, FlowNode node, MacroAction steps, Action rebuild)
    {
        var step = node.Steps[0];
        switch (step)
        {
            case AskAiStep ask: AskAiForm(host, ask, rebuild); return;
            case WebhookStep hook: FillWebhookForm(host, hook.Request, rebuild); return;
        }
        host.Children.Add(Caption(FieldCaption(step)));
        var fields = StepFields(steps, step, () => { });
        if (fields is TextBox { AcceptsReturn: true } box) box.MinHeight = 90;
        host.Children.Add(fields);
    }

    /// <summary>Ask AI, laid out for the flow window's side panel.</summary>
    void AskAiForm(Panel host, AskAiStep ask, Action rebuild)
    {
        var add = host.Children;
        var subtle = (Brush)Application.Current.Resources["SubtleBrush"];
        TextBlock Hint(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = subtle, FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };

        add.Add(Caption("AI connection"));
        var row = new DockPanel();
        var create = new Button { Content = "New…", Margin = new Thickness(6, 0, 0, 0), ToolTip = "Set up a new AI connection" };
        create.Click += (_, _) =>
        {
            var c = new AiConnection { Name = "AI " + (Config.AiConnections.Count + 1) };
            if (!EditConnection(c)) return;
            Config.AiConnections.Add(c);
            ask.ConnectionId = c.Id;
            Touch();
            rebuild();
        };
        DockPanel.SetDock(create, Dock.Right);
        row.Children.Add(create);
        var connection = new ComboBox { ItemsSource = Config.AiConnections.ToList(), DisplayMemberPath = nameof(AiConnection.Name),
            SelectedItem = Config.FindConnection(ask.ConnectionId) };
        connection.SelectionChanged += (_, _) => { if (connection.SelectedItem is AiConnection c && c.Id != ask.ConnectionId) { ask.ConnectionId = c.Id; rebuild(); } };
        row.Children.Add(connection);
        add.Add(row);
        if (Config.FindConnection(ask.ConnectionId) is { } chosen)
            add.Add(Hint($"Model: {(chosen.Model.Trim().Length > 0 ? chosen.Model.Trim() : "not set")}. It has to read images to be asked about a picture."));
        else
            add.Add(new TextBlock
            {
                Text = Config.AiConnections.Count == 0 ? "No AI connection yet: press New… to set one up." : "Pick the connection to ask.",
                Foreground = Brushes.Orange, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
            });

        add.Add(Caption("It reads, from the clipboard"));
        (AiReads Value, string Text)[] choices =
        {
            (AiReads.Picture, "The picture (a screenshot or a copied image)"), (AiReads.Text, "The text"),
            (AiReads.Both, "The picture and the text"), (AiReads.Nothing, "Nothing, just the question below"),
        };
        var group = "reads-" + Guid.NewGuid().ToString("N");
        foreach (var (value, text) in choices)
        {
            var option = new RadioButton { Content = text, GroupName = group, IsChecked = ask.Reads == value, Margin = new Thickness(0, 2, 0, 2) };
            option.Checked += (_, _) => ask.Reads = value;
            add.Add(option);
        }

        add.Add(Caption("Ask"));
        add.Add(Bound(new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 100, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            TextBox.TextProperty, ask, nameof(AskAiStep.Instruction)));
        add.Add(Hint("For example: Read the error in this screenshot and suggest a fix. Left empty, it's asked what the picture shows."));

        add.Add(Caption("How it should answer (optional)"));
        add.Add(Bound(new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 56 }, TextBox.TextProperty, ask, nameof(AskAiStep.SystemPrompt)));
        add.Add(Hint("For example: Answer in two short sentences, in English."));

        add.Add(new Border
        {
            Margin = new Thickness(0, 16, 0, 0), Padding = new Thickness(12, 10, 12, 10), CornerRadius = new CornerRadius(8),
            Background = Theme.Frozen(Theme.Accent, 0.12),
            Child = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = "The answer replaces what's on the clipboard, picture included. The next cards can paste it, or send it with {clipboard} in a Send to web message.",
            },
        });
    }

    const string RowFormat = "RingDeck.MacroRow";

    /// <summary>A row of the steps window with a grip to drag it by. While another row is dragged over it, a line
    /// above or below shows where that row will land.</summary>
    UIElement DraggableRow(UIElement content, MacroAction macro, IReadOnlyList<MacroRow> rows, int index)
    {
        var line = Theme.Frozen(Theme.Accent);
        var frame = new Border { BorderThickness = new Thickness(0, 2, 0, 2), BorderBrush = Brushes.Transparent, AllowDrop = true };
        var grip = new TextBlock
        {
            Text = "\u283F", FontSize = 16, Width = 22, VerticalAlignment = VerticalAlignment.Center, Cursor = System.Windows.Input.Cursors.SizeNS,
            Foreground = (Brush)Application.Current.Resources["SubtleBrush"], Background = Brushes.Transparent, ToolTip = "Drag to move this step",
        };
        Point? pressed = null;
        grip.MouseLeftButtonDown += (_, e) => { pressed = e.GetPosition(grip); e.Handled = true; };
        grip.MouseLeftButtonUp += (_, _) => pressed = null;
        grip.MouseMove += (_, e) =>
        {
            if (pressed is not { } start || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
            var now = e.GetPosition(grip);
            if (Math.Abs(now.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance
                && Math.Abs(now.X - start.X) < SystemParameters.MinimumHorizontalDragDistance) return;
            pressed = null;
            frame.Opacity = 0.5;
            DragDrop.DoDragDrop(grip, new DataObject(RowFormat, index), DragDropEffects.Move);
            frame.Opacity = 1;
        };

        bool Above(DragEventArgs e) => e.GetPosition(frame).Y < frame.ActualHeight / 2;
        void Show(Thickness where) => (frame.BorderBrush, frame.BorderThickness) = (where == default ? Brushes.Transparent : line, where == default ? new Thickness(0, 2, 0, 2) : where);
        // Preview (tunnelling) events, so text boxes inside the row, which take drops of their own, never see these.
        frame.PreviewDragOver += (_, e) =>
        {
            if (!e.Data.GetDataPresent(RowFormat)) return;           // not a step: let the field handle it
            e.Handled = true;
            e.Effects = DragDropEffects.Move;
            Show(Above(e) ? new Thickness(0, 2, 0, 0) : new Thickness(0, 0, 0, 2));
        };
        frame.DragLeave += (_, _) => Show(default);
        frame.PreviewDrop += (_, e) =>
        {
            Show(default);
            if (e.Data.GetData(RowFormat) is not int from) return;
            e.Handled = true;
            MacroView.MoveTo(macro.Steps, rows, from, Above(e) ? index : index + 1);
            // Rebuild once the drag has fully finished; the dragged row is still on screen until then.
            Dispatcher.BeginInvoke(() => { RefreshMacro(); Touch(); });
        };

        var panel = new DockPanel();
        DockPanel.SetDock(grip, Dock.Left);
        panel.Children.Add(grip);
        panel.Children.Add(content);
        frame.Child = panel;
        return frame;
    }

    /// <summary>Milliseconds box with "ms" after it; keeps the total up to date while typing.</summary>
    static UIElement MsBox(DelayStep wait, Action updateTotal)
    {
        var panel = new DockPanel();
        var unit = new TextBlock { Text = "ms", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0),
            Foreground = (Brush)Application.Current.Resources["SubtleBrush"] };
        DockPanel.SetDock(unit, Dock.Right);
        var box = Bound(new TextBox { Width = 80, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Milliseconds (1000 = 1 second)" },
            TextBox.TextProperty, wait, nameof(DelayStep.Ms));
        box.TextChanged += (_, _) => updateTotal();
        panel.Children.Add(unit);
        panel.Children.Add(box);
        return panel;
    }

    /// <summary>A joined press: its key or button, and how long it is held when the recording kept that.</summary>
    static UIElement PressEditor(UIElement field, DelayStep? hold, Action updateTotal)
    {
        if (hold is null) return field;
        // Two lines, so the key or button keeps its full width even in the narrow slice panel.
        var held = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        held.Children.Add(new TextBlock { Text = "held", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
            Foreground = (Brush)Application.Current.Resources["SubtleBrush"] });
        var box = Bound(new TextBox { Width = 64, ToolTip = "How long it is held down, in milliseconds" }, TextBox.TextProperty, hold, nameof(DelayStep.Ms));
        box.TextChanged += (_, _) => updateTotal();
        held.Children.Add(box);
        held.Children.Add(new TextBlock { Text = "ms", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0),
            Foreground = (Brush)Application.Current.Resources["SubtleBrush"] });
        var panel = new StackPanel();
        panel.Children.Add(field);
        panel.Children.Add(held);
        return panel;
    }

    /// <summary>The key of a joined press: changing it changes both the down and the up step.</summary>
    static UIElement KeyField(KeyDownStep down, KeyUpStep up)
    {
        var box = Bound(new ShortcutBox { SingleKey = true }, ShortcutBox.ValueProperty, down, nameof(KeyDownStep.Key));
        box.TextChanged += (_, _) => up.Key = box.Value;
        return box;
    }

    static readonly string[] ButtonNames = { "Left", "Right", "Middle" };
    static readonly string[] ScrollNames = { "Up", "Down", "Left", "Right" };
    static readonly string[] ScreenshotNames = { "The screen with the pointer", "All screens", "The window in front", "An area I drag out when it runs" };

    static readonly string[] AiReadsNames = { "The picture on the clipboard", "The text on the clipboard", "Picture and text", "Nothing (just the instruction)" };

    /// <summary>A new Ask AI step uses the first connection, if there is one.</summary>
    internal AskAiStep NewAskAi() => new() { ConnectionId = Config.AiConnections.FirstOrDefault()?.Id ?? Guid.Empty };

    /// <summary>Which connection, what it reads, and what to ask.</summary>
    UIElement AskAiEditor(AskAiStep ask)
    {
        var panel = new StackPanel();
        var connection = new ComboBox { ItemsSource = Config.AiConnections.ToList(), DisplayMemberPath = nameof(AiConnection.Name),
            SelectedItem = Config.FindConnection(ask.ConnectionId), ToolTip = "The AI connection to ask (set them up in Settings → AI connections)" };
        connection.SelectionChanged += (_, _) => { if (connection.SelectedItem is AiConnection c) ask.ConnectionId = c.Id; };
        panel.Children.Add(connection);
        if (connection.SelectedItem is null)
            panel.Children.Add(new TextBlock
            {
                Text = Config.AiConnections.Count == 0 ? "Create an AI connection first: Settings → AI connections." : "Pick the connection to ask.",
                Foreground = Brushes.Orange, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
            });
        var reads = new ComboBox { ItemsSource = AiReadsNames, SelectedIndex = (int)ask.Reads, ToolTip = "What it sends from the clipboard" };
        reads.SelectionChanged += (_, _) => { if (reads.SelectedIndex >= 0) ask.Reads = (AiReads)reads.SelectedIndex; };
        panel.Children.Add(Labelled("Reads", reads));
        panel.Children.Add(Labelled("Ask", Bound(new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 44,
            ToolTip = "For example: Read the error in this screenshot and suggest a fix. Empty: it's asked what the picture shows." },
            TextBox.TextProperty, ask, nameof(AskAiStep.Instruction))));
        panel.Children.Add(new TextBlock
        {
            Text = "The answer replaces what's on the clipboard (picture included), for the next steps to paste or send.", TextWrapping = TextWrapping.Wrap, FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["SubtleBrush"], Margin = new Thickness(72, 2, 0, 0),
        });
        return panel;
    }

    static UIElement ScreenshotEditor(ScreenshotStep shot)
    {
        var area = new ComboBox { ItemsSource = ScreenshotNames, SelectedIndex = (int)shot.Area, ToolTip = "What to capture; the picture goes on the clipboard" };
        area.SelectionChanged += (_, _) => { if (area.SelectedIndex >= 0) shot.Area = (ScreenshotArea)area.SelectedIndex; };
        return area;
    }

    /// <summary>Which way, and how many notches of the wheel.</summary>
    static UIElement ScrollEditor(ScrollStep scroll)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var direction = new ComboBox { ItemsSource = ScrollNames, SelectedIndex = (int)scroll.Direction, Width = 100 };
        direction.SelectionChanged += (_, _) => { if (direction.SelectedIndex >= 0) scroll.Direction = (ScrollDirection)direction.SelectedIndex; };
        var count = Bound(new TextBox { Width = 56, Margin = new Thickness(6, 0, 0, 0), ToolTip = "How many clicks of the wheel" },
            TextBox.TextProperty, scroll, nameof(ScrollStep.Notches));
        var unit = new TextBlock { Text = "notches", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0),
            Foreground = (Brush)Application.Current.Resources["SubtleBrush"] };
        panel.Children.Add(direction);
        panel.Children.Add(count);
        panel.Children.Add(unit);
        return panel;
    }

    static UIElement ButtonField(ClickButton value, Action<ClickButton> set)
    {
        var combo = new ComboBox { ItemsSource = ButtonNames, SelectedIndex = (int)value, ToolTip = "Mouse button" };
        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) set((ClickButton)combo.SelectedIndex); };
        return combo;
    }

    /// <summary>Two lines: what was recorded; then the playback speed and a button to record it again.</summary>
    UIElement MouseMoveEditor(MacroAction macro, MouseMoveStep move, Action updateTotal)
    {
        var record = new Button { Content = "\u23FA", Width = 30, Padding = new Thickness(0), Margin = new Thickness(6, 0, 0, 0), ToolTip = "Record it again (F9 starts and stops)" };
        record.Click += (_, _) =>
        {
            // What is recorded takes this step's place (a path, or more if clicks and keys were recorded too).
            if (Record(movementOnly: true) is not { } steps) return;
            var at = macro.Steps.IndexOf(move);
            if (at < 0) return;
            macro.Steps.RemoveAt(at);
            macro.Steps.InsertRange(at, steps);
            RefreshMacro();
            Touch();
        };
        var speedText = new TextBlock { Width = 36, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
        var slider = new Slider { Minimum = MousePath.MinSpeed, Maximum = MousePath.MaxSpeed, TickFrequency = 0.25, IsSnapToTickEnabled = true,
            Value = MousePath.ClampSpeed(move.Speed), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Speed: play the path up to 3 times faster than recorded" };
        void ShowSpeed() => speedText.Text = $"\u00D7{slider.Value:0.##}";
        slider.ValueChanged += (_, _) => { move.Speed = slider.Value; ShowSpeed(); updateTotal(); };
        ShowSpeed();
        var top = new DockPanel();
        DockPanel.SetDock(record, Dock.Right);
        top.Children.Add(record);
        top.Children.Add(new TextBlock { Text = MousePath.Describe(move.Path), VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = MousePath.Describe(move.Path) });
        var speed = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        DockPanel.SetDock(speedText, Dock.Right);
        speed.Children.Add(speedText);
        speed.Children.Add(slider);

        var panel = new StackPanel();
        panel.Children.Add(top);
        panel.Children.Add(speed);
        return panel;
    }

    // ----- AI chat -----

    /// <summary>A new AI slice uses the first connection, if there is one.</summary>
    AiChatAction NewAiAction() => new() { ConnectionId = Config.AiConnections.FirstOrDefault()?.Id ?? Guid.Empty };

    /// <summary>Opens the connection dialog. Returns true when the user saved.</summary>
    internal bool EditConnection(AiConnection connection) => new ConnectionWindow(connection) { Owner = this }.ShowDialog() == true;

    void AiEditor(AiChatAction ai)
    {
        var add = slicePanel.Children;
        add.Add(Caption("AI connection"));
        var row = new DockPanel();
        var newButton = new Button { Content = "New", Margin = new Thickness(6, 0, 0, 0) };
        var editButton = new Button { Content = "Edit", Margin = new Thickness(6, 0, 0, 0) };
        DockPanel.SetDock(newButton, Dock.Right);
        DockPanel.SetDock(editButton, Dock.Right);
        var picker = new ComboBox { ItemsSource = Config.AiConnections.ToList(), DisplayMemberPath = nameof(AiConnection.Name), SelectedItem = Config.FindConnection(ai.ConnectionId) };
        picker.SelectionChanged += (_, _) => { if (!_loading && picker.SelectedItem is AiConnection c) ai.ConnectionId = c.Id; };
        editButton.IsEnabled = picker.SelectedItem is not null;
        editButton.Click += (_, _) => { if (picker.SelectedItem is AiConnection c && EditConnection(c)) { ShowSlice(_slice); Touch(); } };
        newButton.Click += (_, _) =>
        {
            var c = new AiConnection { Name = "AI " + (Config.AiConnections.Count + 1) };
            if (!EditConnection(c)) return;
            Config.AiConnections.Add(c);
            ai.ConnectionId = c.Id;
            ShowSlice(_slice);
            Touch();
        };
        row.Children.Add(editButton);
        row.Children.Add(newButton);
        row.Children.Add(picker);
        add.Add(row);
        if (picker.SelectedItem is null)
            add.Add(new TextBlock
            {
                Text = Config.AiConnections.Count == 0 ? "Create a connection: the service's address, model and key." : "Pick the connection this slice talks to.",
                Foreground = Brushes.Orange, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap,
            });

        add.Add(Caption("Instruction (optional, sent before your text)"));
        add.Add(Bound(new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60 }, TextBox.TextProperty, ai, nameof(AiChatAction.Instruction)));
        add.Add(Caption("System prompt (optional, how the AI should behave)"));
        // The grabbed text reaches the model inside <text> tags; saying so in the prompt keeps it from answering it.
        add.Add(Bound(new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 44 }, TextBox.TextProperty, ai, nameof(AiChatAction.SystemPrompt)));
        add.Add(Caption("Start from"));
        var input = new ComboBox { ItemsSource = new[] { "The selected text", "The clipboard", "Nothing" }, SelectedIndex = (int)ai.Input };
        input.SelectionChanged += (_, _) => { if (!_loading) ai.Input = (AiInput)input.SelectedIndex; };
        add.Add(input);
        add.Add(Bound(new CheckBox { Content = new TextBlock { Text = "Also send the picture on the clipboard (a screenshot or a copied image), for the AI to read", TextWrapping = TextWrapping.Wrap },
            Margin = new Thickness(0, 12, 0, 0) },
            ToggleButton.IsCheckedProperty, ai, nameof(AiChatAction.SendImage)));
        add.Add(new TextBlock
        {
            Margin = new Thickness(28, 2, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["SubtleBrush"],
            Text = "Take a screenshot (Win+Shift+S), then use the ring. The model must be one that reads images, such as GPT-4o, Claude or Gemini. "
                 + "With no instruction and no text, it's asked what the picture shows. Custom connections get it through {{image}} and {{image_type}}.",
        });
        var showChat = Bound(new CheckBox { Content = "Show the chat window", Margin = new Thickness(0, 12, 0, 0) },
            ToggleButton.IsCheckedProperty, ai, nameof(AiChatAction.ShowChat));
        var sendNow = Bound(new CheckBox { Content = "Send straight away (otherwise you can edit first)" },
            ToggleButton.IsCheckedProperty, ai, nameof(AiChatAction.SendImmediately));
        var quietNote = new TextBlock
        {
            Margin = new Thickness(28, 2, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["SubtleBrush"],
            Text = "Off: the request runs in the background and the answer is copied to the clipboard. A notification tells you when it's ready.",
        };
        void SyncChat()
        {
            sendNow.IsEnabled = showChat.IsChecked == true;        // without a window there is nothing to edit first
            quietNote.Visibility = showChat.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
        }
        showChat.Checked += (_, _) => SyncChat();
        showChat.Unchecked += (_, _) => SyncChat();
        SyncChat();
        add.Add(showChat);
        add.Add(quietNote);
        add.Add(sendNow);
        add.Add(new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["SubtleBrush"],
            Text = "Your selected text is sent between <text> and </text>. For rewrites, end the prompt with \"Reply with only the rewritten text.\" In the chat: Enter sends, Ctrl+Enter pastes the reply back, Esc closes.",
        });
    }

    /// <summary>Two lines: the target with a browse button, then optional arguments.</summary>
    UIElement OpenStepEditor(LaunchStep launch)
    {
        var top = new DockPanel();
        var browse = new Button { Content = "…", ToolTip = "Choose a file or program", Width = 30, Padding = new Thickness(0), Margin = new Thickness(4, 0, 0, 0) };
        var target = Bound(new TextBox { ToolTip = "App, file, folder or web address" }, TextBox.TextProperty, launch, nameof(LaunchStep.Target));
        browse.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = "All files|*.*" };
            if (dialog.ShowDialog(this) == true) target.Text = dialog.FileName;
        };
        DockPanel.SetDock(browse, Dock.Right);
        top.Children.Add(browse);
        top.Children.Add(target);
        var panel = new StackPanel();
        panel.Children.Add(top);
        panel.Children.Add(Labelled("Arguments", Bound(new TextBox { ToolTip = "Arguments (optional)" }, TextBox.TextProperty, launch, nameof(LaunchStep.Arguments))));
        return panel;
    }

    /// <summary>Two lines: program with a picker of the windows open now (fills both fields), then part of the title.</summary>
    UIElement FocusStepEditor(FocusStep focus)
    {
        var program = Bound(new TextBox { ToolTip = "Program, e.g. outlook.exe (optional)" }, TextBox.TextProperty, focus, nameof(FocusStep.Program));
        var title = Bound(new TextBox { ToolTip = "Part of the window title (optional)" }, TextBox.TextProperty, focus, nameof(FocusStep.Title));
        var pick = new ComboBox { Width = 40, MinWidth = 0, ToolTip = "Pick one of the windows open now", Margin = new Thickness(4, 0, 0, 0) };
        List<WindowFocuser.OpenWindow> windows = new();
        pick.DropDownOpened += (_, _) =>
        {
            windows = WindowFocuser.OpenWindows();
            _loading = true;
            pick.ItemsSource = windows.Select(w => $"{w.Program} — {w.Title}").ToList();
            pick.SelectedIndex = -1;
            _loading = false;
        };
        pick.SelectionChanged += (_, e) =>
        {
            e.Handled = true;
            if (_loading || pick.SelectedIndex < 0 || pick.SelectedIndex >= windows.Count) return;
            var w = windows[pick.SelectedIndex];
            program.Text = w.Program;
            title.Text = w.Title.Length > 60 ? w.Title[..60] : w.Title;
        };
        var top = new DockPanel();
        DockPanel.SetDock(pick, Dock.Right);
        top.Children.Add(pick);
        top.Children.Add(program);
        var panel = new StackPanel();
        panel.Children.Add(top);
        panel.Children.Add(Labelled("Title has", title));
        return panel;
    }

    static UIElement Labelled(string text, UIElement field)
    {
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var label = new TextBlock { Text = text, Width = 72, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Application.Current.Resources["SubtleBrush"] };
        DockPanel.SetDock(label, Dock.Left);
        row.Children.Add(label);
        row.Children.Add(field);
        return row;
    }

    /// <summary>The brand logo when the app's name is a known brand ("Spotify", "Google Chrome"), otherwise the app's own icon.</summary>
    string IconForApp(AppPickerWindow.InstalledApp app)
    {
        var name = app.Name.Trim();
        var brand = IconAssets.Brands.FirstOrDefault(b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                                                         || b.Keywords.Contains(name.ToLowerInvariant()));
        if (brand is not null) return brand.Id;
        try { return _app.Icons.ImportPng(ToPng(IconStore.PictureBytes(app.Path)), fillsSlice: false); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or NotSupportedException or System.Runtime.InteropServices.COMException)
        {
            return "";
        }
    }

    /// <summary>Any picture the crop editor accepts, re-encoded as PNG (the icon store keeps PNGs).</summary>
    static byte[] ToPng(byte[] bytes)
    {
        using var input = new System.IO.MemoryStream(bytes);
        var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(input, System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth * f.PixelHeight).First();
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(frame));
        using var output = new System.IO.MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }
}
