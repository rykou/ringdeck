using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using RingDeck.Core;

namespace RingDeck.App;

/// <summary>A slice's macro steps in their own resizable window, with room for every row and a toolbar for the
/// whole list. The editor builds its content; while it is open the slice panel only points to it.</summary>
public sealed class MacroWindow : Window
{
    readonly EditorWindow _editor;
    readonly StackPanel _host = new() { Margin = new Thickness(20, 16, 20, 20) };

    public Slice Slice { get; }
    public MacroAction Macro { get; }

    public MacroWindow(EditorWindow editor, Slice slice, MacroAction macro)
    {
        (_editor, Slice, Macro) = (editor, slice, macro);
        Owner = editor;
        Title = $"Macro steps — {slice.Label}";
        Width = 760;
        Height = 720;
        MinWidth = 520;
        MinHeight = 360;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _host };
        // Edits made here are saved the same way as edits in the editor's own panel.
        scroll.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((s, e) => _editor.EditedInMacroWindow(s, e)));
        scroll.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(_editor.EditedInMacroWindow));
        scroll.AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(_editor.EditedInMacroWindow));
        scroll.AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler((s, e) => _editor.EditedInMacroWindow(s, e)));
        scroll.AddHandler(RangeBase.ValueChangedEvent, new RoutedPropertyChangedEventHandler<double>((s, e) => _editor.EditedInMacroWindow(s, e)));
        // Dragging a step near the top or bottom edge scrolls the list.
        scroll.PreviewDragOver += (_, e) =>
        {
            const double Edge = 40, Step = 14;
            var y = e.GetPosition(scroll).Y;
            if (y < Edge) scroll.ScrollToVerticalOffset(scroll.VerticalOffset - Step);
            else if (y > scroll.ActualHeight - Edge) scroll.ScrollToVerticalOffset(scroll.VerticalOffset + Step);
        };
        Content = scroll;
    }

    public void Rebuild()
    {
        Title = $"Macro steps — {Slice.Label}";
        _editor.FillMacroWindow(this, _host);
    }
}
