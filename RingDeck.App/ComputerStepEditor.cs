using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RingDeck.Core;
using RingDeck.Windows;

namespace RingDeck.App;

public partial class EditorWindow
{
    const string ComputerExample = "1. Open Edge\n2. Click Gmail in the favourites bar\n3. Click Inbox\n4. Read the top 10 emails\n5. Give me a short summary";

    /// <summary>A new Use the computer step uses the first connection that can use tools, if there is one.</summary>
    internal ComputerStep NewComputer() => new()
    {
        ConnectionId = (Config.AiConnections.FirstOrDefault(c => AiTools.Supports(c.Format)) ?? Config.AiConnections.FirstOrDefault())?.Id ?? Guid.Empty,
        Instruction = ComputerExample,
    };

    /// <summary>Use the computer, laid out for the flow window's side panel.</summary>
    void ComputerForm(Panel host, ComputerStep step, Action rebuild)
    {
        var add = host.Children;
        var subtle = (Brush)Application.Current.Resources["SubtleBrush"];
        TextBlock Hint(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = subtle, FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };
        TextBlock Warning(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Orange, Margin = new Thickness(0, 6, 0, 0) };

        if (AgentDesktop.Find(Config.Settings.AgentDesktopPath) is null)
            add.Add(Warning(Config.Settings.AgentDesktopPath.Length > 0
                ? $"agent-desktop was not found at {Config.Settings.AgentDesktopPath}. Fix its location in Settings."
                : "agent-desktop is not installed, so this card can't run yet. Install it (it goes in your tools\\agent-desktop folder), or set its location in Settings."));

        if (ConnectionPicker(add, step, rebuild) is { } chosen)
            add.Add(AiTools.Supports(chosen.Format)
                ? Hint($"Model: {(chosen.Model.Trim().Length > 0 ? chosen.Model.Trim() : "not set")}. Pick a capable one: it has to plan, read pages and use tools.")
                : Warning("This connection uses the custom format, which can't use tools. Pick an OpenAI, Anthropic or Gemini connection."));

        add.Add(Caption("Steps to follow"));
        add.Add(Bound(new TextBox
        {
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            ToolTip = "Write the exact steps, one per line, the way you'd tell someone sitting at your computer.",
        }, TextBox.TextProperty, step, nameof(ComputerStep.Instruction)));
        add.Add(Hint("Write the exact steps, one per line, for example:\n" + ComputerExample
            + "\nNumbered steps make a failure easy to place: the message says which step it stopped at."));

        add.Add(Caption("Limits"));
        var limits = new StackPanel { Orientation = Orientation.Horizontal };
        TextBlock Word(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
        var first = Word("At most");
        first.Margin = new Thickness(0, 0, 6, 0);
        limits.Children.Add(first);
        limits.Children.Add(Bound(new TextBox { Width = 48, ToolTip = "How many things it may do (reads, clicks, key presses) before it gives up" },
            TextBox.TextProperty, step, nameof(ComputerStep.MaxActions)));
        limits.Children.Add(Word("actions and"));
        limits.Children.Add(Bound(new TextBox { Width = 40, ToolTip = "How long it may take before it gives up" },
            TextBox.TextProperty, step, nameof(ComputerStep.MaxMinutes)));
        limits.Children.Add(Word("minutes"));
        add.Add(limits);

        var real = Bound(new CheckBox
        {
            Content = "Use the real mouse and keyboard", Margin = new Thickness(0, 10, 0, 0),
            ToolTip = "Off: it works without moving your mouse or taking over the keyboard, so you can keep working.\nOn: for apps that don't respond otherwise. Don't use the computer while it runs.",
        }, CheckBox.IsCheckedProperty, step, nameof(ComputerStep.RealInput));
        add.Add(real);

        add.Add(Bound(new CheckBox
        {
            Content = "Let it type into windows and run commands", Margin = new Thickness(0, 6, 0, 0),
            ToolTip = "For terminals and windows without a text field: it types with the real keyboard, so don't use the computer meanwhile.",
        }, CheckBox.IsCheckedProperty, step, nameof(ComputerStep.AllowTyping)));
        add.Add(Hint("Only tick this for cards that need it, such as running a script in a terminal. It is told to type only what your steps ask for, "
            + "but a card that also reads web pages or emails could be tricked into typing something else."));

        add.Add(new Border
        {
            Margin = new Thickness(0, 16, 0, 0), Padding = new Thickness(12, 10, 12, 10), CornerRadius = new CornerRadius(8),
            Background = Theme.Frozen(Theme.Accent, 0.12),
            Child = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = "Unless you let it type into windows, it only opens, reads and moves around: it is told never to send, delete, buy or change anything. "
                    + "When it's done, its result replaces what's on the clipboard. If a step fails, a message saying which step and why goes there instead, "
                    + "so a Send to web card after it can always send {clipboard} to your phone.\n\n"
                    + "What it reads on screen is sent to your AI service. Windows doesn't let it read apps while the computer is locked.",
            },
        });

        var log = new Button { Content = "Open the log", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0),
            ToolTip = "Every action it took, with the time. What it read or typed is not kept." };
        log.Click += (_, _) => OpenComputerLog();
        add.Add(log);
    }

    static void OpenComputerLog()
    {
        var path = App.ComputerLogPath;
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "");
        }
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
    }
}
