using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RingDeck.Core;
using RingDeck.Windows;

namespace RingDeck.App;

/// <summary>The chat window an AI slice opens. Conversations live only as long as the window.</summary>
public partial class AiPanel : Window
{
    static readonly AiClient Client = new();
    static AiPanel? _open;

    readonly AiConnection _connection;
    readonly string? _system;
    readonly IntPtr _target;
    readonly Action<IntPtr, string> _insert;
    readonly List<ChatMessage> _messages = new();
    CancellationTokenSource? _cts;
    string _lastReply = "";

    AiPanel(AiConnection connection, string? system, IntPtr target, Action<IntPtr, string> insert)
    {
        InitializeComponent();
        (_connection, _system, _target, _insert) = (connection, system, target, insert);
        Title = $"RingDeck · {connection.Name}";
        nameText.Text = connection.Name;
        modelText.Text = connection.Model;
        Closed += (_, _) => { _cts?.Cancel(); if (_open == this) _open = null; };
    }

    /// <summary>Opens the panel (replacing one that is already open) next to the pointer.</summary>
    /// <param name="target">The window the slice was used in; Insert pastes there.</param>
    public static AiPanel Open(AiConnection connection, AiChatAction action, string? input, IntPtr target, Action<IntPtr, string> insert)
    {
        _open?.Close();
        var panel = new AiPanel(connection, action.SystemPrompt, target, insert);
        panel.PlaceNearPointer();
        panel.Show();
        panel.Activate();
        _open = panel;
        var start = AiChat.Compose(action.Instruction, input);
        if (action.SendImmediately && start.Length > 0) panel.Send(start);
        else { panel.inputBox.Text = start; panel.inputBox.CaretIndex = start.Length; }
        panel.inputBox.Focus();
        return panel;
    }

    void PlaceNearPointer()
    {
        var (px, py) = Screens.Cursor();
        var screen = Screens.At(px, py);
        double s = screen.DpiScale, x = px / s - 60, y = py / s + 24;
        Left = Math.Clamp(x, screen.Left / s + 8, Math.Max(screen.Left / s + 8, screen.Right / s - Width - 8));
        Top = Math.Clamp(y, screen.Top / s + 8, Math.Max(screen.Top / s + 8, screen.Bottom / s - Height - 8));
    }

    bool Busy => _cts is not null;

    void SetBusy(bool busy)
    {
        sendButton.Content = busy ? "Stop" : "Send";
        retryButton.IsEnabled = !busy && _messages.Any(m => m.Role == "user");
        insertButton.IsEnabled = copyButton.IsEnabled = !busy && _lastReply.Length > 0;
        hintText.Text = busy ? "Esc stops the reply" : "Enter sends · Shift+Enter new line · Esc closes";
    }

