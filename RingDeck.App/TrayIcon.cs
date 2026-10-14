using RingDeck.Core;
using RingDeck.Windows;
using WinForms = System.Windows.Forms;

namespace RingDeck.App;

public sealed class TrayIcon : INotifier, IDisposable
{
    readonly WinForms.NotifyIcon _icon;
    readonly System.Windows.Threading.Dispatcher _ui = System.Windows.Threading.Dispatcher.CurrentDispatcher;

    public TrayIcon(Action openEditor, Action<bool> setPaused, Action exit)
    {
        var pause = new WinForms.ToolStripMenuItem("Pause shortcuts") { CheckOnClick = true };
        pause.CheckedChanged += (_, _) => setPaused(pause.Checked);
        var autostart = new WinForms.ToolStripMenuItem("Start with Windows") { CheckOnClick = true };
        var syncing = false;
        autostart.CheckedChanged += (_, _) =>
        {
            if (syncing || Autostart.Set(autostart.Checked)) return;
            syncing = true;
            autostart.Checked = Autostart.IsEnabled();
            syncing = false;
            Notify("Start with Windows is switched off in Windows", "Turn RingDeck on in Settings > Apps > Startup.");
        };
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Open editor", null, (_, _) => openEditor());
        menu.Items.Add(pause);
        menu.Items.Add(autostart);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        if (!AppPackage.IsPackaged)                     // Store rules on payment links are strict; keep the Store version clean
            menu.Items.Add("Buy me a coffee ☕", null, (_, _) => OpenLink("https://paypal.me/rykou893"));
        menu.Items.Add("Exit", null, (_, _) => exit());
        menu.Opening += (_, _) => { syncing = true; autostart.Checked = Autostart.IsEnabled(); syncing = false; };
        _icon = new WinForms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            Text = "RingDeck",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => openEditor();
    }

    /// <summary>Safe from any thread, and never blocks the caller.</summary>
    static void OpenLink(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(); }
        catch (System.ComponentModel.Win32Exception) { }   // no default browser
    }

    public void Notify(string title, string message) =>
        _ui.BeginInvoke(() => _icon.ShowBalloonTip(5000, title, message, WinForms.ToolTipIcon.Info));

    public void Dispose() { _icon.Visible = false; _icon.Dispose(); }
}
