using RingDeck.Core;
using RingDeck.Windows;
using WinForms = System.Windows.Forms;

namespace RingDeck.App;

public sealed class TrayIcon : INotifier, IDisposable
{
    readonly WinForms.NotifyIcon _icon;

    public TrayIcon(Action openEditor, Action<bool> setPaused, Action exit)
    {
        var pause = new WinForms.ToolStripMenuItem("Pause shortcuts") { CheckOnClick = true };
        pause.CheckedChanged += (_, _) => setPaused(pause.Checked);
        var autostart = new WinForms.ToolStripMenuItem("Start with Windows") { CheckOnClick = true };
        autostart.CheckedChanged += (_, _) => Autostart.Set(autostart.Checked);
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Open editor", null, (_, _) => openEditor());
        menu.Items.Add(pause);
        menu.Items.Add(autostart);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => exit());
        menu.Opening += (_, _) => autostart.Checked = Autostart.IsEnabled();
        _icon = new WinForms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            Text = "RingDeck",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => openEditor();
    }

    public void Notify(string title, string message) =>
        _icon.ShowBalloonTip(5000, title, message, WinForms.ToolTipIcon.Info);

    public void Dispose() { _icon.Visible = false; _icon.Dispose(); }
}