    TextBox AddBubble(string role, string text, bool error = false)
    {
        var mine = role == "user";
        var box = new TextBox
        {
            Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0),
            Background = Brushes.Transparent, Padding = new Thickness(0), MinHeight = 0,
            Foreground = error ? new SolidColorBrush(Color.FromRgb(0xF2, 0x99, 0x4A)) : new SolidColorBrush(Theme.Foreground),
        };
        transcript.Children.Add(new Border
        {
            Child = box, CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(mine ? 60 : 0, 4, mine ? 0 : 60, 4),
            HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Background = Theme.Frozen(mine ? Theme.Accent : Theme.Foreground, mine ? 0.22 : 0.07),
        });
        scroller.ScrollToEnd();
        return box;
    }

    void Send(string text)
    {
        if (Busy || string.IsNullOrWhiteSpace(text)) return;
        AddBubble("user", text.Trim());
        _messages.Add(new ChatMessage("user", text.Trim()));
        inputBox.Clear();
        _ = Ask();
    }

    /// <summary>Asks for a reply to the conversation as it stands, streaming it into a new bubble.</summary>
    async Task Ask()
    {
        var bubble = AddBubble("assistant", "…");
        var cts = _cts = new CancellationTokenSource();
        SetBusy(true);
        var reply = "";
        try
        {
            await foreach (var piece in Client.SendAsync(_connection, _system, _messages.ToList(), App.Instance.Secrets.Reveal, cts.Token))
            {
                reply += piece;
                bubble.Text = reply;
                scroller.ScrollToEnd();
            }
        }
        catch (AiException ex) { ShowError(bubble, ex.Message); }
        catch (OperationCanceledException) { if (reply.Length == 0) bubble.Text = "(stopped)"; }
        catch (Exception ex) { ShowError(bubble, ex.Message); App.Log(ex); }
        finally
        {
            if (reply.Length > 0) { _messages.Add(new ChatMessage("assistant", reply)); _lastReply = reply; }
            else if (bubble.Text == "…") bubble.Text = "(no answer)";
            _cts = null;
            cts.Dispose();
            if (IsLoaded) { SetBusy(false); inputBox.Focus(); }
        }
    }

    void ShowError(TextBox bubble, string message)
    {
        bubble.Text = message;
        bubble.Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0x99, 0x4A));
    }

    void OnSend(object sender, RoutedEventArgs e)
    {
        if (Busy) _cts?.Cancel(); else Send(inputBox.Text);
    }

    void OnRetry(object sender, RoutedEventArgs e)
    {
        if (Busy || !_messages.Any(m => m.Role == "user")) return;
        while (_messages.Count > 0 && _messages[^1].Role == "assistant") _messages.RemoveAt(_messages.Count - 1);
        if (transcript.Children.Count > 0 && transcript.Children[^1] is Border { HorizontalAlignment: HorizontalAlignment.Left } last)
            transcript.Children.Remove(last);
        _lastReply = _messages.LastOrDefault(m => m.Role == "assistant")?.Content ?? "";
        _ = Ask();
    }

    void OnCopy(object sender, RoutedEventArgs e)
    {
        if (_lastReply.Length == 0) return;
        try { Clipboard.SetDataObject(_lastReply, true); hintText.Text = "Copied"; }
        catch (System.Runtime.InteropServices.ExternalException) { hintText.Text = "The clipboard is busy; try again"; }
    }

    void OnInsert(object sender, RoutedEventArgs e) => Insert();

    void Insert()
    {
        if (Busy || _lastReply.Length == 0) return;
        RingDeck.Windows.Focus.Restore(_target);   // while this window is still in front, Windows lets us hand focus back
        var text = _lastReply;
        Close();
        _insert(_target, text);
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (e.Key == Key.Escape)
        {
            if (Busy) _cts?.Cancel(); else Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && ctrl) { Insert(); e.Handled = true; }
        else if (e.Key == Key.Enter && !shift && inputBox.IsKeyboardFocusWithin) { Send(inputBox.Text); e.Handled = true; }
    }

    /// <summary>Developer aid for --ai-demo: waits for the reply, then saves a picture of the window.</summary>
    internal async Task SaveWhenDoneAsync(string path, TimeSpan limit)
    {
        var until = DateTime.Now + limit;
        while ((Busy || _lastReply.Length == 0) && DateTime.Now < until) await Task.Delay(100);
        await Task.Delay(300);
        UpdateLayout();
        var root = (FrameworkElement)Content;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)root.ActualWidth + 28, (int)root.ActualHeight + 24, 96, 96, PixelFormats.Pbgra32);
        var back = new DrawingVisual();
        using (var dc = back.RenderOpen()) dc.DrawRectangle(Theme.Frozen(Color.FromRgb(0x20, 0x20, 0x20)), null, new Rect(0, 0, root.ActualWidth + 28, root.ActualHeight + 24));
        bitmap.Render(back);
        bitmap.Render(root);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = System.IO.File.Create(path);
        encoder.Save(file);
    }
}
