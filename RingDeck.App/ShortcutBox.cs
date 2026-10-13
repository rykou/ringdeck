using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RingDeck.Core;
using RingDeck.Windows;

namespace RingDeck.App;

public sealed class ShortcutBox : TextBox
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(string),
        typeof(ShortcutBox), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, e) => ((ShortcutBox)d).Text = (string)e.NewValue ?? ""));

    public ShortcutBox()
    {
        SetResourceReference(StyleProperty, typeof(TextBox));   // a subclass does not pick up the themed TextBox style by itself
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        ToolTip = "Click, then press the keys. Backspace clears, Esc cancels.";
        GotKeyboardFocus += (_, _) => App.Instance.Controller.Capture = OnKey;
        LostKeyboardFocus += (_, _) => App.Instance.Controller.Capture = null;
    }

    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    /// <summary>Allow a key with no modifier (used for macro steps such as Enter).</summary>
    public bool AllowBare { get; set; }

    bool OnKey(int vk, bool down)
    {
        if (vk is 0x01 or 0x02 || KeyNames.IsModifier(vk)) return false;    // let clicks move focus away
        if (!down) return true;
        var mods = InputSender.CurrentMods();
        if (vk == 0x1B && mods == Mods.None) Keyboard.ClearFocus();
        else if (vk is 0x08 or 0x2E && mods == Mods.None) Value = "";
        else if (AllowBare || mods != Mods.None || vk is (>= 0x70 and <= 0x87) or 0x04 or 0x05 or 0x06)
            Value = new Shortcut(mods, vk).ToString();
        return true;
    }
}
