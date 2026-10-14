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
    static readonly string[] ActionNames = { "Nothing", "Paste text", "Macro / workflow", "Open app, file or link", "Open another ring", "AI chat" };
    MacroRecorder? _recorder;

    static int ActionIndex(SliceAction? action) => action switch
    {
        PasteTextAction => 1, MacroAction => 2, LaunchAction => 3, NestedRingAction => 4, AiChatAction => 5, _ => 0,
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
                1 => new PasteTextAction(), 2 => new MacroAction(), 3 => new LaunchAction(), 4 => new NestedRingAction(), 5 => NewAiAction(), _ => null,
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
            case AiChatAction ai: AiEditor(ai); break;
        }
    }

    // ----- icon -----

    UIElement IconRow(Slice slice)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var preview = new IconPreview { Icon = slice.Icon, Fill = slice.Color, Label = slice.Label, Width = 40, Height = 40, Margin = new Thickness(0, 0, 10, 0) };
        var choose = new Button { Content = "Emoji & icons…", Margin = new Thickness(0, 0, 8, 0), ToolTip = "Pick from about 1,600 emoji and 2,500 icons" };
        choose.Click += (_, _) =>
        {
            var picker = new IconPickerWindow { Owner = this };
            if (picker.ShowDialog() != true || picker.Result is null) return;
            slice.Icon = picker.Result;
            ShowSlice(slice);
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
                slice.Icon = _app.Icons.ImportPng(result.Round ? result.Png : ToPng(result.Png), result.Round);
                ShowSlice(slice);
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
            Text = slice.Icon.StartsWith("glyph:", StringComparison.Ordinal) ? slice.Icon[6..] : "",
        };
        custom.TextChanged += (_, _) =>
        {
            if (_loading) return;
            slice.Icon = custom.Text.Length > 0 ? "glyph:" + custom.Text : "";
            preview.Icon = slice.Icon;
        };
        preview.VerticalAlignment = choose.VerticalAlignment = image.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(preview);
        row.Children.Add(choose);
        row.Children.Add(image);
        row.Children.Add(custom);
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

    void MacroEditor(MacroAction macro)
    {
        var stepsCaption = Caption("Steps, played top to bottom. If a step fails the rest are skipped. Esc stops a running macro.");
        stepsCaption.TextWrapping = TextWrapping.Wrap;
        slicePanel.Children.Add(stepsCaption);
        for (var i = 0; i < macro.Steps.Count; i++) slicePanel.Children.Add(StepRow(macro, i));

        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        void AddButton(string text, Func<MacroStep> make)
        {
            var button = new Button { Content = text, Margin = new Thickness(0, 0, 6, 6) };
            button.Click += (_, _) => { macro.Steps.Add(make()); ShowSlice(_slice); Touch(); };
            buttons.Children.Add(button);
        }
        AddButton("Keys", () => new KeyStep());
        AddButton("Type text", () => new TextStep());
        AddButton("Paste text", () => new PasteStep());
        AddButton("Wait", () => new DelayStep { Ms = 300 });
        AddButton("Open", () => new LaunchStep());
        AddButton("Focus window", () => new FocusStep());
        AddButton("Set clipboard", () => new ClipboardStep());
        var record = new Button { Content = "Record", Margin = new Thickness(0, 0, 6, 6), ToolTip = "Records the keys you press in any window, without their timing" };
        record.Click += (_, _) =>
        {
            if (_recorder is null)
            {
                _recorder = new MacroRecorder();
                record.Content = "Stop recording";
                _app.Controller.Capture = (vk, down) =>
                {
                    if (vk < 0x08) return false;            // mouse buttons always pass, so Stop can be clicked
                    if (down) _recorder?.OnKeyDown(vk, InputSender.CurrentMods());
                    // Keys reach the app being recorded. When that "app" is this editor they are swallowed, or
                    // Space and Enter would press the focused Stop button and Tab would walk the form.
                    return IsActive && !RingDeck.Core.KeyNames.IsModifier(vk);
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
        var label = new TextBlock { Width = 68, VerticalAlignment = VerticalAlignment.Center,
            Text = step switch
            {
                KeyStep => "Keys", TextStep => "Type", PasteStep => "Paste", DelayStep => "Wait (ms)",
                LaunchStep => "Open", FocusStep => "Focus", ClipboardStep => "Clipboard", _ => "",
            } };
        DockPanel.SetDock(label, Dock.Left);
        row.Children.Add(label);
        row.Children.Add(step switch
        {
            KeyStep key => Bound(new ShortcutBox { AllowBare = true }, ShortcutBox.ValueProperty, key, nameof(KeyStep.Combo)),
            TextStep text => Bound(new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap }, TextBox.TextProperty, text, nameof(TextStep.Text)),
            DelayStep wait => Bound(new TextBox { ToolTip = "Milliseconds" }, TextBox.TextProperty, wait, nameof(DelayStep.Ms)),
            PasteStep paste => Bound(new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, ToolTip = "Pasted through the clipboard. Placeholders like {date} work." },
                TextBox.TextProperty, paste, nameof(PasteStep.Text)),
            ClipboardStep clip => Bound(new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, ToolTip = "Put on the clipboard and left there" },
                TextBox.TextProperty, clip, nameof(ClipboardStep.Text)),
            LaunchStep launch => OpenStepEditor(launch),
            FocusStep focus => FocusStepEditor(focus),
            _ => new TextBlock(),
        });
        return row;
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
